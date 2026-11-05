using Aquarium.Launcher;
using System.IO.Compression;
using System.Security.Cryptography;

string hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
byte[] Zip(Dictionary<string, byte[]> files) {
    using var memory = new MemoryStream();
    using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
        foreach (var file in files) { using var output = zip.CreateEntry(file.Key).Open(); output.Write(file.Value); }
    return memory.ToArray();
}
var root = Path.Combine(Path.GetTempPath(), "Aquarium-launch-tests-" + Guid.NewGuid().ToString("N"));
int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
void Reject(Action action, string name) { try { action(); } catch (InvalidDataException) { Check(true, name); return; } throw new Exception("Accepted " + name); }
try {
    var files = new Dictionary<string, byte[]> { ["Aquarium.exe"] = "inert fixture"u8.ToArray(), ["Aquarium.pri"] = "resources"u8.ToArray(), ["Bundled/FishTank.zip"] = "inert companion"u8.ToArray() };
    var payload = Zip(files);
    var manifest = new PayloadManifest("0.4.2", hash(payload), files.Select(f => new PayloadFile(f.Key, f.Value.Length, hash(f.Value))).ToArray());
    string Prepare(byte[] bytes, PayloadManifest m) => RuntimeCache.Prepare(new MemoryStream(bytes), m, root);
    var exe = Prepare(payload, manifest);
    Check(File.ReadAllBytes(exe).SequenceEqual(files["Aquarium.exe"]), "cold extraction and included files");
    var stamp = File.GetLastWriteTimeUtc(exe);
    Check(Prepare(payload, manifest) == exe && File.GetLastWriteTimeUtc(exe) == stamp, "repeat launch reuses verified cache");
    File.WriteAllText(exe, "tampered");
    Check(Prepare(payload, manifest) == exe && File.ReadAllBytes(exe).SequenceEqual(files["Aquarium.exe"]), "corrupt cache repaired");
    File.WriteAllText(Path.Combine(Path.GetDirectoryName(exe)!, "unexpected.dll"), "extra");
    Check(Prepare(payload, manifest) == exe && !File.Exists(Path.Combine(Path.GetDirectoryName(exe)!, "unexpected.dll")), "unlisted runtime file repaired");
    Reject(() => Prepare(payload, manifest with { Sha256 = new string('0', 64) }), "payload digest mismatch");
    Reject(() => Prepare(payload, manifest with { Files = manifest.Files.Append(manifest.Files[0]).ToArray() }), "duplicate manifest path");
    Reject(() => Prepare(payload, manifest with { Files = new[] { new PayloadFile("../escape", 0, hash(Array.Empty<byte>())) } }), "traversal rejected");
    Reject(() => Prepare(payload, manifest with { Files = manifest.Files.Select(f => f with { Size = long.MaxValue }).ToArray() }), "oversized payload rejected");
    var extraZip = Zip(new Dictionary<string, byte[]>(files) { ["extra.dll"] = new byte[0] });
    Reject(() => Prepare(extraZip, manifest with { Sha256 = hash(extraZip) }), "unlisted payload file rejected");
    var badFiles = manifest.Files.Select(f => f with { Sha256 = new string('0', 64) }).ToArray();
    Reject(() => Prepare(payload, manifest with { Files = badFiles }), "individual file digests verified");
    Check(!File.Exists(Path.Combine(root, "escape")), "no escaped files");
    Console.WriteLine($"{passed} launcher checks passed (fixtures never executed).");
} finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
