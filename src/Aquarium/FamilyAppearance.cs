using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aquarium;

public static class FamilyAppearance
{
    public static List<ProfileBackup> Preview(string root, string background, string accent, IEnumerable<string> apps)
    {
        if (!new[] { "Ocean", "Midnight", "Slate" }.Contains(background) || !new[] { "Mint", "Blue", "Coral" }.Contains(accent))
            throw new InvalidDataException("Choose a supported family palette.");
        var result = new List<ProfileBackup>();
        foreach (var app in apps.Distinct())
        {
            if (app is not ("FishNet" or "FishBowl" or "FishTank")) throw new InvalidDataException("Unsupported family appearance target.");
            var file = app == "FishBowl" ? "library.json" : "appearance.json";
            var path = Path.Combine(root, app, file);
            PackageInstaller.RefuseLinks(path);
            if (!File.Exists(path)) continue; // First-run defaults belong to each app.
            if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidDataException("An app profile is too large to preview.");
            var json = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new InvalidDataException("An appearance profile is invalid.");
            if (app == "FishNet") { json["Background"] = background; json["Accent"] = "Aquarium " + accent; }
            else if (app == "FishTank")
            {
                json["Theme"] = "Aquarium " + background; json["Accent"] = "Aquarium " + accent;
                json["CustomPalette"] = false;
            }
            else
            {
                if (json["Theme"] is not JsonObject theme) throw new InvalidDataException("FishBowl has no initialized appearance profile.");
                theme["Name"] = "Aquarium " + background; theme["AccentColor"] = "Aquarium " + accent;
                if (json["Cosmetics"] is JsonObject cosmetics) cosmetics["CustomPalette"] = false;
            }
            result.Add(new(app, file, JsonSerializer.SerializeToUtf8Bytes(json, new JsonSerializerOptions { WriteIndented = true })));
        }
        return result;
    }
}
