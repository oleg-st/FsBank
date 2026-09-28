using FsBank.Scanner.Export;
using FsBank.Scanner.Imaging;
using FsBank.Scanner.Ocr;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FsBank.Scanner.Diagnostics.Checks;

internal static class SideOutlineCheck
{
    public static void Run(string source,string output)
    {
        Directory.CreateDirectory(output);
        var images=Regex.Matches(File.ReadAllText(Path.Combine(source,"scan-issues.html")),@"data:image/png;base64,([A-Za-z0-9+/=]+)");
        if(images.Count!=2)throw new InvalidOperationException("Expected two side-outline fixtures");
        string folder=Path.Combine(output,"tab-01");Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"session.json"),"{\"LocationType\":\"bank\",\"ActiveTab\":1}");
        var items=JsonNode.Parse(File.ReadAllText(Path.Combine(source,"items.json")))!.AsArray();
        using var reader=new NativeTextReader();using var quality=new TooltipCaptureQuality();
        var results=new List<ItemRecognition.Result>();
        for(int i=0;i<2;i++)
        {
            int row=i==0 ? 5 : 8;
            string name=i==0 ? "CONJURER'S SILKEN CLOAK" : "FALLEN ORDER'S THORNY GRIPS";
            string file=Path.Combine(folder,$"r{row:00}_c01.png");
            File.WriteAllBytes(file,Convert.FromBase64String(images[i].Groups[1].Value));
            var pixels=Pixels.Load(file);
            if(!quality.HasReadableStats(pixels))throw new InvalidOperationException("Stat gate rejected intact tooltip");
            var bands=TooltipSegmenter.Find(pixels,excludeHeaderDecoration:true);
            if(bands.Any(b=>b.Box.Left<=14))throw new InvalidOperationException("Outline still included in text bands");
            var result=ItemRecognition.RecognizeFile(file,reader,_=>{},CancellationToken.None);
            if(result.Issue is not null)throw new InvalidOperationException(result.Issue);
            var actual=CompactExport.Project(JsonSerializer.SerializeToElement(result.Data),1);
            int index=Enumerable.Range(0,items.Count).Single(n=>items[n]!["location"]!["type"]!.GetValue<string>()=="bank"
                && items[n]!["location"]!["tab"]!.GetValue<int>()==1 && items[n]!["location"]!["row"]!.GetValue<int>()==row
                && items[n]!["location"]!["column"]!.GetValue<int>()==1);
            var expected=items[index]!.DeepClone();expected["name"]=name;
            if(i==0){expected["rarity"]="Epic";expected["temper"]="0/6";}
            if(!JsonNode.DeepEquals(expected,actual))throw new InvalidOperationException("Unexpected export change: "+file);
            // Independently vary external padding on each side. The text itself
            // stays unchanged, including the two-line title ending in GRIPS.
            var normalized=pixels.Crop(new Rectangle(11,0,pixels.Width-11,pixels.Height));
            foreach(int leftPadding in new[]{0,11})foreach(int rightPadding in new[]{0,11})
            {
                var shifted=new Pixels(normalized.Width+leftPadding+rightPadding,normalized.Height);
                for(int y=0;y<normalized.Height;y++)
                    Array.Copy(normalized.Data,y*normalized.Width*Pixels.BytesPerPixel,shifted.Data,
                        shifted.Offset(leftPadding,y),normalized.Width*Pixels.BytesPerPixel);
                string variantFolder=Path.Combine(output,$"padding-{leftPadding}-{rightPadding}");Directory.CreateDirectory(variantFolder);
                string variantFile=Path.Combine(variantFolder,Path.GetFileName(file));shifted.Save(variantFile);
                var variant=ItemRecognition.RecognizeFile(variantFile,reader,_=>{},CancellationToken.None);
                var variantItem=CompactExport.Project(JsonSerializer.SerializeToElement(variant.Data),1);
                if(variant.Issue is not null || !JsonNode.DeepEquals(expected,variantItem))
                    throw new InvalidOperationException($"Padding changed OCR: {variantFile}: {variant.Issue}");
            }
            items[index]=actual;results.Add(result);
            Console.WriteLine("PASS "+name+": visible metadata recovered; stats and modifiers unchanged");
        }
        ItemRecognition.WriteReport(folder,results,0,_=>{});
        CompactExport.WriteItems(output,items);ScanExportCheck.CheckItemBrowser(output);
        File.WriteAllText(Path.Combine(output,"checks.txt"),$"Passed: two original issue images and eight independent left/right padding variants, outline excluded by pixel evidence, complete one/two-line titles, rarity and temper restored, no review warnings, other exported fields unchanged. Derived export contains {items.Count} items.");
    }
}
