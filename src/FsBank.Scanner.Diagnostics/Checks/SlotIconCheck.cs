using FsBank.Scanner.Export;
using FsBank.Scanner.Ocr;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FsBank.Scanner.Diagnostics.Checks;

internal static class SlotIconCheck
{
    public static void Run(string source,string output)
    {
        Directory.CreateDirectory(output);
        var images=Regex.Matches(File.ReadAllText(Path.Combine(source,"scan-issues.html")),@"data:image/png;base64,([A-Za-z0-9+/=]+)");
        if(images.Count!=1)throw new InvalidOperationException("Expected one slot-icon fixture");
        string folder=Path.Combine(output,"tab-02");Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,"session.json"),"{\"LocationType\":\"bank\",\"ActiveTab\":2}");
        string file=Path.Combine(folder,"r07_c04.png");
        File.WriteAllBytes(file,Convert.FromBase64String(images[0].Groups[1].Value));
        using var reader=new NativeTextReader();
        var result=ItemRecognition.RecognizeFile(file,reader,_=>{},CancellationToken.None);
        ItemRecognition.WriteReport(folder,[result],0,_=>{});
        if(result.Issue is not null)throw new InvalidOperationException(result.Issue);
        var data=JsonSerializer.SerializeToElement(result.Data);
        var line=data.GetProperty("lines").EnumerateArray().Single(l=>l.GetProperty("Index").GetInt32()==2);
        if(line.GetProperty("RawText").GetString()!="SRing 315"
            || !Regex.IsMatch(line.GetProperty("RecognitionText").GetString()!,@"\bRing\b.*?\b315$")
            || line.GetProperty("Corrections").GetArrayLength()==0)
            throw new InvalidOperationException("Expected original OCR and agreed pixel reread to be retained");
        var actual=CompactExport.Project(data,2);
        var items=JsonNode.Parse(File.ReadAllText(Path.Combine(source,"items.json")))!.AsArray();
        int index=Enumerable.Range(0,items.Count).Single(n=>items[n]!["location"]!["type"]!.GetValue<string>()=="bank"
            && items[n]!["location"]!["tab"]!.GetValue<int>()==2 && items[n]!["location"]!["row"]!.GetValue<int>()==7
            && items[n]!["location"]!["column"]!.GetValue<int>()==4);
        var expected=items[index]!.DeepClone();expected["item_slot"]="Ring";expected["item_level"]=315;
        if(expected["name"]!.GetValue<string>()!="FALLEN ORDER'S HALLOWED SIGNET"
            || !JsonNode.DeepEquals(expected,actual))throw new InvalidOperationException("Unexpected export change");
        items[index]=actual;CompactExport.WriteItems(output,items);ScanExportCheck.CheckItemBrowser(output);
        File.WriteAllText(Path.Combine(output,"checks.txt"),$"Passed: original screenshot recognized without review warnings; Ring / 315 recovered by two confident pixel rereads agreeing on slot and level; original SRing OCR retained; all other fields unchanged. Derived export contains {items.Count} items.");
        Console.WriteLine("PASS FALLEN ORDER'S HALLOWED SIGNET: Ring / 315, other exported fields unchanged");
    }
}
