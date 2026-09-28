using FsBank.Scanner.Export;
using FsBank.Scanner.Imaging;
using FsBank.Scanner.Ocr;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FsBank.Scanner.Diagnostics.Checks;

internal static class TooltipStructureCheck
{
    public static void Run(string source,string output)
    {
        Directory.CreateDirectory(output);
        var images=Regex.Matches(File.ReadAllText(Path.Combine(source,"scan-issues.html")),@"data:image/png;base64,([A-Za-z0-9+/=]+)");
        Require(images.Count==2,"Expected two structural OCR fixtures");
        var items=JsonNode.Parse(File.ReadAllText(Path.Combine(source,"items.json")))!.AsArray();
        using var reader=new NativeTextReader();var vision=new Vision();
        for(int i=0;i<2;i++)
        {
            int tab=i==0 ? 5 : 7,row=i==0 ? 8 : 7,column=i==0 ? 8 : 5;
            string folder=Path.Combine(output,$"tab-{tab:00}");Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"session.json"),JsonSerializer.Serialize(new {LocationType="bank",ActiveTab=tab}));
            string file=Path.Combine(folder,$"r{row:00}_c{column:00}.png");File.WriteAllBytes(file,Convert.FromBase64String(images[i].Groups[1].Value));
            int index=Enumerable.Range(0,items.Count).Single(n=>items[n]!["location"]!["type"]!.GetValue<string>()=="bank"
                && items[n]!["location"]!["tab"]!.GetValue<int>()==tab && items[n]!["location"]!["row"]!.GetValue<int>()==row
                && items[n]!["location"]!["column"]!.GetValue<int>()==column);
            var expected=items[index]!.DeepClone();
            if(i==1)expected["stats"]=JsonNode.Parse("""
                [{"type":"Armor","value":109,"origin":"base"},{"type":"Stamina","value":32,"origin":"base"},
                 {"type":"Intellect","value":11,"origin":"base"},{"type":"Expertise","value":12,"origin":"base"},
                 {"type":"Haste","value":13,"origin":"dynamic"},{"type":"Spirit","value":13,"origin":"dynamic"}]
                """);
            JsonObject Verify(string path)
            {
                var result=ItemRecognition.RecognizeFile(path,reader,_=>{},CancellationToken.None);
                ItemRecognition.WriteReport(Path.GetDirectoryName(path)!,[result],0,_=>{});
                Require(result.Issue is null,path+": "+result.Issue);
                var actual=CompactExport.Project(JsonSerializer.SerializeToElement(result.Data),tab);
                Require(JsonNode.DeepEquals(expected,actual),"Unexpected export change: "+path);
                return actual;
            }
            items[index]=Verify(file);
            if(i==0)
            {
                var original=Pixels.Load(file);
                foreach(int extra in new[]{0,30,60})
                {
                    var frame=new Pixels(original.Width,original.Height+extra);
                    for(int y=0;y<extra;y++)Array.Copy(original.Data,(y%30)*original.Width*Pixels.BytesPerPixel,frame.Data,y*frame.Width*Pixels.BytesPerPixel,original.Width*Pixels.BytesPerPixel);
                    Array.Copy(original.Data,0,frame.Data,extra*frame.Width*Pixels.BytesPerPixel,original.Data.Length);
                    var tip=vision.FindTooltip(frame,1);
                    Require(tip is not null && tip.Bounds.Top>=extra+55,"External background remains in captured bounds");
                    Require(vision.TrackTooltip(frame,1,tip!)==tip,"Header bounds change during tracking");
                    string capturedFolder=Path.Combine(output,$"top-padding-{extra}");Directory.CreateDirectory(capturedFolder);
                    string captured=Path.Combine(capturedFolder,Path.GetFileName(file));frame.Crop(tip!.Bounds).Save(captured);Verify(captured);
                }
            }
            Console.WriteLine("PASS "+expected["name"]+": complete exported fields");
        }
        CompactExport.WriteItems(output,items);ScanExportCheck.CheckItemBrowser(output);
        File.WriteAllText(Path.Combine(output,"checks.txt"),$"Passed: both original screenshots recognized, six visible stats restored, all other exported values unchanged; complete capture and tracking with 0/30/60 additional pixels of world background. Derived export contains {items.Count} items.");
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
