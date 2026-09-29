using FsBank.Scanner.Export;
using FsBank.Scanner.Imaging;
using FsBank.Scanner.Ocr;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FsBank.Scanner.Diagnostics.Checks;

internal static class CornerHeaderCheck
{
    public static void Run(string source,string output)
    {
        Directory.CreateDirectory(output);
        var images=Regex.Matches(File.ReadAllText(Path.Combine(source,"scan-issues.html")),@"data:image/png;base64,([A-Za-z0-9+/=]+)");
        Require(images.Count==1,"Expected one external-background fixture");
        string originalFile=Path.Combine(output,"original.png");
        File.WriteAllBytes(originalFile,Convert.FromBase64String(images[0].Groups[1].Value));
        var original=Pixels.Load(originalFile);
        var items=JsonNode.Parse(File.ReadAllText(Path.Combine(source,"items.json")))!.AsArray();
        var expected=items.Single(i=>i!["location"]!["type"]!.GetValue<string>()=="inventory"
            && i["location"]!["row"]!.GetValue<int>()==1 && i["location"]!["column"]!.GetValue<int>()==9)!;
        using var reader=new NativeTextReader();var vision=new Vision();
        void Verify(Pixels pixels,string folder)
        {
            Directory.CreateDirectory(folder);string file=Path.Combine(folder,"r01_c09.png");pixels.Save(file);
            File.WriteAllText(Path.Combine(folder,"session.json"),"{\"LocationType\":\"inventory\"}");
            var result=ItemRecognition.RecognizeFile(file,reader,_=>{},CancellationToken.None);
            ItemRecognition.WriteReport(folder,[result],0,_=>{});
            Require(result.Issue is null,file+": "+result.Issue);
            var actual=CompactExport.Project(JsonSerializer.SerializeToElement(result.Data),null,"inventory");
            Require(JsonNode.DeepEquals(expected,actual),"Export differs from visually verified source: "+file);
        }
        foreach(double brightness in new[]{1d,.9,1.1})foreach(int padding in new[]{0,30,60})
        {
            var frame=new Pixels(original.Width,original.Height+padding);
            for(int y=0;y<padding;y++)Array.Copy(original.Data,(y%30)*original.Width*Pixels.BytesPerPixel,frame.Data,y*frame.Width*Pixels.BytesPerPixel,original.Width*Pixels.BytesPerPixel);
            Array.Copy(original.Data,0,frame.Data,padding*frame.Width*Pixels.BytesPerPixel,original.Data.Length);
            for(int y=0;y<frame.Height;y++)for(int x=0;x<frame.Width;x++)for(int c=0;c<3;c++)
            {int i=frame.Offset(x,y)+c;frame.Data[i]=(byte)Math.Clamp(Math.Round(frame.Data[i]*brightness),0,255);}
            string variant=Path.Combine(output,$"brightness-{brightness:F1}-padding-{padding}");
            Verify(frame,Path.Combine(variant,"saved"));
            var tip=vision.FindTooltip(frame,1);
            Require(tip is not null && tip.Bounds.Top>=padding+25,"Captured world scenery above tooltip");
            Require(vision.TrackTooltip(frame,1,tip!)==tip,"Bounds changed during tracking");
            var cropped=frame.Crop(tip!.Bounds);Verify(cropped,Path.Combine(variant,"captured"));
            // A second pass must never mistake the divider below the title
            // for a new top edge and progressively remove the title.
            var again=vision.FindTooltip(cropped,1);
            Require(again is not null && again.Bounds.Top==0,"Header refinement is not idempotent");
            Console.WriteLine($"PASS brightness {brightness:F1}, top padding {padding}: saved OCR, capture, tracking, repeated refinement");
        }
        foreach(int cut in new[]{55,64,78})
        {
            var incomplete=original.Crop(new System.Drawing.Rectangle(0,cut,original.Width,original.Height-cut));
            Require(!TooltipSegmenter.HasCompleteTitle(incomplete),"A clipped/missing title passed completeness validation");
        }
        CompactExport.WriteItems(output,items);ScanExportCheck.CheckItemBrowser(output);
        File.WriteAllText(Path.Combine(output,"checks.txt"),$"Passed 3 clipped-title rejections, 9 brightness/background variants and their captures: no review issues; all exported fields unchanged. Derived export contains {items.Count} items.");
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
