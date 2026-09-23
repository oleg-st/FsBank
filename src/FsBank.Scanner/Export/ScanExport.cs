using FsBank.Scanner.Ocr;
using FsBank.Scanner.Scanning.Batch;

using System.Text.Json;
using System.Text.RegularExpressions;

namespace FsBank.Scanner.Export;

// Capture/OCR uses temporary files; only the requested artifacts reach the export folder.
internal sealed class ScanExport(string root, bool debug) : IDisposable
{
    public string WorkingRoot { get; } = Path.Combine(Path.GetTempPath(), "FsBank", Guid.NewGuid().ToString("N"));
    public List<string> Destinations { get; } = [];
    public bool PreserveWorkingFiles { get; private set; }

    public void Complete(Action<string> log)
    {
        try { CompleteCore(log); }
        catch { PreserveWorkingFiles = true; throw; }
    }

    private void CompleteCore(Action<string> log)
    {
        if (!Directory.Exists(WorkingRoot)) return;
        foreach (string capture in Directory.GetDirectories(WorkingRoot))
        {
            // Batch/session metadata and baseline images are created before the
            // first item. They alone do not justify a permanent result folder.
            if (!HasResultData(capture)) continue;
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
                CompactExport.Write(destination, report.RootElement.GetProperty("items").EnumerateArray());
            }
            log("Export: " + destination);
            Destinations.Add(destination);
        }
    }

    private bool HasResultData(string capture)
    {
        foreach(string file in Directory.EnumerateFiles(capture,"*",SearchOption.AllDirectories))
        {
            string name=Path.GetFileName(file);
            if(Regex.IsMatch(name,@"^r\d+_c\d+\.png$",RegexOptions.IgnoreCase))return true;
            // Explicit diagnostics must still help when capturing an item failed.
            if(debug && Path.GetExtension(name).Equals(".png",StringComparison.OrdinalIgnoreCase)
                && !name.Equals("baseline.png",StringComparison.OrdinalIgnoreCase))return true;
            if(name is not ("full.json" or "session.json"))continue;
            using var document=JsonDocument.Parse(File.ReadAllText(file));
            var record=document.RootElement;
            if(name=="full.json" && record.TryGetProperty("items",out var items) && items.GetArrayLength()>0)return true;
            if(name=="session.json" && record.TryGetProperty("Cells",out var cells)
                && cells.EnumerateArray().Any(cell=>cell.TryGetProperty("Status",out var status)
                    && status.GetString() is "captured" or "ocr_failed" or "incomplete_tooltip" or "no_stable_tooltip"))return true;
        }
        return false;
    }

    public void Dispose()
    {
        if (!PreserveWorkingFiles && Directory.Exists(WorkingRoot)) Directory.Delete(WorkingRoot, recursive: true);
    }
}
