using FsBank.Scanner.Export;
using FsBank.Scanner.Ocr;

using System.Text.Json;
namespace FsBank.Scanner.Diagnostics.Checks;
internal static class OcrPipelineCheck
{
    public static void Run(string sample, string output)
    {
        Directory.CreateDirectory(output);
        var pipeline = new OcrPipeline(_ => {}, 2);
        File.WriteAllText(Path.Combine(output, "batch.json"), JsonSerializer.Serialize(new
        { Status = "cancelled", Tabs = new[] { new { Tab = 1, Status = "completed" }, new { Tab = 2, Status = "cancelled" }, new { Tab = 3, Status = "completed" } } }));
        for (int tab = 1; tab <= 3; tab++)
        {
            string session = Path.Combine(output, $"tab-{tab:00}");
            Directory.CreateDirectory(session);
            File.WriteAllText(Path.Combine(session, "session.json"), "{}");
            pipeline.Register(session);
            if (tab == 3) continue;
            string file = Path.Combine(session, "r01_c01.png");
            File.Copy(sample, file, true);
            pipeline.Enqueue(file);
        }
        pipeline.Complete();
        using var report = JsonDocument.Parse(File.ReadAllText(CompactExport.FullReportPath(output)));
        var items = report.RootElement.GetProperty("items").EnumerateArray().ToArray();
        if (items.Length != 2 || items[0].GetProperty("file").GetString() != "tab-01/r01_c01.png"
            || items[1].GetProperty("file").GetString() != "tab-02/r01_c01.png")
            throw new InvalidOperationException("Shared queue lost or mixed tabs.");
        using var empty = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "tab-03", "full.json")));
        if (empty.RootElement.GetProperty("items").GetArrayLength() != 0) throw new InvalidOperationException("Empty tab failed.");
        using var compact = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "items.json")));
        var exported = compact.RootElement.EnumerateArray().ToArray();
        if (exported.Length != 2 || exported[0].GetProperty("location").GetProperty("tab").GetInt32() != 1
            || exported[1].GetProperty("location").GetProperty("tab").GetInt32() != 2
            || exported.Any(item => item.GetProperty("location").GetProperty("row").GetInt32() != 1
                || item.GetProperty("location").GetProperty("column").GetInt32() != 1 || item.TryGetProperty("lines", out _)))
            throw new InvalidOperationException("Compact export lost coordinates or retained OCR diagnostics.");
        using var compactEmpty = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "tab-03", "items.json")));
        if (compactEmpty.RootElement.GetArrayLength() != 0) throw new InvalidOperationException("Compact empty tab failed.");
        File.WriteAllText(Path.Combine(output, "checks.txt"), "Passed: shared queue, identical filenames across tabs, empty tab, drain and merged report for cancelled batch.");
    }
}
