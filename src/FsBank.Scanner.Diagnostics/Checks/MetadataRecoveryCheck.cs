using FsBank.Scanner.Export;
using FsBank.Scanner.Imaging;
using FsBank.Scanner.Ocr;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FsBank.Scanner.Diagnostics.Checks;

internal static class MetadataRecoveryCheck
{
    public static void Run(string source,string output)
    {
        Directory.CreateDirectory(output);
        using var summary=JsonDocument.Parse(File.ReadAllText(Path.Combine(source,"scan-summary.json")));
        var issues=summary.RootElement.GetProperty("Issues").EnumerateArray().ToArray();
        var images=Regex.Matches(File.ReadAllText(Path.Combine(source,"scan-issues.html")),@"data:image/png;base64,([A-Za-z0-9+/=]+)");
        string[] names=["ANCIENT POULTRY FETISH","HUMMING PORTALSTONE","FALLEN ORDER'S THORNED TREADS","SHARD OF ANTIMATTER","MINER'S HEAVY LEGGUARDS"];
        string[] slots=["Relic","Relic","Feet","Relic","Legs"];
        Require(issues.Length==5 && images.Count==5,"Expected five original issue images");
        var items=JsonNode.Parse(File.ReadAllText(Path.Combine(source,"items.json")))!.AsArray();
        using var reader=new NativeTextReader();
        for(int i=0;i<issues.Length;i++)
        {
            var issue=issues[i];int tab=issue.GetProperty("Tab").GetInt32();
            int row=issue.GetProperty("Row").GetInt32(),column=issue.GetProperty("Column").GetInt32();
            string folder=Path.Combine(output,$"tab-{tab:00}",$"cell-{row}-{column}");Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"session.json"),JsonSerializer.Serialize(new {LocationType="bank",ActiveTab=tab}));
            string file=Path.Combine(folder,$"r{row:00}_c{column:00}.png");
            File.WriteAllBytes(file,Convert.FromBase64String(images[i].Groups[1].Value));
            var result=ItemRecognition.RecognizeFile(file,reader,_=>{},CancellationToken.None);
            ItemRecognition.WriteReport(folder,[result],0,_=>{});
            Require(result.Issue is null,file+": "+result.Issue);
            var data=JsonSerializer.SerializeToElement(result.Data);
            var actual=CompactExport.Project(data,tab);
            Require(actual["name"]!.GetValue<string>()==names[i],"Visible name differs: "+file);
            int index=Enumerable.Range(0,items.Count).Single(n=>items[n]!["location"]!["type"]!.GetValue<string>()=="bank"
                && items[n]!["location"]!["tab"]?.GetValue<int>()==tab && items[n]!["location"]!["row"]!.GetValue<int>()==row
                && items[n]!["location"]!["column"]!.GetValue<int>()==column);
            var expected=items[index]!.DeepClone();expected["item_slot"]=slots[i];expected["item_level"]=315;
            Require(JsonNode.DeepEquals(expected,actual),"Fields changed outside slot/level: "+file);
            if(i==2)
            {
                Require(data.GetProperty("lines")[0].GetProperty("Box").GetProperty("Top").GetInt32()>35,"Background remained above title");
                var pixels=Pixels.Load(file);var vision=new Vision();
                var detected=vision.FindTooltip(pixels,1);
                Require(detected is not null && detected.Bounds.Top>=20,"Capture still includes world scenery");
                Require(vision.TrackTooltip(pixels,1,detected!)==detected,"Tracking disagrees with corrected bounds");
                string correctedFolder=Path.Combine(output,"recaptured");Directory.CreateDirectory(correctedFolder);
                string corrected=Path.Combine(correctedFolder,Path.GetFileName(file));pixels.Crop(detected!.Bounds).Save(corrected);
                var captured=ItemRecognition.RecognizeFile(corrected,reader,_=>{},CancellationToken.None);
                Require(captured.Issue is null && JsonNode.DeepEquals(expected,CompactExport.Project(JsonSerializer.SerializeToElement(captured.Data),tab)),
                    "Corrected capture lost title or changed item fields: "+captured.Issue);
            }
            else
            {
                var repaired=data.GetProperty("lines").EnumerateArray().Single(l=>l.GetProperty("Corrections").EnumerateArray()
                    .Any(c=>c.GetProperty("Kind").GetString()=="ocr_equipment_agreement"));
                Require(repaired.GetProperty("RawText").GetString()==issue.GetProperty("Lines")[0].GetProperty("RawText").GetString(),"Raw evidence lost");
                var b=repaired.GetProperty("Box");var box=new Rectangle(b.GetProperty("X").GetInt32(),b.GetProperty("Y").GetInt32(),b.GetProperty("Width").GetInt32(),b.GetProperty("Height").GetInt32());
                // Erase each required field independently in the actual pixels.
                // A previous successful read must never supply a missing value.
                foreach(bool eraseLevel in new[]{true,false})
                {
                    var damaged=Pixels.Load(file);
                    int left=eraseLevel ? (int)(damaged.Width*.85) : (int)(damaged.Width*.1);
                    int right=eraseLevel ? damaged.Width : (int)(damaged.Width*.5);
                    for(int y=box.Top-2;y<box.Bottom+2;y++)for(int x=left;x<right;x++)
                    {
                        int offset=damaged.Offset(x,y);
                        damaged.Data[offset]=40;damaged.Data[offset+1]=30;damaged.Data[offset+2]=20;
                    }
                    Require(reader.ReadEquipment(damaged,box) is null,"Accepted an erased "+(eraseLevel ? "level" : "slot"));
                }
            }
            items[index]=actual;Console.WriteLine("PASS "+names[i]+": "+slots[i]+" / 315; other fields unchanged");
        }
        CompactExport.WriteItems(output,items);ScanExportCheck.CheckItemBrowser(output);
        File.WriteAllText(Path.Combine(output,"checks.txt"),$"Passed: five original images recognized with zero review issues; four slot/level rows recovered with original OCR evidence retained; eight erased-field cases rejected; background excluded without losing the two-line title; all other fields unchanged. Derived export contains {items.Count} items.");
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
