using Aquarium.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json;

namespace Aquarium;

public sealed partial class MainWindow
{
    private readonly ActivityHistory activity = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Aquarium", "Activity", "history.json"), "aquarium", ["application.started", "notice.information", "notice.success", "notice.warning", "notice.error"]);
    private readonly CancellationTokenSource activityCancellation = new();
    private int activityStorageState;
    private bool activityReviewRunning;
    private async Task RecordActivityAsync(string code, ActivityLevel level)
    {
        try
        {
            await Task.Run(() => activity.AppendAsync(code, level, AquariumSelfUpdate.CurrentVersion, activityCancellation.Token));
            Interlocked.CompareExchange(ref activityStorageState, 1, 0);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { Volatile.Write(ref activityStorageState, -1); } // Optional logging cannot disable unrelated local features.
    }
    private void AddActivityControls()
    {
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(Text("Local activity history", 20));
        body.Children.Add(Text("Retains up to 500 fixed-code events: app start and notice severity. Message text, exception stacks, paths, library contents and credentials are not recorded. This is not crash collection or desktop-notification history. Nothing uploads automatically. Activity is separate from record backups and the default support report.", 14, true));
        if (Volatile.Read(ref activityStorageState) < 0) body.Children.Add(Text("An activity write failed during this session; some events may be missing. Existing history was preserved and other app features remain available.", 14, true));
        var review = new Button { Content = "Review activity history…" }; review.Click += async (_, _) => await ReviewActivityAsync();
        body.Children.Add(review); PageBody.Children.Add(Panel(body));
    }
    private async Task ReviewActivityAsync()
    {
        if (activityReviewRunning) return;
        activityReviewRunning = true;
        try
        {
            var snapshot = await Task.Run(activity.Read);
            if (isClosed) return;
            var body = new StackPanel { Spacing = 8 };
            body.Children.Add(Text($"{snapshot.Events.Count} retained events · {snapshot.DroppedEntries} older events dropped. Latest first. No free-text error details are stored.", 14, true));
            var display = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 400,
                Text = snapshot.Events.Count == 0 ? "No retained events." : string.Join("\n", snapshot.Events.Reverse().Select(e => $"{e.RecordedAtUtc:yyyy-MM-dd HH:mm:ss 'UTC'} · {e.Level} · {e.Code} · v{e.ApplicationVersion}")) };
            AutomationPropertiesName(display, "Retained local activity events"); body.Children.Add(display);
            var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "Review local activity", Content = body,
                PrimaryButtonText = "Export reviewed JSON…", CloseButtonText = "Close" };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || isClosed) return;
            var picker = new Microsoft.Windows.Storage.Pickers.FileSavePicker(AppWindow.Id)
                { SuggestedFileName = "Aquarium-activity", DefaultFileExtension = ".json" };
            picker.FileTypeChoices.Add("Activity history", new List<string> { ".json" });
            var file = await picker.PickSaveFileAsync(); if (file is null || isClosed) return;
            var profile = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aquarium"))) + Path.DirectorySeparatorChar;
            var program = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)) + Path.DirectorySeparatorChar;
            if (!Path.GetExtension(file.Path).Equals(".json", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFullPath(file.Path).StartsWith(profile, StringComparison.OrdinalIgnoreCase) || Path.GetFullPath(file.Path).StartsWith(program, StringComparison.OrdinalIgnoreCase))
            { ShowNotice("Choose a JSON filename outside Aquarium's profile and program folders.", InfoBarSeverity.Warning); return; }
            var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, new JsonSerializerOptions { WriteIndented = true });
            await AtomicFile.WriteAsync(file.Path, (output, token) => output.WriteAsync(bytes, token).AsTask(), activityCancellation.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!isClosed) ShowNotice("Activity history is unavailable or could not be exported. Existing history was preserved.", InfoBarSeverity.Error); }
        finally { activityReviewRunning = false; }
    }
}
