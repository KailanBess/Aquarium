using Aquarium;
using System.Text;

internal static class ReliabilityChecks
{
    internal static async Task Run(string root)
    {
        void Check(bool valid, string label) { if (!valid) throw new Exception(label); }
        void Refuses(Action action)
        {
            try { action(); } catch (InvalidDataException) { return; }
            throw new Exception("Unsafe backup was accepted.");
        }
        var profiles = Path.Combine(root, "suite-profiles");
        var quietPolicy = Path.Combine(root, "quiet.json");
        File.WriteAllText(quietPolicy, "{\"Enabled\":true,\"Start\":22,\"End\":8,\"Apps\":[\"FishBowl\"]}");
        Check(AquariumFamily.SuiteNoticePolicy.Read(quietPolicy, "FishBowl", 23) && AquariumFamily.SuiteNoticePolicy.Read(quietPolicy, "FishBowl", 2) &&
            !AquariumFamily.SuiteNoticePolicy.Read(quietPolicy, "FishBowl", 12) && !AquariumFamily.SuiteNoticePolicy.Read(quietPolicy, "FishTank", 23), "Quiet-hour bounds or per-app selection failed.");
        File.WriteAllText(quietPolicy, "{invalid}");
        Check(!AquariumFamily.SuiteNoticePolicy.Read(quietPolicy, "FishBowl", 23), "Invalid shared preferences silenced notices.");
        Directory.CreateDirectory(Path.Combine(profiles, "FishNet"));
        File.WriteAllText(Path.Combine(profiles, "FishNet", "media-library.json"), "[]");
        File.WriteAllText(Path.Combine(profiles, "FishNet", "appearance.json"), "{\"Background\":\"Slate\",\"Compact\":true,\"FutureField\":42}");
        var family = FamilyAppearance.Preview(profiles, "Midnight", "Blue", ["FishNet"]);
        using (var palette = System.Text.Json.JsonDocument.Parse(family.Single().Data))
            Check(palette.RootElement.GetProperty("Compact").GetBoolean() && palette.RootElement.GetProperty("FutureField").GetInt32() == 42 && palette.RootElement.GetProperty("Accent").GetString() == "Aquarium Blue", "Family palette changed non-appearance settings or discarded future fields.");
        var records = SuiteBackup.Capture(profiles, ["FishNet"]);
        var radioPath = Path.Combine(profiles, "FishNet", "radio-stations.json");
        File.WriteAllText(radioPath, "[]");
        var radioRecords = SuiteBackup.Capture(profiles, ["FishNet"]);
        Check(radioRecords.Any(record => record.File == "radio-stations.json"), "Saved radio stations were omitted from app-record backups.");
        File.WriteAllText(radioPath, "[1]");
        SuiteBackup.Restore(profiles, radioRecords);
        Check(File.ReadAllText(radioPath) == "[]", "Saved radio stations were not restored.");
        File.Delete(radioPath);
        const string password = "fixture passphrase used only in generated tests";
        var encrypted = SuiteBackup.Encrypt(records, password);
        var decoded = SuiteBackup.Decrypt(encrypted, password);
        Check(decoded.Count == 2 && decoded[0].Data.SequenceEqual(records[0].Data), "Encrypted backup did not round trip.");
        Refuses(() => SuiteBackup.Decrypt(encrypted, "a different fixture passphrase"));
        encrypted[^1] ^= 1;
        Refuses(() => SuiteBackup.Decrypt(encrypted, password));
        Refuses(() => SuiteBackup.Encrypt([new("FishNet", "../outside.json", Encoding.UTF8.GetBytes("{}"))], password));
        Refuses(() => SuiteBackup.Encrypt([new("FishTank", "access.json", Encoding.UTF8.GetBytes("{}"))], password));
        Refuses(() => SuiteBackup.Encrypt([records[0], records[0]], password));
        var target = Path.Combine(profiles, "FishNet", "media-library.json");
        File.WriteAllText(target, "{corrupt original}");
        var corruptBefore = SuiteBackup.Capture(profiles, ["FishNet"], requireReadableJson: false);
        var recoveredEvidence = SuiteBackup.Decrypt(SuiteBackup.Encrypt(corruptBefore, password), password);
        Check(Encoding.UTF8.GetString(recoveredEvidence.First(r => r.File == "media-library.json").Data) == "{corrupt original}", "Before-restore recovery failed to preserve a corrupt original.");
        SuiteBackup.Clear(corruptBefore); SuiteBackup.Clear(recoveredEvidence);
        File.WriteAllText(target, "[1]");
        SuiteBackup.Restore(profiles, decoded);
        Check(File.ReadAllText(target) == "[]", "Restore did not publish the reviewed record.");
        var second = Path.Combine(profiles, "FishNet", "playlists.json");
        File.WriteAllText(target, "[1]"); File.WriteAllText(second, "[2]");
        FileStream? locked = null;
        try
        {
            SuiteBackup.Restore(profiles, [decoded[0], new("FishNet", "playlists.json", Encoding.UTF8.GetBytes("[]"))],
                () => locked = File.Open(second, FileMode.Open, FileAccess.Read, FileShare.None));
            throw new Exception("A locked destination did not stop restore.");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        finally { locked?.Dispose(); }
        Check(File.ReadAllText(target) == "[1]" && File.ReadAllText(second) == "[2]", "Interrupted restore did not retain the original records.");
        Check(Directory.GetFiles(Path.GetDirectoryName(target)!, "*.tmp").Length == 0, "Restore left temporary plaintext files.");
        using (var lease = ProfileLease.Acquire(Path.Combine(root, "lease")))
        {
            try { using var duplicate = ProfileLease.Acquire(Path.Combine(root, "lease")); throw new Exception("Two processes can edit one profile."); }
            catch (IOException) { }
        }
        using (ProfileLease.Acquire(Path.Combine(root, "lease"))) { }
        var store = new LocalStore(Path.Combine(root, "cleanup-registry")); await store.LoadAsync();
        var appRoot = Path.Combine(root, "cleanup-apps");
        var source = GitHubPackages.Sources[0];
        var retained = Path.Combine(appRoot, source.AppId, Guid.NewGuid().ToString("N"));
        var unused = Path.Combine(appRoot, source.AppId, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(retained); Directory.CreateDirectory(unused);
        File.WriteAllText(Path.Combine(retained, source.Executable), "inert retained fixture");
        File.WriteAllText(Path.Combine(unused, source.Executable), "inert unused fixture");
        await store.RecordInstallationAsync(new(source.AppId, "fixture", retained, DateTimeOffset.Now, source.Executable));
        var installer = new PackageInstaller(store, appRoot);
        var candidates = installer.FindUnusedInstallations();
        Check(candidates.Count == 1 && candidates[0].Directory == unused, "Cleanup offered a registered version.");
        File.WriteAllText(Path.Combine(unused, "portable.flag"), "");
        try { installer.FindUnusedInstallations(); throw new Exception("Cleanup offered portable data."); }
        catch (InvalidOperationException) { }
        File.Delete(Path.Combine(unused, "portable.flag"));
        foreach (var tool in GitHubPackages.Sources.Where(s => s.AssetName.Length > 0))
        {
            Check(store.Catalog.Any(a => a.Id == tool.AppId) && tool.Repository == "KailanBess/Aquarium" && tool.ChecksumName == "SHA256SUMS-tools.txt", "Optional tool is outside its approved catalog/checksum source.");
            var archivePath = Path.Combine(root, tool.Executable + ".zip");
            using (var archive = System.IO.Compression.ZipFile.Open(archivePath, System.IO.Compression.ZipArchiveMode.Create))
            { using var writer = new StreamWriter(archive.CreateEntry(tool.Executable).Open()); writer.Write("Inert tool fixture, never executed."); }
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(archivePath)));
            var installed = await installer.InstallZipAsync(tool, "v0.5.0", archivePath, hash, new Progress<string>(), default);
            Check(File.Exists(Path.Combine(installed.InstallDirectory, tool.Executable)), "An approved standalone tool package could not be installed.");
        }
        await store.SetVersionPinAsync(source.AppId, true);
        await store.RecordInstallationAsync(store.Registry.Installations.Single(i => i.AppId == source.AppId) with { Version = "retained-fixture" });
        Check(store.GetVersionPin(source.AppId) == "retained-fixture", "A version pin did not follow an explicitly selected retained build.");
        var runtimeRoot = Path.Combine(Path.GetDirectoryName(store.RegistryPath)!, "Runtime");
        foreach (var version in new[] { "0.0.1", "0.0.2", "0.0.3" })
        {
            var directory = Path.Combine(runtimeRoot, version + "-0123456789abcdef"); Directory.CreateDirectory(directory);
            foreach (var file in new[] { "Aquarium.exe", "Aquarium.pri", "Aquarium.runtimeconfig.json" }) File.WriteAllText(Path.Combine(directory, file), "inert runtime fixture; never executed");
        }
        var oldCaches = installer.FindUnusedRuntimes();
        Check(oldCaches.Count == 2 && oldCaches.All(c => !c.Directory.Contains("0.0.3-")), "Runtime cleanup offered the newest previous cache.");
        var oldCache = oldCaches.First();
        using (var heldRuntime = File.Open(Path.Combine(oldCache.Directory, "Aquarium.exe"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            bool refused = false;
            try { await installer.RemoveUnusedRuntimeAsync(oldCache); } catch (IOException) { refused = true; } catch (UnauthorizedAccessException) { refused = true; }
            Check(refused && Directory.GetFiles(oldCache.Directory).Length == 3, "Cleanup changed a runtime before proving exclusive file access.");
        }
        await installer.RemoveUnusedRuntimeAsync(oldCache);
        Check(!Directory.Exists(oldCache.Directory) && Directory.Exists(Path.Combine(runtimeRoot, "0.0.3-0123456789abcdef")), "Runtime cleanup failed or removed the retained previous cache.");
        // Do not invoke process-aware deletion when a real companion might be running.
        SuiteBackup.Clear(records); SuiteBackup.Clear(decoded);
        Console.WriteLine("Suite encryption, tamper rejection, restore rollback, profile lease and cleanup preservation checks passed.");
    }
}
