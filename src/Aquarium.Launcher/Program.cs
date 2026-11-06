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
        using var form = new Form {
            Text = "Aquarium", Width = 420, Height = 140, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, ControlBox = false, StartPosition = FormStartPosition.CenterScreen
        };
        form.Controls.Add(new Label { Text = "Preparing Aquarium…", AutoSize = true, Left = 24, Top = 20 });
        form.Controls.Add(new ProgressBar { Style = ProgressBarStyle.Marquee, Left = 24, Top = 52, Width = 350 });
        form.Shown += async (_, _) => {
            try
            {
                var executable = await Task.Run(() => {
                    var assembly = Assembly.GetExecutingAssembly();
                    using var manifestStream = assembly.GetManifestResourceStream("Aquarium.Payload.json")!;
                    var manifest = JsonSerializer.Deserialize<PayloadManifest>(manifestStream)!;
                    using var payload = assembly.GetManifestResourceStream("Aquarium.Payload.zip")!;
                    return RuntimeCache.Prepare(payload, manifest, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aquarium", "Runtime"));
                });
                var start = new ProcessStartInfo(executable) { WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false };
                start.Environment["AQUARIUM_LAUNCHER"] = Environment.ProcessPath;
                foreach (var argument in args) start.ArgumentList.Add(argument);
                using var process = Process.Start(start) ?? throw new IOException("Aquarium could not start.");
                if (args.Contains("--package-check")) { await process.WaitForExitAsync(); Environment.ExitCode = process.ExitCode; }
            }
            catch (Exception error)
            {
                Environment.ExitCode = 1;
                MessageBox.Show("Aquarium could not prepare its files.\n\n" + error.Message + "\n\nYour saved apps and settings have not been removed.", "Aquarium", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { form.Close(); }
        };
        Application.Run(form);
    }
}
