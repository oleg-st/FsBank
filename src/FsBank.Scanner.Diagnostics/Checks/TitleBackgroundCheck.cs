using FsBank.Scanner.Export;
using FsBank.Scanner.Imaging;
using FsBank.Scanner.Ocr;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FsBank.Scanner.Diagnostics.Checks;

internal static class TitleBackgroundCheck
{
    public static void Run(string source,string output)
    {
        Directory.CreateDirectory(output);
        var images=Regex.Matches(File.ReadAllText(Path.Combine(source,"scan-issues.html")),@"data:image/png;base64,([A-Za-z0-9+/=]+)");
        Require(images.Count==1,"Expected one title-background fixture");
        string folder=Path.Combine(output,"tab-07");Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"session.json"),"{\"LocationType\":\"bank\",\"ActiveTab\":7}");
        string file=Path.Combine(folder,"r07_c06.png");File.WriteAllBytes(file,Convert.FromBase64String(images[0].Groups[1].Value));
        var items=JsonNode.Parse(File.ReadAllText(Path.Combine(source,"items.json")))!.AsArray();
        int index=Enumerable.Range(0,items.Count).Single(n=>items[n]!["location"]!["type"]!.GetValue<string>()=="bank"
            && items[n]!["location"]!["tab"]!.GetValue<int>()==7 && items[n]!["location"]!["row"]!.GetValue<int>()==7
            && items[n]!["location"]!["column"]!.GetValue<int>()==6);
        var expected=items[index]!.DeepClone();expected["name"]="GEM-STUDDED HARNESS";
        using var reader=new NativeTextReader();
        var original=Pixels.Load(file);
        foreach(double brightness in new[]{1d,.9,1.1})
        {
            var pixels=original.Crop(original.Bounds);
            for(int y=0;y<pixels.Height;y++)for(int x=0;x<pixels.Width;x++)
                for(int c=0;c<3;c++){int i=pixels.Offset(x,y)+c;pixels.Data[i]=(byte)Math.Clamp(Math.Round(pixels.Data[i]*brightness),0,255);}
            string variantFolder=brightness==1 ? folder : Path.Combine(output,$"brightness-{brightness:F1}");Directory.CreateDirectory(variantFolder);
            string variantFile=Path.Combine(variantFolder,Path.GetFileName(file));pixels.Save(variantFile);
            var bands=TooltipSegmenter.Find(pixels,excludeHeaderDecoration:true);
            var result=ItemRecognition.RecognizeFile(variantFile,reader,_=>{},CancellationToken.None);
            ItemRecognition.WriteReport(variantFolder,[result],0,_=>{});
            Require(result.Issue is null,result.Issue ?? "");
            Require(bands[0].Box.Left<=32 && bands[0].Box.Right>=247,"Segmentation removed title glyphs: "+bands[0].Box);
            var actual=CompactExport.Project(JsonSerializer.SerializeToElement(result.Data),7);
            Require(JsonNode.DeepEquals(expected,actual),"Fields changed beyond restoring the complete name");
            if(brightness==1)items[index]=actual;
            Console.WriteLine($"PASS complete title at brightness {brightness:F1}; all other fields unchanged");
        }
        CompactExport.WriteItems(output,items);ScanExportCheck.CheckItemBrowser(output);
        File.WriteAllText(Path.Combine(output,"checks.txt"),$"Passed: original issue image and two brightness variants retain GEM-STUDDED HARNESS, no review warnings, all other fields unchanged. Derived export contains {items.Count} items.");
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
