using Microsoft.UI.Xaml;

namespace Aquarium;

public partial class App : Application
{
    private Window? window;
    public App() => InitializeComponent();
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (Environment.GetCommandLineArgs().Contains("--package-check"))
        {
            try
            {
                var root = AppContext.BaseDirectory;
                foreach (var name in new[] { "Aquarium.pri", "Data/catalog.json", "Bundled/bundled.json", "Bundled/FishTank.zip", "Bundled/FishNet.zip" })
                    if (!System.IO.File.Exists(System.IO.Path.Combine(root, name))) throw new System.IO.IOException("Missing runtime file: " + name);
                using var catalog = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(root, "Data/catalog.json")));
                using var bundles = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(root, "Bundled/bundled.json")));
                if (bundles.RootElement.GetArrayLength() != 2) throw new System.IO.InvalidDataException("Included apps missing.");
                foreach (var bundle in bundles.RootElement.EnumerateArray())
                {
                    using var stream = System.IO.File.OpenRead(System.IO.Path.Combine(root, "Bundled", bundle.GetProperty("file").GetString()!));
                    var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
                    if (!hash.Equals(bundle.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase)) throw new System.IO.InvalidDataException("Included app digest mismatch.");
                }
                Environment.Exit(0);
            }
            catch { Environment.Exit(1); }
            return;
        }
        window = new MainWindow();
        window.Activate();
    }
}
