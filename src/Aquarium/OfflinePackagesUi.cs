using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Aquarium;

public sealed partial class MainWindow
{
    private Button OfflineInstallButton()
    {
        var button = new Button { Content = "Install an offline package", IsEnabled = store.CanSave && installCancellation is null };
        button.Click += async (_, _) => await InstallOfflineAsync();
        return button;
    }

    private Button ExportOfflineButton(CatalogApp app, AppPackage package)
    {
        var button = new Button { Content = "Save offline package", IsEnabled = installCancellation is null };
        button.Click += async (_, _) =>
        {
            if (installCancellation is not null || recoveryRunning || shellUpdateRunning) return;
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(20));
            installCancellation = cancellation;
            try
            {
                var picker = new FileSavePicker { SuggestedFileName = Path.GetFileNameWithoutExtension(package.AssetName) };
                picker.FileTypeChoices.Add("Aquarium offline package", new List<string> { ".aqpkg" });
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                var file = await picker.PickSaveFileAsync();
                if (file is null || isClosed) return;
                ReportInstall("Saving offline package for " + app.Name + "…"); Render();
                await installer.DownloadAsync(package, new Progress<string>(ReportInstall), cancellation.Token, async payload =>
                {
                    await OfflinePackages.ExportAsync(package, payload, file.Path, cancellation.Token);
                    return true;
                });
                ReportInstall("Offline package saved for " + app.Name + " " + package.Version + ". Nothing was installed.");
            }
            catch (Exception ex) { ReportInstall(ex is OperationCanceledException ? "Offline export canceled." : "Offline export failed: " + ex.Message); }
            finally
            {
                downloadHistory.Add($"{DateTimeOffset.Now:g} · {downloadState}");
                installCancellation = null;
                if (!isClosed) Render();
            }
        };
        return button;
    }

    private async Task InstallOfflineAsync()
    {
        if (installCancellation is not null || recoveryRunning || shellUpdateRunning || !store.CanSave) return;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        installCancellation = cancellation;
        try
        {
            var picker = new FileOpenPicker(); picker.FileTypeFilter.Add(".aqpkg");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync();
            if (file is null || isClosed) return;
            ReportInstall("Verifying offline package…"); Render();
            using var prepared = await OfflinePackages.PrepareAsync(file.Path,
                Path.Combine(installer.AppRoot, ".offline"), cancellation.Token);
            if (isClosed) return;
            var manifest = prepared.Manifest;
            var app = store.Catalog.Single(a => a.Id == manifest.AppId);
            if (!store.AllowsPublishedVersion(app.Id, manifest.Version))
                throw new InvalidOperationException("Unpin the installed version before installing a different offline version.");
            var confirm = new ContentDialog
            {
                XamlRoot = Root.XamlRoot, Title = "Install offline " + app.Name + "?",
                Content = $"Version: {manifest.Version}\nRepository recorded in package: {manifest.Repository}\nSize: {manifest.Size / 1048576d:F1} MB\nSHA-256: {manifest.Sha256}\n\nThe payload matches its included checksum. This offline file is not independently publisher-authenticated; install only a package saved from a trusted release. Previous program versions and app data are kept. The app will not start automatically.",
                PrimaryButtonText = "Install reviewed package", CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };
            if (await confirm.ShowAsync() != ContentDialogResult.Primary || isClosed) return;
            cancellation.Token.ThrowIfCancellationRequested();
            if (!store.AllowsPublishedVersion(app.Id, manifest.Version)) throw new InvalidOperationException("The version pin changed. Review the package again.");
            await installer.InstallZipAsync(prepared.Source, manifest.Version, prepared.PayloadPath, manifest.Sha256,
                new Progress<string>(ReportInstall), cancellation.Token);
        }
        catch (Exception ex) { ReportInstall(ex is OperationCanceledException ? "Offline installation canceled. Previous installed version retained." : "Offline installation failed: " + ex.Message); }
        finally
        {
            downloadHistory.Add($"{DateTimeOffset.Now:g} · {downloadState}");
            installCancellation = null;
            if (!isClosed) Render();
        }
    }
}
