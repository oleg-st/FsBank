using FsBank.Scanner.Export;
using FsBank.Scanner.Imaging;
using FsBank.Scanner.Ocr;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FsBank.Scanner.Diagnostics.Checks;

internal static class Patch05CaptureCheck
{
    public static void Run(string source,string placements,string output)
    {
        Directory.CreateDirectory(output);
        using var saved=JsonDocument.Parse(File.ReadAllText(Path.Combine(source,"full.json")));
        using var reader=new NativeTextReader();
        using var quality=new TooltipCaptureQuality();
        var vision=new Vision();
        var items=new JsonArray();
        var reports=new Dictionary<string,List<ItemRecognition.Result>>();
        foreach(var row in saved.RootElement.GetProperty("items").EnumerateArray())
        {
            string relative=row.GetProperty("file").GetString()!;
            string file=Path.Combine(output,relative),folder=Path.GetDirectoryName(file)!;
            Directory.CreateDirectory(folder);
            File.Copy(Path.Combine(source,relative),file,true);
            var result=ItemRecognition.RecognizeFile(file,reader,_=>{},CancellationToken.None);
            Require(result.Issue is null,relative+": "+result.Issue);
            var data=JsonSerializer.SerializeToElement(result.Data);
            foreach(var oldLine in row.GetProperty("lines").EnumerateArray().Where(l=>l.GetProperty("Text").GetString()=="WRAP OF INSECTS"))
            {
                var title=data.GetProperty("lines").EnumerateArray().Single(l=>l.GetProperty("Text").GetString()=="WRAP OF INSECTS");
                Require(title.GetProperty("RawText").GetString()==oldLine.GetProperty("RawText").GetString(),"Title text replaced during confidence verification");
                if(title.GetProperty("Confidence").GetInt32()<60)
                    Require(title.GetProperty("VerifiedConfidence").GetInt32()>=60,"Title was accepted without confirmation");
            }
            var updated=JsonNode.Parse(row.GetRawText())!;
            updated["item"]=JsonNode.Parse(data.GetProperty("item").GetRawText());
            var actual=CompactExport.Project(JsonSerializer.SerializeToElement(updated),null);
            Require(JsonNode.DeepEquals(CompactExport.Project(row,null),actual),"Export changed: "+relative);
            items.Add(actual);
            if(!reports.TryGetValue(folder,out var results))reports[folder]=results=[];
            results.Add(result);
            Require(quality.HasReadableStats(Pixels.Load(file)),"Content rejected: "+relative);
            if(items.Count%25==0)Console.WriteLine($"Verified {items.Count} captures");
        }
        foreach(var (folder,results) in reports)ItemRecognition.WriteReport(folder,results,0,_=>{});
        CompactExport.WriteItems(output,items);
        Console.WriteLine($"PASS {items.Count} captures: zero review warnings, exported values unchanged");

        string issueFile=Path.Combine(source,"tab-05","r08_c01.png");
        var issue=Pixels.Load(issueFile);
        var fixedTip=vision.FindTooltip(issue,1);
        int titleTop=saved.RootElement.GetProperty("items").EnumerateArray().Single(r=>r.GetProperty("file").GetString()=="tab-05/r08_c01.png")
            .GetProperty("lines").EnumerateArray().Single(l=>l.GetProperty("Text").GetString()=="BOARHIDE HEADGUARD")
            .GetProperty("Box").GetProperty("Y").GetInt32();
        Require(fixedTip is not null && fixedTip.Bounds.Top>=titleTop-28 && fixedTip.Bounds.Top<=titleTop-9,"Bank grid remains above header or title was clipped");
        var fixedCrop=issue.Crop(fixedTip!.Bounds);
        fixedCrop.Save(Path.Combine(output,"fixed-header.png"));
        var repeat=vision.FindTooltip(fixedCrop,1);
        Require(repeat is not null && repeat.Bounds.Top==0,"Repeated refinement removed the title");
        foreach(int cut in new[]{22,30,45})
            Require(!TooltipSegmenter.HasCompleteTitle(fixedCrop.Crop(new Rectangle(0,cut,fixedCrop.Width,fixedCrop.Height-cut))),"Clipped title accepted");

        foreach(var (name,hover) in new[]{("above",new Point(165,700)),("clamped",new Point(130,300))})
        {
            var frame=Pixels.Load(Path.Combine(placements,name+".png"));
            var tip=vision.FindTooltipNear(frame,1,hover);
            Require(tip is not null,name+": tooltip missing");
            var crop=frame.Crop(tip!.Bounds);
            Require(TooltipSegmenter.HasCompleteTitle(crop) && quality.HasReadableStats(crop),name+": title/content lost");
            Require(vision.TrackTooltip(frame,1,tip)==tip,name+": tracking changes bounds");
            Require(tip.Bounds.Top<hover.Y,name+": expected tooltip above cursor");
            string file=Path.Combine(output,name+".png");crop.Save(file);
            var result=ItemRecognition.RecognizeFile(file,reader,_=>{},CancellationToken.None);
            Require(result.Issue is null,name+": "+result.Issue);
            Require(JsonSerializer.SerializeToElement(result.Data).GetProperty("item").GetProperty("Name").GetString()=="ELHERYN'S CACHE",name+": truncated name");
        }
        // Replay the known cursor/tooltip positions from the saved bank session.
        var below=new Pixels(1000,1000);var charm=Pixels.Load(Path.Combine(source,"tab-07","r01_c02.png"));
        for(int y=0;y<charm.Height;y++)Array.Copy(charm.Data,y*charm.Width*4,below.Data,below.Offset(300,250+y),charm.Width*4);
        var belowTip=vision.FindTooltipNear(below,1,new Point(273,225));
        Require(belowTip is not null && belowTip.Bounds.Top>225 && TooltipSegmenter.HasCompleteTitle(below.Crop(belowTip.Bounds)),"Below-cursor placement failed");

        var charms=Directory.GetFiles(Path.Combine(source,"tab-07"),"r??_c??.png").Order().Select(Pixels.Load).ToArray();
        using var oldReader=new NativeTextReader();
        // Warm both engines before comparing identical in-memory captures.
        Require(LegacyQuality(charms[0],oldReader) && quality.HasReadableStats(charms[0]),"Warm-up rejected charm");
        double Measure(Func<Pixels,bool> check)
        {
            var timer=Stopwatch.StartNew();
            for(int repeat=0;repeat<3;repeat++)foreach(var pixels in charms)Require(check(pixels),"Benchmark rejected charm");
            return timer.Elapsed.TotalMilliseconds/(charms.Length*3);
        }
        double before=Measure(p=>LegacyQuality(p,oldReader)),after=Measure(p=>quality.HasReadableStats(p));
        // Neither metadata alone nor ability prose alone is enough.
        var bands=TooltipSegmenter.Find(charms[0],excludeHeaderDecoration:true);
        foreach(int index in new[]{1,4})
        {
            var negative=charms[0].Crop(charms[0].Bounds);var box=bands[index].Box;
            for(int y=box.Top;y<box.Bottom;y++)Array.Clear(negative.Data,negative.Offset(box.Left,y),box.Width*4);
            Require(!quality.HasReadableStats(negative),"Incomplete charm accepted: "+index);
        }
        string summary=$"Passed: {items.Count} real captures, zero reviews, identical exports; bank-grid header repair; clipped-title and incomplete-charm rejection; below/above/bottom-clamped placement. Charm validation mean: {before:F1} ms before, {after:F1} ms after ({before/after:F1}x).";
        Console.WriteLine(summary);File.WriteAllText(Path.Combine(output,"checks.txt"),summary);
    }

    // Previous release's content gate, retained only as the benchmark baseline.
    private static bool LegacyQuality(Pixels pixels,NativeTextReader reader)
    {
        var bands=TooltipSegmenter.Find(pixels,excludeHeaderDecoration:true);
        foreach(var band in bands)
        {
            if(band.Box.Top<85 || band.Box.Bottom>pixels.Height-45 || band.Box.Left>pixels.Width*.15 || band.Box.Width>pixels.Width*.85 || band.Box.Height>24 || band.Color=="mixed")continue;
            var result=reader.ReadVariant(pixels,band.Box,3,2,7,60);
            if(result.Confidence>=60 && Regex.IsMatch(result.Text,@"^\+\d+\s+[A-Za-z][A-Za-z ]*$"))return true;
        }
        bool charm=false;
        foreach(var band in bands)
        {
            if(band.Box.Bottom>pixels.Height-35 || !charm && band.Box.Top>110)continue;
            var result=reader.Read(pixels,band.Box,false,true);
            if(Regex.IsMatch(result.Text,@"\bCharm\s*$") && band.Box.Top<110)charm=true;
            else if(charm && Regex.IsMatch(result.Text,@"\bAbility:\s*\S"))return true;
        }
        return false;
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
