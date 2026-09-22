using FsBank.Scanner.Scanning.Batch;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;

namespace FsBank.Scanner.Ocr;

// Only file names wait in this queue; PNGs are persisted before admission.
// Each worker owns its native engine, which is never used concurrently.
internal sealed class OcrPipeline
{
    public static int DefaultWorkers => Environment.ProcessorCount >= 4 ? 2 : 1;
    private readonly Channel<string> queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleWriter = true });
    private readonly ConcurrentDictionary<string, ConcurrentBag<ItemRecognition.Result>> sessions = new();
    private readonly ConcurrentQueue<string> errors = new();
    private readonly Task[] workers;
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private readonly Action<string> log;
    private int queued, finished;
    public OcrPipeline(Action<string> log, int? workerCount = null)
    {
        this.log = log;
        int count = workerCount ?? DefaultWorkers;
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(workerCount));
        workers = Enumerable.Range(0, count).Select(_ => Task.Run(async () =>
        {
            try
            {
                using var reader = new NativeTextReader();
                await foreach (string file in queue.Reader.ReadAllAsync())
                {
                    try { sessions[Path.GetDirectoryName(file)!].Add(ItemRecognition.RecognizeFile(file, reader, _ => {}, CancellationToken.None)); }
                    catch (Exception e) { errors.Enqueue(file + ": " + e.Message); }
                    finally { Interlocked.Increment(ref finished); }
                }
            }
            catch (Exception e) { errors.Enqueue("OCR worker: " + e.Message); }
        })).ToArray();
        log($"OCR: {count} background workers.");
    }
    public void Register(string session) => sessions.TryAdd(session, new());
    public void Enqueue(string file)
    {
        if (!queue.Writer.TryWrite(file)) throw new InvalidOperationException("OCR queue is closed.");
        Interlocked.Increment(ref queued);
    }
    public void Complete()
    {
        queue.Writer.TryComplete();
        var tail = Stopwatch.StartNew();
        log($"Capture finished; OCR remaining: {queued - Volatile.Read(ref finished)}.");
        var all = Task.WhenAll(workers);
        while (!all.Wait(1000)) log($"OCR remaining: {queued - Volatile.Read(ref finished)}.");
        foreach (var session in sessions)
        {
            ItemRecognition.WriteReport(session.Key, session.Value, elapsed.Elapsed.TotalSeconds, log);
            File.WriteAllText(Path.Combine(session.Key, "pipeline.json"), JsonSerializer.Serialize(new
            { workers = workers.Length, queued, finished, tailSeconds = tail.Elapsed.TotalSeconds, errors = errors.ToArray(), status = errors.IsEmpty && queued == finished ? "completed" : "failed" }, new JsonSerializerOptions { WriteIndented = true }));
        }
        foreach (string parent in sessions.Keys.Select(s => Path.GetDirectoryName(s)!).Distinct())
            if (File.Exists(Path.Combine(parent, "batch.json")))
                BatchRecognition.Run(parent, s => Path.Combine(s, "report.html"), log, CancellationToken.None);
        log($"OCR finished: {finished}/{queued}; tail {tail.Elapsed.TotalSeconds:F1} s; errors {errors.Count}.");
        if (!errors.IsEmpty || queued != finished) throw new InvalidOperationException("OCR incomplete. See pipeline.json: " + string.Join("; ", errors.Take(3)));
    }
}
