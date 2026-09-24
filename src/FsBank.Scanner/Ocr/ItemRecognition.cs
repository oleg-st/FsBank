using FsBank.Scanner.Export;
using FsBank.Scanner.Imaging;
using FsBank.Scanner.Items;
using FsBank.Scanner.Scanning;
using FsBank.Scanner.Scanning.Batch;

using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FsBank.Scanner.Ocr;

internal static class ItemRecognition
{
    public static Task<string> Run(string folder, Action<string> log, CancellationToken token) =>
        Task.Run(() => RunCore(folder, log, token), token);

    private static string RunCore(string folder, Action<string> log, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string session = CaptureSessions.Resolve(folder);
        if(File.Exists(Path.Combine(session,"batch.json")))
            return BatchRecognition.Run(session,tab=>RunSingle(tab,log,token),log,token);
        return RunSingle(session,log,token);
    }
    private static string RunSingle(string session, Action<string> log, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string output = session;
        Directory.CreateDirectory(output);
        var timer = Stopwatch.StartNew();
        using var reader = new NativeTextReader();
        var results = Directory.EnumerateFiles(session, "r??_c??.png").Order(StringComparer.Ordinal)
            .Select(file => RecognizeFile(file, reader, log, token)).ToArray();
        return WriteReport(session, results, timer.Elapsed.TotalSeconds, log);
    }
    internal sealed record Result(string File, object Data, string Card, int Lines, string? Issue = null, ScanIssueEvidence? Evidence = null);
    internal static Result RecognizeFile(string file, NativeTextReader reader, Action<string> log, CancellationToken token)
    {
        string output = Path.GetDirectoryName(file)!;
        Directory.CreateDirectory(output);
        var cards = new StringBuilder();
        token.ThrowIfCancellationRequested();
        var pixels = Pixels.Load(file);
        var bands = TooltipSegmenter.Find(pixels,excludeHeaderDecoration:true);
        List<RecognizedLine> lines = [];
        bool title = true;
        bool details = false;
        int previousBottom = 0;
        foreach (var band in bands)
        {
            token.ThrowIfCancellationRequested();
            if (band.Index > 0 && band.Box.Top - previousBottom > 12) title = false;
            var read = reader.Read(pixels, band.Box, title, details);
            if (read.Text.StartsWith("Power Potential")) details = true;
            previousBottom = band.Box.Bottom;
            var repaired = TextGeometry.RestoreSpaces(read.Text, reader.Symbols, pixels, band.Box);
            if(reader.PrimaryText!=read.Text)
                repaired.Corrections.Insert(0,new("ocr_variant_agreement",0,reader.PrimaryText,read.Text,0));
            lines.Add(new(band.Index, band.Box, band.Color, repaired.Text, read.Confidence)
                { Symbols = [..reader.Symbols], RawText = reader.PrimaryText, RecognitionText=read.Text, VerifiedConfidence=reader.VerifiedConfidence, Corrections = repaired.Corrections });
        }
        var item = TooltipParser.Parse(lines);
        bool captureHeaderComplete=TooltipSegmenter.HasCompleteTitle(pixels);
        if(!captureHeaderComplete)item.Warn("Incomplete capture header; rescan required");
        bool captureLayoutComplete=TooltipSegmenter.HasReadableLayout(pixels);
        if(!captureLayoutComplete)item.Warn("Incomplete capture text layout; rescan required");
        string name = Path.GetFileNameWithoutExtension(file);
        using (var annotated = pixels.Bitmap())
        {
            using (var g = Graphics.FromImage(annotated))
            using (var pen = new Pen(Color.Lime, 1))
                foreach (var band in bands) g.DrawRectangle(pen, band.Box);
            annotated.Save(Path.Combine(output, name + "-lines.png"));
        }
        var data = new { file = Path.GetFileName(file), sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))),
            status = !captureHeaderComplete || !captureLayoutComplete ? "rescan_required" : item.ReviewWarnings.Count > 0 ? "needs_visual_review" : "recognized",
            reviewWarnings = item.ReviewWarnings, reviewLines = item.ReviewLines, captureHeaderComplete, captureLayoutComplete, lines, item };
        cards.Append($"<article><h2>{name}</h2><div class='pair'><img src='{name}-lines.png' alt='Detected lines'><img src='data:image/png;base64,{Convert.ToBase64String(File.ReadAllBytes(file))}' alt='Source'></div><pre>{WebUtility.HtmlEncode(JsonSerializer.Serialize(item, JsonOptions))}</pre><table><tr><th>Line</th><th>Color</th><th>Confidence</th><th>Raw text</th></tr>");
        foreach (var line in lines) cards.Append($"<tr><td>{line.Index}</td><td>{line.Color}</td><td>{line.Confidence}</td><td>{WebUtility.HtmlEncode(line.Text)}"
            + (line.Corrections.Count > 0 ? $"<details><summary>OCR corrections: {line.Corrections.Count}</summary>Raw OCR: {WebUtility.HtmlEncode(line.RawText)}<br>{WebUtility.HtmlEncode(JsonSerializer.Serialize(line.Corrections))}</details>" : "") + "</td></tr>");
        cards.Append("</table></article>");

        log($"{name}: {lines.Count} lines, {item.Stats.Count} stats, {item.Modifiers.Count} modifier blocks");
        return new(file, data, cards.ToString(), lines.Count,
            item.ReviewWarnings.Count == 0 ? null : string.Join("; ", item.ReviewWarnings.Distinct()),
            item.ReviewWarnings.Count == 0 ? null : ScanIssueEvidence.Create(file,pixels.Width,pixels.Height,lines,item));

    }
    internal static string WriteReport(string session, IEnumerable<Result> results, double seconds, Action<string> log)
    {
        string output = session;
        Directory.CreateDirectory(output);
        var ordered = results.OrderBy(r => r.File, StringComparer.Ordinal).ToArray();
        var items = ordered.Select(r => r.Data).ToList();
        var cards = new StringBuilder(string.Concat(ordered.Select(r => r.Card)));
        int lineCount = ordered.Sum(r => r.Lines);
        int reviewCount = ordered.Count(r => r.Issue is not null);
        var summary = new { items = items.Count, lines = lineCount, seconds = seconds,
            engine = "Tesseract native / title 2x, detail 3x / contrast + padding / measured gaps / DAWG dictionaries disabled",
            reviewItems = reviewCount, status = reviewCount > 0 ? "needs_visual_review" : "recognized",
            scope = "Alt tooltip prototype. Modifier descriptions retained verbatim; not schema v2. No font atlas yet." };
        File.WriteAllText(Path.Combine(output, "full.json"), JsonSerializer.Serialize(new { summary, items }, JsonOptions));
        CompactExport.Write(output, items.Select(item => JsonSerializer.SerializeToElement(item)));
        string report = Path.Combine(output, "report.html");
        File.WriteAllText(report, "<!doctype html><meta charset='utf-8'><title>FsBank — C# OCR</title><style>body{background:#141c25;color:#eee;font:15px system-ui;margin:24px}article{border-top:1px solid #789;padding:20px 0}img{max-width:45%;object-fit:contain;align-self:start}.pair{display:flex;gap:20px}td,th{text-align:left;padding:4px 12px;border-bottom:1px solid #456}pre{white-space:pre-wrap}</style><h1>Alt tooltip recognition — C#</h1><p><a href='items.html'>Compact items</a></p><p>Items needing review: " + reviewCount + ". Review warnings concern exported data or incomplete captures. Raw OCR diagnostics are retained below.</p><pre>" + WebUtility.HtmlEncode(JsonSerializer.Serialize(summary, JsonOptions)) + "</pre>" + cards);
        log($"Done: {items.Count} items in {seconds:F1} s.");
        return report;
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
