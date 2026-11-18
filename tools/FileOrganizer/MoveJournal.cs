using System.Text.Json;

namespace FileOrganizer;

internal sealed class MoveJournal
{
    private readonly string file;
    private readonly string[] sources;
    private readonly string[] targets;
    internal MoveJournal(IEnumerable<(string Source, string Target)> moves)
    {
        var entries = moves.ToArray(); sources = entries.Select(m => m.Source).ToArray(); targets = entries.Select(m => m.Target).ToArray();
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AquariumFileOrganizer", "Recovery");
        Directory.CreateDirectory(directory);
        file = Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".json");
        Record("Prepared", 0, "");
    }

    internal void Record(string state, int completed, string detail)
    {
        if (!File.Exists(file))
        {
            var temporary = file + ".tmp";
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { JsonSerializer.Serialize(output, new { Sources = sources, Targets = targets, Note = "Reviewed path plan; inspect the matching events.jsonl file for progress. These records are not file-content backups." }); output.Flush(true); }
                File.Move(temporary, file);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        // Append a small event instead of rewriting the complete path plan after every file.
        var bytes = System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { State = state, Completed = completed, Detail = detail }) + Environment.NewLine);
        using var events = new FileStream(file + ".events.jsonl", FileMode.Append, FileAccess.Write, FileShare.Read);
        events.Write(bytes); events.Flush(true);
    }

    internal static string Review()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AquariumFileOrganizer", "Recovery");
        if (!Directory.Exists(directory)) return "No recorded file operations yet.";
        return "Recovery records are kept here: " + directory + Environment.NewLine +
            "These records contain the reviewed source/destination paths. They are not copies of file contents. If an operation was interrupted, inspect both locations before moving anything. A source may already have moved even if its completion was not recorded. Recovery never moves files automatically." + Environment.NewLine + Environment.NewLine +
            string.Join(Environment.NewLine + Environment.NewLine, Directory.EnumerateFiles(directory, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).Take(10).Select(path =>
            {
                var progress = path + ".events.jsonl";
                var events = File.Exists(progress) && new FileInfo(progress).Length <= 2 * 1024 * 1024 ? string.Join(Environment.NewLine, File.ReadLines(progress).TakeLast(10)) : "Open the matching events.jsonl file for progress.";
                return (new FileInfo(path).Length <= 1024 * 1024 ? Path.GetFileName(path) + Environment.NewLine + File.ReadAllText(path) : Path.GetFileName(path) + " (open this large record separately)") + Environment.NewLine + events;
            }));
    }
}
