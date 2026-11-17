using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Aquarium.Launcher;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var dispatcher = new Control();
        _ = dispatcher.Handle;
        using var context = new ApplicationContext();
        dispatcher.BeginInvoke(new Action(async () =>
        {
            Form? preparation = null;
            try
            {
                var executable = await Task.Run(() =>
                {
                    var assembly = Assembly.GetExecutingAssembly();
                    using var manifestStream = assembly.GetManifestResourceStream("Aquarium.Payload.json")!;
                    var manifest = JsonSerializer.Deserialize<PayloadManifest>(manifestStream)!;
                    using var payload = assembly.GetManifestResourceStream("Aquarium.Payload.zip")!;
                    var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aquarium", "Runtime");
                    return RuntimeCache.Prepare(payload, manifest, root, () =>
                        dispatcher.Invoke(new Action(() =>
                        {
                            preparation = CreatePreparationWindow();
                            preparation.Show();
                        })));
                });
                preparation?.Close();
                var start = new ProcessStartInfo(executable)
                {
                    WorkingDirectory = Path.GetDirectoryName(executable)!,
                    UseShellExecute = false
                };
                start.Environment["AQUARIUM_LAUNCHER"] = Environment.ProcessPath;
                foreach (var argument in args) start.ArgumentList.Add(argument);
                using var process = Process.Start(start) ?? throw new IOException("Aquarium could not start.");
                if (args.Contains("--package-check"))
                {
                    await process.WaitForExitAsync();
                    Environment.ExitCode = process.ExitCode;
                }
            }
            catch (Exception error)
            {
                preparation?.Close();
                Environment.ExitCode = 1;
                MessageBox.Show("Aquarium could not prepare its files.\n\n" + error.Message +
                    "\n\nYour saved apps and settings have not been removed.", "Aquarium",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                preparation?.Dispose();
                context.ExitThread();
            }
        }));
        Application.Run(context);
    }

    private static Form CreatePreparationWindow()
    {
        var form = new Form
        {
            Text = "Aquarium", ClientSize = new Size(400, 104), FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, ControlBox = false, ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterScreen, AutoScaleMode = AutoScaleMode.Dpi
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 16, 20, 16), RowCount = 2, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        layout.Controls.Add(new Label { Text = "Preparing Aquarium…", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft });
        layout.Controls.Add(new ProgressBar { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee,
            AccessibleName = "Preparing the Aquarium runtime" });
        form.Controls.Add(layout);
        return form;
    }
}
