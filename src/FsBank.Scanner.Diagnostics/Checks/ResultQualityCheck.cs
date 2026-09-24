using System.Text.Json;
using System.Text.Json.Nodes;
using FsBank.Scanner.Export;
using FsBank.Scanner.Items;
using FsBank.Scanner.Ocr;
using FsBank.Scanner.Scanning.Batch;
using FsBank.Scanner.Scanning;
using FsBank.Scanner.Game;

namespace FsBank.Scanner.Diagnostics.Checks;

internal static class ResultQualityCheck
{
    public static void Run(string output)
    {
        RecognizedLine L(int i, string text, int confidence = 99) =>
            new(i, new Rectangle(0, i * 20, 300, 16), "white", text, confidence);
        RecognizedLine[] baseline = [
            L(0,"EXAMPLE RING"), L(1,"Ring 360"), L(2,"Regal Tempered 0/8"),
            L(3,"Power Potential 1,200"), L(4,"+12 Intellect"), L(5,"+7 Haste"),
            L(6,"ITEM MODIFIER Slot: 1"), L(7,"Blessing: Example +3"),
            L(8,"Use a [CORE] ability."), L(9,"Left Alt - Show Details")
        ];
        JsonObject Export(ParsedTooltip item) => CompactExport.Project(JsonSerializer.SerializeToElement(new { file="r01_c01.png", item }), 1);
        var expected = Export(TooltipParser.Parse(baseline));
        void Clean(IEnumerable<RecognizedLine> lines, string reason, bool sameExport = true)
        {
            var parsed = TooltipParser.Parse(lines.ToArray());
            Require(parsed.ReviewWarnings.Count == 0, reason + ": " + string.Join("; ", parsed.ReviewWarnings));
            if (sameExport) Require(JsonNode.DeepEquals(expected, Export(parsed)), reason + " changed the exported values");
        }
        void Problem(IEnumerable<RecognizedLine> lines, string warning)
        {
            var parsed = TooltipParser.Parse(lines.ToArray());
            Require(parsed.ReviewWarnings.Any(w => w.Contains(warning, StringComparison.OrdinalIgnoreCase)),
                "Missing review warning: " + warning + "; got " + string.Join("; ", parsed.ReviewWarnings));
        }
        Clean(baseline, "Baseline");
        // Lower confidence must not change either the export or the review outcome
        // when quality, level, tempering, stats and modifier data were extracted.
        Clean(baseline.Select(l => l.Index == 0 ? l : l with { Confidence = 0 }), "Resolved fields with low OCR confidence");
        Clean(baseline.Select(l => l.Index == 2 ? l with { Text="Regal unreadable-label 0/8", Confidence=0 } : l), "Tempering recovered despite damaged label");
        Clean(baseline.Select(l => l.Index == 8 ? l with { Text="Use a (CORE] ability (CONTROL).", Confidence=0 } : l), "Unused modifier description");
        Clean(baseline.Where(l => l.Index != 9), "Unrecognized footer has no exported value");
        Clean(baseline.Prepend(L(-1,"Cloth",0)).Append(L(10,"Gem Socket",0)), "Unused metadata");
        Clean(baseline.Select(l => l.Index == 0 ? l with { Confidence=0, VerifiedConfidence=90 } : l), "Name confirmed by OCR variants");
        Clean(baseline.Take(6).Concat(new[] { L(20,"Imbued Traits (1/1)",0), L(21,"???",0) }).Concat(baseline.Skip(6)), "Unexported imbued trait");

        Problem(baseline.Select(l => l.Index == 0 ? l with { Confidence=0 } : l), "Low confidence in item name");
        Problem(baseline.Where(l => l.Index != 0), "Name not recognized");
        Problem(baseline.Take(1).Append(L(20,"UNREADAB1E TITLE",99)).Concat(baseline.Skip(1)), "Unrecognized item name text");
        Problem(baseline.Where(l => l.Index != 1), "Item slot not recognized");
        Problem(baseline.Where(l => l.Index != 1), "Item level not recognized");
        Problem(baseline.Where(l => l.Index != 2), "Rarity not recognized");
        Problem(baseline.Select(l => l.Index == 2 ? l with { Text="Regal Tempered ?/8" } : l), "Tempering not recognized");
        Problem(baseline.Select(l => l.Index == 2 ? l with { Text="Regal Tempered 9/8" } : l), "Tempering not recognized");
        Problem(baseline.Where(l => l.Index is not (4 or 5)), "No stats recognized");
        Problem(baseline.Select(l => l.Index == 5 ? l with { Text="+? Haste" } : l), "UnparsedLines");
        Problem(baseline.Select(l => l.Index == 5 ? l with { Text="" } : l), "UnparsedLines");
        Problem(baseline.Select(l => l.Index == 5 ? l with { Color="unknown" } : l), "Stat origin not recognized");
        Problem(baseline.Select(l => l.Index == 7 ? l with { Text="Blessing: Example +?" } : l), "Unresolved modifier");
        Problem(baseline.Select(l => l.Index == 3 ? l with { Text="Power Potential 1,20" } : l), "No stats recognized");

        foreach (var (heading, kind) in new[] {
            ("+7 Haste","stat"), ("Trait: Example +3","trait"),
            ("Imbued Essence: Example +100","gem_power"), ("Set: (2/2) Example","set"), ("Ability: Example","ability") })
        {
            var lines = baseline.Select(l => l.Index == 7 ? l with { Text=heading, Confidence=0 } : l).ToArray();
            if (kind is "set" or "ability") lines = lines.Where(l => l.Index != 6).ToArray();
            Clean(lines, "Resolved " + kind, sameExport:false);
            var mod = Export(TooltipParser.Parse(lines))["mods"]!.AsArray().Single()!;
            Require(mod["type"]!.GetValue<string>() == kind, "Lost modifier kind: " + kind);
        }

        Directory.CreateDirectory(output);
        CheckReports(Path.Combine(output,"reports"), baseline);
        CheckIssueImages(Path.Combine(output,"issue-images"), baseline);
        File.WriteAllText(Path.Combine(output,"checks.txt"), "Passed: output-based review warnings, low-confidence resolved fields, uncertain/partial names, missing fields/stats/mods, unexported text, all modifier kinds, unchanged JSON values and report review counts.");
        Console.WriteLine("Result quality checks passed: " + output);
    }

    private static void CheckReports(string root, RecognizedLine[] baseline)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root,"batch.json"), """{"Status":"completed","Tabs":[{"Tab":1,"Status":"completed"}]}""");
        string session=Path.Combine(root,"tab-01");
        Directory.CreateDirectory(session);
        File.WriteAllText(Path.Combine(session,"session.json"), """{"LocationType":"bank","ActiveTab":1} """);
        foreach (bool review in new[] { false, true })
        {
            var parsed=TooltipParser.Parse(baseline.Select(l => l.Index==0 && review ? l with { Confidence=0 } : l).ToArray());
            string status=review ? "needs_visual_review" : "recognized";
            var result=new ItemRecognition.Result("r01_c01.png",new { file="r01_c01.png", status, item=parsed },"",baseline.Length,
                parsed.ReviewWarnings.Count==0 ? null : string.Join("; ",parsed.ReviewWarnings));
            ItemRecognition.WriteReport(session,[result],0,_=>{});
            using(var report=JsonDocument.Parse(File.ReadAllText(Path.Combine(session,"full.json"))))
                Require(report.RootElement.GetProperty("summary").GetProperty("status").GetString()==status,"Single-session report review status");
            BatchRecognition.Run(root,s=>Path.Combine(s,"report.html"),_=>{},CancellationToken.None);
            using var batch=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"full.json")));
            var summary=batch.RootElement.GetProperty("summary");
            Require(summary.GetProperty("status").GetString()==status && summary.GetProperty("review_items").GetInt32()==(review ? 1 : 0),"Batch report review count");
            Require(summary.GetProperty("tabs")[0].GetProperty("recognition_status").GetString()==status,"Tab report review status");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void CheckIssueImages(string folder, RecognizedLine[] baseline)
    {
        Directory.CreateDirectory(folder);
        string file=Path.Combine(folder,"r01_c01.png");
        var lines=baseline.Select(l=>l.Index switch
        {
            0 => l with { Confidence=30 },
            2 => l with { Confidence=0 },
            5 => l with { Text="+? Haste", RawText="+<value> Haste", Confidence=0 },
            _ => l
        }).ToArray();
        using(var bitmap=new Bitmap(420,230))
        {
            using(var graphics=Graphics.FromImage(bitmap))
            using(var font=new Font("Segoe UI",11))
            {
                graphics.Clear(Color.FromArgb(20,30,40));
                foreach(var line in lines)graphics.DrawString(line.Text,font,Brushes.White,line.Box.Location);
            }
            bitmap.Save(file,System.Drawing.Imaging.ImageFormat.Png);
        }
        var item=TooltipParser.Parse(lines);
        var evidence=ScanIssueEvidence.Create(file,420,230,lines,item);
        Require(evidence.Lines.Select(l=>l.Index).SequenceEqual([0,5]),"Highlighting must exclude resolved low-confidence rarity");
        Require(evidence.Lines[1].RawText=="+<value> Haste","Raw OCR was lost in issue evidence");
        var tracker=new ScanProgressTracker(new(ScanMode.Auto,true,false,false,false),_=>{});
        tracker.RegisterSession(folder,ScanTarget.Equipped,null);
        tracker.Queued(file);
        tracker.Recognized(file,new(file,new{},"",lines.Length,string.Join("; ",item.ReviewWarnings),evidence),null);
        tracker.CaptureComplete(ScanTarget.Equipped);
        tracker.CaptureFailed(ScanTarget.Equipped,null,2,1,"No stable tooltip <capture>");
        tracker.Finish(ScanPhase.Completed,"Scan complete. Some items need review.");
        // Images must survive temporary capture cleanup in the normal (non-debug) export.
        File.Delete(file);
        ScanController.WriteSummary(folder,tracker.Snapshot);
        string html=File.ReadAllText(Path.Combine(folder,"scan-issues.html"));
        Require(html.Contains("data:image/png;base64,"+evidence.ImageBase64) && html.Contains("class='line'"),"Standalone image or overlays missing");
        Require(html.Contains("Raw OCR:") && html.Contains("&lt;value&gt;") && !html.Contains("<value>") && !html.Contains("<capture>"),"Tooltip OCR text was not escaped");
        Require(html.Contains("No item image is available"),"Missing captures need an explicit fallback");
        using var summary=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"scan-summary.json")));
        Require(summary.RootElement.GetProperty("Issues")[0].GetProperty("Lines").GetArrayLength()==2,"Summary lost source line evidence");
    }
}
