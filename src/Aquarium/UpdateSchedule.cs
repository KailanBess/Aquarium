using System.Text.Json;

namespace Aquarium;

public sealed class UpdateSchedule
{
    public int Hours { get; set; }
    public bool IncludeOutsideApps { get; set; }
    public DateTimeOffset LastAttempt { get; set; }
    public bool IsDue(DateTimeOffset now) => Hours > 0 && now - LastAttempt >= TimeSpan.FromHours(Hours);
    public static string PathOnDisk => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aquarium", "update-schedule.json");
    public static UpdateSchedule Load()
    {
        if (!File.Exists(PathOnDisk)) return new();
        var value = JsonSerializer.Deserialize<UpdateSchedule>(File.ReadAllText(PathOnDisk)) ?? throw new InvalidDataException("Empty update schedule.");
        if (!new[] { 0, 1, 24, 168 }.Contains(value.Hours)) throw new InvalidDataException("Invalid schedule interval.");
        return value;
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PathOnDisk)!);
        File.WriteAllText(PathOnDisk + ".tmp", JsonSerializer.Serialize(this)); File.Move(PathOnDisk + ".tmp", PathOnDisk, true);
    }
}
