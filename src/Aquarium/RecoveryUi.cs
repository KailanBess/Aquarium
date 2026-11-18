using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Aquarium;

public sealed partial class MainWindow
{
    private static readonly string ProfileRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private bool recoveryRunning;

    private void RequireCompanionsClosed()
    {
        foreach (var name in new[] { "FishBowl", "FishNet", "FishTank", "DeviceInventory", "WorkspaceLauncher" })
        {
            var processes = Process.GetProcessesByName(name);
            try { if (processes.Any(p => !p.HasExited)) throw new InvalidOperationException("Close " + name + " before backing up or restoring app records."); }
            finally { foreach (var process in processes) process.Dispose(); }
        }
    }

    private void RenderRecovery()
    {
        Empty("Repair and recovery", "Verify an installed app, return to a retained version, or back up selected app records. Your games, media and save files use their app’s own backup tools.");
        foreach (var app in store.Catalog)
        {
            var installed = store.Registry.Installations.SingleOrDefault(i => i.AppId == app.Id);
            if (installed is null) continue;
            var body = new StackPanel { Spacing = 10 };
            body.Children.Add(Text(app.Name + " · " + installed.Version, 22));
            body.Children.Add(PackageCheckButton(app, installed));
            AddRollbackControls(body, app);
            PageBody.Children.Add(Panel(body));
        }
        AddBackupControls();
        var cleanup = new Button { Content = "Review unused program folders", IsEnabled = store.CanSave && !recoveryRunning && installCancellation is null && !shellUpdateRunning };
        cleanup.Click += async (_, _) =>
        {
            if (installCancellation is not null || recoveryRunning || shellUpdateRunning) return;
            recoveryRunning = true; cleanup.IsEnabled = false;
            try
            {
                var candidates = await Task.Run(installer.FindUnusedInstallations);
                if (candidates.Count == 0) { ShowNotice("No unused managed program folders were found. All current and retained rollback versions are kept.", InfoBarSeverity.Informational); return; }
                var confirm = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Remove unused program folders?",
                    Content = $"{candidates.Count} unused folders · {candidates.Sum(c => c.Bytes) / 1048576d:F1} MB\n" + string.Join("\n", candidates.Select(c => c.AppId + " / " + Path.GetFileName(c.Directory))) + "\nCurrent installations, retained rollback versions and app profiles are kept.",
                    PrimaryButtonText = "Remove reviewed folders", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
                if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
                foreach (var candidate in candidates) await installer.RemoveUnusedInstallationAsync(candidate);
                ShowNotice("Reviewed unused program folders removed.", InfoBarSeverity.Success);
            }
            catch (Exception ex) { if (!isClosed) ShowNotice("Cleanup stopped. " + ex.Message, InfoBarSeverity.Error); }
            finally { recoveryRunning = false; if (!isClosed) Render(); }
        };
        PageBody.Children.Add(cleanup);
        var diagnostics = new Button { Content = "Preview support report" };
        diagnostics.Click += async (_, _) => await ExportSupportReport();
        PageBody.Children.Add(diagnostics);
    }

    private void AddBackupControls()
    {
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(Text("Encrypted app records", 22));
        body.Children.Add(Text("Choose profiles below. Includes settings and library records; files referenced by those records are not included. FishTank includes appearance only: use its authenticated encrypted backup tools for vaults and security data. Portable profiles are not included. Backups can contain private paths and account information. Keep the passphrase separately.", 14, true));
        var choices = SuiteBackup.Apps.Select(name => new CheckBox { Content = name, IsChecked = name is "Aquarium" or "FishBowl" or "FishNet" }).ToArray();
        foreach (var choice in choices) body.Children.Add(choice);
        var password = new PasswordBox { Header = "Backup passphrase (at least 12 characters)", MaxLength = 1024 };
        var repeated = new PasswordBox { Header = "Repeat passphrase for a new backup", MaxLength = 1024 };
        body.Children.Add(password); body.Children.Add(repeated);
        var export = new Button { Content = "Save encrypted backup", IsEnabled = !recoveryRunning && installCancellation is null && !shellUpdateRunning };
        var restore = new Button { Content = "Review encrypted backup to restore", IsEnabled = export.IsEnabled };
        async Task Run(bool restoring)
        {
            if (recoveryRunning || installCancellation is not null || shellUpdateRunning || busy) return;
            if (password.Password.Length < 12 || !restoring && password.Password != repeated.Password)
            { ShowNotice("Enter at least 12 characters and repeat the same passphrase when creating a backup.", InfoBarSeverity.Warning); return; }
            string passphrase = password.Password;
            var selected = choices.Where(c => c.IsChecked == true).Select(c => (string)c.Content).ToArray();
            if (selected.Length == 0) { ShowNotice("Choose at least one profile.", InfoBarSeverity.Warning); return; }
            recoveryRunning = true; Navigation.IsEnabled = false;
            List<ProfileBackup>? records = null;
            try
            {
                RequireCompanionsClosed();
                if (restoring)
                {
                    var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".aqbackup");
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                    var file = await picker.PickSingleFileAsync(); if (file is null || isClosed) return;
                    if (new FileInfo(file.Path).Length > 128 * 1024 * 1024) throw new InvalidDataException("The backup is too large.");
                    records = await Task.Run(() => SuiteBackup.Decrypt(File.ReadAllBytes(file.Path), passphrase));
                    var chosen = records.Where(r => selected.Contains(r.App)).ToList();
                    var review = SuiteBackup.Review(chosen);
                    if (review.Files == 0) throw new InvalidDataException("The backup has no records for the selected profiles.");
                    var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Restore reviewed app records?",
                        Content = string.Join(", ", review.Apps) + $"\n{review.Files} files · {review.Bytes / 1024d:F1} KB\nExisting records with the same names will be replaced. Referenced file paths may need repair on another computer. An encrypted before-restore backup will be saved beside the selected backup using the same passphrase.",
                        PrimaryButtonText = "Restore records", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close };
                    if (await dialog.ShowAsync() != ContentDialogResult.Primary || isClosed) return;
                    RequireCompanionsClosed();
                    await Task.Run(() =>
                    {
                        var previous = SuiteBackup.Capture(ProfileRoot, review.Apps);
                        try { SuiteBackup.Save(file.Path + ".before-restore-" + Guid.NewGuid().ToString("N") + ".aqbackup", previous, passphrase); }
                        finally { SuiteBackup.Clear(previous); }
                        SuiteBackup.Restore(ProfileRoot, chosen, RequireCompanionsClosed);
                    });
                    await store.LoadAsync(); appearance.Load(); ApplyAppearance();
                    ShowNotice("Reviewed app records restored. An encrypted before-restore backup was saved beside the backup you selected.", InfoBarSeverity.Success);
                }
                else
                {
                    var picker = new FileSavePicker { SuggestedFileName = "Aquarium-records-" + DateTime.Now.ToString("yyyy-MM-dd") };
                    picker.FileTypeChoices.Add("Encrypted Aquarium app records", new List<string> { ".aqbackup" });
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                    var file = await picker.PickSaveFileAsync(); if (file is null || isClosed) return;
                    records = await Task.Run(() => SuiteBackup.Capture(ProfileRoot, selected));
                    var review = SuiteBackup.Review(records);
                    if (review.Files == 0) throw new InvalidDataException("The selected profiles have no records yet.");
                    await Task.Run(() => SuiteBackup.Save(file.Path, records, passphrase));
                    ShowNotice($"Encrypted backup saved: {review.Files} app records. Keep its passphrase separately.", InfoBarSeverity.Success);
                }
            }
            catch (Exception ex) { if (!isClosed) ShowNotice("Recovery operation stopped. " + ex.Message, InfoBarSeverity.Error); }
            finally
            {
                if (records is not null) SuiteBackup.Clear(records);
                passphrase = ""; password.Password = repeated.Password = "";
                recoveryRunning = false; Navigation.IsEnabled = true;
                if (!isClosed) Render();
            }
        }
        export.Click += async (_, _) => await Run(false); restore.Click += async (_, _) => await Run(true);
        body.Children.Add(export); body.Children.Add(restore); PageBody.Children.Add(Panel(body));
    }

    private async Task ExportSupportReport()
    {
        var report = JsonSerializer.Serialize(new { schema = 1, aquariumVersion = AquariumSelfUpdate.CurrentVersion,
            os = Environment.OSVersion.Version.ToString(), architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            registryReadable = store.CanSave, operationActive = installCancellation is not null || shellUpdateRunning,
            apps = store.Registry.Installations.Select(i => new { id = i.AppId, version = i.Version, checksumRecorded = GitHubPackages.IsHash(i.PackageSha256) }),
            retainedVersions = store.Registry.InstallationHistory.Count,
            note = "This report omits file paths, library contents, credentials and raw logs. Package checksums do not establish publisher trust." }, new JsonSerializerOptions { WriteIndented = true });
        var preview = new TextBox { Text = report, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 420 };
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Review support report", Content = preview, PrimaryButtonText = "Save report", CloseButtonText = "Cancel" };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            var picker = new FileSavePicker { SuggestedFileName = "Aquarium-support" };
            picker.FileTypeChoices.Add("Support report", new List<string> { ".json" });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSaveFileAsync(); if (file is not null) await File.WriteAllTextAsync(file.Path, report);
        }
        catch (Exception ex) { ShowNotice("Could not save report. " + ex.Message, InfoBarSeverity.Error); }
    }
}
