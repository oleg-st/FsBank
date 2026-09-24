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
    private readonly Action<string>? onQueued;
    private readonly Action<string, ItemRecognition.Result?, Exception?>? onRecognized;
    public OcrPipeline(Action<string> log, int? workerCount = null,
        Action<string>? onQueued = null, Action<string, ItemRecognition.Result?, Exception?>? onRecognized = null)
    {
        this.log = log;
        this.onQueued = onQueued;
        this.onRecognized = onRecognized;
        int count = workerCount ?? DefaultWorkers;
        if (count < 1) throw new ArgumentOutOfRangeException(nameof(workerCount));
        workers = Enumerable.Range(0, count).Select(_ => Task.Run(async () =>
        {
            try
            {
                using var reader = new NativeTextReader();
                await foreach (string file in queue.Reader.ReadAllAsync())
                {
                    ItemRecognition.Result? result = null;
                    Exception? failure = null;
                    try { result = ItemRecognition.RecognizeFile(file, reader, _ => {}, CancellationToken.None); sessions[Path.GetDirectoryName(file)!].Add(result); }
                    catch (Exception e) { failure = e; errors.Enqueue(file + ": " + e.Message); }
                    finally { Interlocked.Increment(ref finished); }
                    onRecognized?.Invoke(file, result, failure);
                }
            }
            catch (Exception e) { errors.Enqueue("OCR worker: " + e.Message); }
        })).ToArray();
        log($"OCR: {count} background workers.");
    }
    public void Register(string session) => sessions.TryAdd(session, new());
    public void Enqueue(string file)
    {
        // Publish admission before a fast worker can publish its result.
        onQueued?.Invoke(file);
        if (!queue.Writer.TryWrite(file))
        {
            var error=new InvalidOperationException("OCR queue is closed.");
            onRecognized?.Invoke(file,null,error);throw error;
        }
        Interlocked.Increment(ref queued);
    }
    public void Complete()
    {
        queue.Writer.TryComplete();
        var tail = Stopwatch.StartNew();
        log($"Capture finished; OCR remaining: {queued - Volatile.Read(ref finished)}.");
        var all = Task.WhenAll(workers);
        while (!all.Wait(1000)) log($"OCR remaining: {queued - Volatile.Read(ref finished)}.");
        // A worker can fail during native OCR initialization. Surface every stranded
        // capture as an issue instead of leaving the UI in "recognizing" forever.
        while(queue.Reader.TryRead(out string? file))
        {
            var failure=new InvalidOperationException("OCR workers could not process this capture.");
            errors.Enqueue(file+": "+failure.Message);Interlocked.Increment(ref finished);
            onRecognized?.Invoke(file,null,failure);
        }
        double drainSeconds = tail.Elapsed.TotalSeconds;
        log($"Finishing: OCR drain {drainSeconds:F3} s.");
        var reportTimer = Stopwatch.StartNew();
        foreach (var session in sessions)
        {
            var sessionTimer = Stopwatch.StartNew();
            ItemRecognition.WriteReport(session.Key, session.Value, elapsed.Elapsed.TotalSeconds, log);
            log($"Finishing: {Path.GetFileName(session.Key)} reports {sessionTimer.Elapsed.TotalSeconds:F3} s.");
            File.WriteAllText(Path.Combine(session.Key, "pipeline.json"), JsonSerializer.Serialize(new
            { workers = workers.Length, queued, finished, tailSeconds = tail.Elapsed.TotalSeconds, drainSeconds, reportSeconds = sessionTimer.Elapsed.TotalSeconds, errors = errors.ToArray(), status = errors.IsEmpty && queued == finished ? "completed" : "failed" }, new JsonSerializerOptions { WriteIndented = true }));
        }
        log($"Finishing: session reports total {reportTimer.Elapsed.TotalSeconds:F3} s.");
        reportTimer.Restart();
        foreach (string parent in sessions.Keys.Select(s => Path.GetDirectoryName(s)!).Distinct())
            if (File.Exists(Path.Combine(parent, "batch.json")))
                BatchRecognition.Run(parent, s => Path.Combine(s, "report.html"), log, CancellationToken.None);
        log($"Finishing: batch reports {reportTimer.Elapsed.TotalSeconds:F3} s.");
        log($"OCR finished: {finished}/{queued}; tail {tail.Elapsed.TotalSeconds:F1} s; errors {errors.Count}.");
        if (!errors.IsEmpty || queued != finished) throw new InvalidOperationException("OCR incomplete. See pipeline.json: " + string.Join("; ", errors.Take(3)));
    }
}
