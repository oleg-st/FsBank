using FsBank.Scanner.Export;
using FsBank.Scanner.Imaging;
using FsBank.Scanner.Items;
using FsBank.Scanner.Ocr;

using System.Text.Json;

namespace FsBank.Scanner.Diagnostics.Checks;

internal static class QualityCheck
{
    public static void InspectCaptureLayout(string batch,string output)
    {
        Directory.CreateDirectory(output);
        var rejected=new List<string>();var unreadable=new List<string>();int count=0;
        foreach(var directory in Directory.GetDirectories(batch))
        foreach(var file in Directory.GetFiles(directory,"r??_c??.png"))
        {
            count++;
            var pixels=Pixels.Load(file);
            if(!TooltipSegmenter.HasCompleteTitle(pixels))
                rejected.Add(Path.GetRelativePath(batch,file).Replace('\\','/'));
            if(!TooltipSegmenter.HasReadableLayout(pixels))
                unreadable.Add(Path.GetRelativePath(batch,file).Replace('\\','/'));
        }
        var json=JsonSerializer.Serialize(new {Count=count,Rejected=rejected,Unreadable=unreadable},new JsonSerializerOptions{WriteIndented=true});
        File.WriteAllText(Path.Combine(output,"capture-layout.json"),json);
        Console.WriteLine(json);
    }

    public static void Headers(string batch,string output)
    {
        Directory.CreateDirectory(output);var vision=new Vision();var results=new List<object>();
        foreach(var tab in Directory.GetDirectories(batch,"tab-*"))
        foreach(var file in Directory.GetFiles(tab,"*-header-attempt-*.png"))
        {
            var frame=Pixels.Load(file);var tip=vision.FindTooltip(frame,1);
            bool valid=false;
            if(tip is not null)
            {
                var crop=frame.Crop(tip.Bounds);valid=TooltipSegmenter.HasCompleteTitle(crop);
                crop.Save(Path.Combine(output,Path.GetFileName(tab)+"-"+Path.GetFileName(file)));
            }
            results.Add(new {File=Path.GetRelativePath(batch,file),Bounds=tip?.Bounds,Valid=valid});
        }
        File.WriteAllText(Path.Combine(output,"headers.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
    }
    public static void HeaderRegression(string batch,string output)
    {
        Directory.CreateDirectory(output);var vision=new Vision();var rejected=new List<string>();int checkedCount=0,previouslyValid=0;
        foreach(var tab in Directory.GetDirectories(batch,"tab-*"))
        foreach(var file in Directory.GetFiles(tab,"r??_c??.png"))
        {
            var crop=Pixels.Load(file);
            if(!TooltipSegmenter.HasCompleteTitle(crop))continue;
            var frame=new Pixels(crop.Width+40,crop.Height+40);
            for(int y=0;y<crop.Height;y++)Array.Copy(crop.Data,y*crop.Width*Pixels.BytesPerPixel,
                frame.Data,frame.Offset(20,y+20),crop.Width*Pixels.BytesPerPixel);
            checkedCount++;var original=vision.FindTooltip(frame,1,repairHeader:false);var tip=vision.FindTooltip(frame,1);
            if(original is null || !TooltipSegmenter.HasCompleteTitle(frame.Crop(original.Bounds)))continue;
            previouslyValid++;
            if(tip?.Bounds!=original.Bounds)rejected.Add(Path.GetRelativePath(batch,file));
        }
        File.WriteAllText(Path.Combine(output,"regression.json"),JsonSerializer.Serialize(new {Checked=checkedCount,PreviouslyValid=previouslyValid,Rejected=rejected,Note="Crops embedded on blank canvas; only previously valid detections must remain identical. Not a substitute for original full frames."},new JsonSerializerOptions{WriteIndented=true}));
        if(rejected.Count>0)throw new InvalidOperationException($"Header regression: {rejected.Count}/{checkedCount}");
    }
    public static void Run(string batch)
    {
        void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        var results=new List<object>();
        var rejected=new List<string>();
        foreach(var tab in Directory.GetDirectories(batch,"tab-*"))
        foreach(var file in Directory.GetFiles(tab,"r??_c??.png"))
        {
            var pixels=Pixels.Load(file);
            if(!TooltipSegmenter.HasCompleteTitle(pixels))
            {var name=Path.GetRelativePath(batch,file).Replace('\\','/');rejected.Add(name);results.Add(new {File=name});}
        }
        File.WriteAllText(Path.Combine(batch,"header-check.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
        Require(rejected.Order().SequenceEqual(new[]{"tab-04/r03_c03.png","tab-04/r03_c05.png","tab-05/r06_c04.png"}),"Header check must reject the three clipped audit fixtures and accept the other 444");
        var source=new Dictionary<string,(RecognizedLine[] Lines,ParsedTooltip Item)>();
        foreach(var tab in Directory.GetDirectories(batch,"tab-*"))
        {
            using var doc=JsonDocument.Parse(File.ReadAllText(CompactExport.FullReportPath(tab)));
            foreach(var row in doc.RootElement.GetProperty("items").EnumerateArray())
            {
                var lines=JsonSerializer.Deserialize<RecognizedLine[]>(row.GetProperty("lines").GetRawText())!;
                source[Path.GetFileName(tab)+"/"+row.GetProperty("file").GetString()]=(lines,TooltipParser.Parse(lines));
            }
        }
        Require(source.Count==447,"Expected 447 audit fixtures");
        Require(source.Values.All(r=>r.Item.Slot is not null && r.Item.ItemLevel is not null && r.Item.Rarity is not null && r.Item.PowerPotential is not null && r.Item.Stats.Count>0),"Missing parsed fields in audit fixtures");
        var legendary=source["tab-02/r07_c06.png"].Item;
        Require(legendary.Rarity=="Legendary" && legendary.Temperable==false && legendary.TemperedCurrent is null && legendary.PowerPotential==1292 && legendary.UniqueEquipped && legendary.Stats.Count==6,"Legendary format lost fields or stats");
        var wrists=source["tab-05/r01_c01.png"].Item;
        Require(wrists.Slot=="Wrists" && wrists.ItemLevel==315,"Wrists lost level");
        foreach(var file in new[]{"tab-01/r06_c02.png","tab-04/r06_c05.png","tab-07/r07_c05.png"})
            Require(source[file].Item.TemperedCurrent==0 && source[file].Item.Temperable==true,"Damaged Tempered label hid visible numbers");
        foreach(var pair in new[]{("tab-04/r01_c01.png",24,"The 1st hit from Nature's Fury has +100%"),
            ("tab-06/r03_c02.png",17,"1/2/3/3 allies 6/10/13/17 Haste")})
        {
            var line=source[pair.Item1].Lines.Single(l=>l.Index==pair.Item2);
            var pixels=Pixels.Load(Path.Combine(batch,pair.Item1));
            Require(TextGeometry.RestoreSpaces(line.RawText!,line.Symbols,pixels,line.Box).Text==pair.Item3,"Measured spacing regression: "+pair.Item1);
        }
        RecognizedLine L(int i,string t)=>new(i,new Rectangle(1,1,100,12),"white",t,99);
        var malformed=TooltipParser.Parse([L(0,"Power Potential 1,29"),L(1,"+7 Haste")]);
        Require(malformed.PowerPotential is null && malformed.Stats.Count==0,"Malformed thousands separator silently accepted");
        var unknown=TooltipParser.Parse([L(0,"Rare unreadable")]);
        Require(unknown.Rarity=="Rare" && unknown.Temperable is null && unknown.Warnings.Any(w=>w.StartsWith("Tempering not recognized")),"Unknown tempering must remain unknown");
        var round=TooltipParser.Parse([L(0,"one of your (CONTROL) abilities")]);
        Require(round.Warnings.Any(w=>w.StartsWith("Parenthesized uppercase")),"Paired OCR bracket error not flagged");
        using(var reader=new NativeTextReader())
        foreach(var test in new[]{("tab-04/r03_c03.png",21,"Latent Resurgence will attempt to save",true),
            ("tab-05/r06_c04.png",17,"cooldown of one of your [CONTROL]",true),
            ("tab-04/r03_c02.png",20,"(CORE]ability used. Abilities with no",true),
            ("tab-01/r06_c02.png",3,"Rare Tempered 0/5",false),
            ("tab-04/r06_c05.png",3,"Rare Tempered 0/5",false),
            ("tab-07/r07_c05.png",3,"Regal Tempered 0/8",false)})
        {
            var line=source[test.Item1].Lines.Single(l=>l.Index==test.Item2);
            var read=reader.Read(Pixels.Load(Path.Combine(batch,test.Item1)),line.Box,false,test.Item4);
            Require(read.Text==test.Item3,"OCR agreement/abstention failed: "+test.Item1);
            Require(reader.PrimaryText==line.RawText,"Primary OCR evidence lost");
        }
        File.WriteAllText(Path.Combine(batch,"quality-checks.txt"),"Passed: all 447 capture headers (3 clipped rejected, 444 accepted), parsed fields and stats; real legendary/wrists/tempering fixtures; ordinal preservation and overlapped glyph spacing; malformed number/unknown tempering abstention and round-bracket warning. Live rescan recovery not tested.");
    }
}
