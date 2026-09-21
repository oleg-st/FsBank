using System.Text.Json;

namespace FsBank.Scanner;

// Capture/OCR uses temporary files; only the requested artifacts reach the export folder.
internal sealed class ScanExport(string root, bool debug) : IDisposable
{
    public string WorkingRoot { get; } = Path.Combine(Path.GetTempPath(), "FsBank", Guid.NewGuid().ToString("N"));

    public void Complete(Action<string> log)
    {
        if (!Directory.Exists(WorkingRoot)) return;
        foreach (string capture in Directory.GetDirectories(WorkingRoot))
        {
            string source = capture;
            if (!File.Exists(Path.Combine(source, "batch.json")))
            {
                string metadata = Path.Combine(source, "session.json");
                if (!File.Exists(metadata)) continue;
                using var session = JsonDocument.Parse(File.ReadAllText(metadata));
                var record = session.RootElement;
                string location = record.TryGetProperty("LocationType", out var kind) ? kind.GetString() ?? "bank" : "bank";
                int tab = record.TryGetProperty("ActiveTab", out var active) && active.ValueKind == JsonValueKind.Number ? active.GetInt32() : 0;
                var entry = new BatchTab(tab, location)
                {
                    Status = record.GetProperty("Status").GetString() ?? "unknown",
                    Error = record.TryGetProperty("Error", out var error) ? error.GetString() : null
                };
                source = Path.Combine(WorkingRoot, "export-" + Path.GetFileName(capture));
                Directory.CreateDirectory(source);
                Directory.Move(capture, Path.Combine(source, entry.Folder));
                File.WriteAllText(Path.Combine(source, "batch.json"), JsonSerializer.Serialize(new
                { Status = entry.Status, Error = entry.Error, Mode = location, Tabs = new[] { entry } }));
            }
            BatchRecognition.Run(source, s => File.Exists(Path.Combine(s, "full.json"))
                ? Path.Combine(s, "report.html") : ItemRecognition.WriteReport(s, [], 0, log), log, CancellationToken.None);
            string destination = Path.Combine(Path.GetFullPath(root), Path.GetFileName(capture));
            Directory.CreateDirectory(destination);
            if (debug)
            {
                foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                {
                    string target = Path.Combine(destination, Path.GetRelativePath(source, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target);
                }
            }
            else
            {
                using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(source, "full.json")));
                CompactExport.Write(destination, report.RootElement.GetProperty("items").EnumerateArray(), source);
            }
            log("Export: " + destination);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(WorkingRoot)) Directory.Delete(WorkingRoot, recursive: true);
    }
}
