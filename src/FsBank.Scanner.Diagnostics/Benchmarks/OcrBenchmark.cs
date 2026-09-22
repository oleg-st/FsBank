using FsBank.Scanner.Export;
using FsBank.Scanner.Ocr;

using System.Diagnostics;
using System.Text.Json;
namespace FsBank.Scanner.Diagnostics.Benchmarks;
internal static class OcrBenchmark
{
    public static void Run(string source, string output)
    {
        Directory.CreateDirectory(output);
        var files = Directory.GetFiles(source, "r??_c??.png").Order(StringComparer.Ordinal).Take(24).ToArray();
        if (files.Length == 0) throw new InvalidOperationException("No sample PNGs.");
        double interval = 400;
        string metadata = Path.Combine(source, "session.json");
        if (File.Exists(metadata))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(metadata));
            var times = doc.RootElement.GetProperty("Cells").EnumerateArray()
                .Where(c => c.GetProperty("Status").GetString() == "captured" && c.TryGetProperty("Timing", out var t) && t.ValueKind == JsonValueKind.Object)
                .Select(c => c.GetProperty("Timing").GetProperty("TotalMs").GetDouble()).ToArray();
            if (times.Length > 0) interval = times.Average();
        }
        var measurements = new List<object>();
        string? reference = null;
        foreach (int count in new[] { 1, 2, 3, 4 })
        {
            string session = Path.Combine(output, "workers-" + count);
            Directory.CreateDirectory(session);
            var timer = Stopwatch.StartNew();
            var pipeline = new OcrPipeline(Console.WriteLine, count);
            pipeline.Register(session);
            foreach (string file in files)
            {
                string copy = Path.Combine(session, Path.GetFileName(file));
                File.Copy(file, copy, true);
                pipeline.Enqueue(copy);
            }
            pipeline.Complete();
            double seconds = timer.Elapsed.TotalSeconds;
            using var report = JsonDocument.Parse(File.ReadAllText(CompactExport.FullReportPath(session)));
            string items = report.RootElement.GetProperty("items").GetRawText();
            if (reference is null) reference = items;
            else if (reference != items) throw new InvalidOperationException("Parallel OCR changed item results.");
            measurements.Add(new { workers = count, items = files.Length, seconds, itemsPerSecond = files.Length / seconds,
                captureIntervalMs = interval, captureItemsPerSecond = 1000 / interval });
            File.WriteAllText(Path.Combine(output, "benchmark.json"), JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }));
        }
        // Replay capture pacing to measure the actual drain, including report writing.
        foreach (int count in new[] { 1, 2 })
        {
            string session = Path.Combine(output, "paced-" + count);
            Directory.CreateDirectory(session);
            var pipeline = new OcrPipeline(Console.WriteLine, count);
            pipeline.Register(session);
            foreach (string file in files)
            {
                Thread.Sleep((int)interval);
                string copy = Path.Combine(session, Path.GetFileName(file));
                File.Copy(file, copy, true);
                pipeline.Enqueue(copy);
            }
            var drain = Stopwatch.StartNew();
            pipeline.Complete();
            using var report = JsonDocument.Parse(File.ReadAllText(CompactExport.FullReportPath(session)));
            if (reference != report.RootElement.GetProperty("items").GetRawText()) throw new InvalidOperationException("Paced OCR changed results.");
            File.WriteAllText(Path.Combine(session, "drain.json"), JsonSerializer.Serialize(new { workers = count, captureIntervalMs = interval, tailSeconds = drain.Elapsed.TotalSeconds }));
        }
        // A bad PNG must not kill a worker or discard the next admitted item.
        string failure = Path.Combine(output, "failure");
        Directory.CreateDirectory(failure);
        var broken = new OcrPipeline(_ => {}, 1);
        broken.Register(failure);
        File.WriteAllText(Path.Combine(failure, "r00_c00.png"), "not a PNG");
        broken.Enqueue(Path.Combine(failure, "r00_c00.png"));
        string valid = Path.Combine(failure, Path.GetFileName(files[0]));
        File.Copy(files[0], valid, true);
        broken.Enqueue(valid);
        bool failed = false;
        try { broken.Complete(); } catch (InvalidOperationException) { failed = true; }
        using var partial = JsonDocument.Parse(File.ReadAllText(CompactExport.FullReportPath(failure)));
        if (!failed || partial.RootElement.GetProperty("items").GetArrayLength() != 1)
            throw new InvalidOperationException("Failure recovery lost the valid item.");
        File.WriteAllText(Path.Combine(output, "checks.txt"), "Passed: identical results with 1/2/3/4 workers, paced queue drain, recovery after corrupt PNG.");    }
}
