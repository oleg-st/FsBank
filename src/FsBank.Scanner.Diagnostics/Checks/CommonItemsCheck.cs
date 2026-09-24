using FsBank.Scanner.Export;
using FsBank.Scanner.Game;
using FsBank.Scanner.Imaging;
using FsBank.Scanner.Ocr;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FsBank.Scanner.Diagnostics.Checks;

internal static class CommonItemsCheck
{
    public static void Run(string batch,string output)
    {
        Directory.CreateDirectory(output);
        void Require(bool ok,string message) { if(!ok)throw new InvalidOperationException(message); }
        var vision=new Vision();
        var baseline=Pixels.Load(Path.Combine(batch,"equipped","baseline.png"));
        var equipped=vision.FindEquipped(baseline) ?? throw new InvalidOperationException("Character panel not found");
        var occupied=new HashSet<(int,int)> { (0,0),(1,1),(2,0),(3,0),(4,0),(5,0),(5,1),(6,0),(6,1) };
        foreach(var slot in equipped.Slots())
            Require(Vision.Occupied(baseline,slot.Bounds)==occupied.Contains((slot.Row,slot.Col)),$"Wrong equipped occupancy {slot.Row+1}:{slot.Col+1}");
        var inventory=(InventoryLayout)vision.FindLayout(baseline,ScanTarget.Inventory)!;
        foreach(var slot in inventory.Slots())
            Require(Vision.Occupied(baseline,slot.Bounds)==(slot.Row==0 && slot.Col==0),$"Wrong inventory occupancy {slot.Row+1}:{slot.Col+1}");
        Require(!Vision.Occupied(new Pixels(56,56),new Rectangle(0,0,56,56)),"Blank cell occupied");
        using var reader=new NativeTextReader();
        int recovered=0;
        var merged=new JsonArray();
        foreach(string location in new[]{"equipped","inventory"})
        {
            string source=Path.Combine(batch,location),destination=Path.Combine(output,location);
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination,"session.json"),JsonSerializer.Serialize(new {LocationType=location}));
            var results=new List<ItemRecognition.Result>();
            foreach(string file in Directory.GetFiles(source,"r??_c??.png"))
            {
                // Replay detection on old crops too: the white sash included
                // over 100 pixels of the character above the tooltip outline.
                var pixels=Pixels.Load(file);
                var frame=new Pixels(pixels.Width+40,pixels.Height+40);
                for(int y=0;y<pixels.Height;y++)
                    Array.Copy(pixels.Data,y*pixels.Width*Pixels.BytesPerPixel,frame.Data,frame.Offset(20,y+20),pixels.Width*Pixels.BytesPerPixel);
                var found=vision.FindTooltip(frame,1);
                if(found is not null && TooltipSegmenter.HasCompleteTitle(frame.Crop(found.Bounds)))pixels=frame.Crop(found.Bounds);
                string copy=Path.Combine(destination,Path.GetFileName(file));pixels.Save(copy);
                results.Add(ItemRecognition.RecognizeFile(copy,reader,_=>{},CancellationToken.None));
            }
            foreach(string file in Directory.GetFiles(source,"*-header-attempt-*.png"))
            {
                var frame=Pixels.Load(file);
                var tooltip=vision.FindTooltip(frame,1) ?? throw new InvalidOperationException("Tooltip not found: "+file);
                var crop=frame.Crop(tooltip.Bounds);
                string stem=Path.GetFileName(file)[..7];
                string target=Path.Combine(destination,stem+".png");
                crop.Save(target);
                Require(TooltipSegmenter.HasCompleteTitle(crop),"Incomplete common title: "+file);
                Require(TooltipSegmenter.HasReadableLayout(crop),"Incomplete common layout: "+file);
                // Missing outline and a title cut through its first line must still be rejected.
                foreach(int cut in new[]{14,28,45})
                    Require(!TooltipSegmenter.HasCompleteTitle(crop.Crop(new Rectangle(0,cut,crop.Width,crop.Height-cut))),"Clipped common title accepted: "+file);
                var result=ItemRecognition.RecognizeFile(target,reader,_=>{},CancellationToken.None);
                var data=JsonSerializer.SerializeToElement(result.Data);
                var item=data.GetProperty("item");
                string expected=location=="inventory" ? "VIGOUR'S HEELED BOOTS" : stem switch
                {
                    "r01_c01" => "VIGOUR'S HEADPIECE",
                    "r03_c01" => "VIGOUR'S MANTLET",
                    "r04_c01" => "VIGOUR'S STOLE",
                    _ => throw new InvalidOperationException("Unexpected fixture: "+file)
                };
                Require(item.GetProperty("Name").GetString()==expected,"Wrong common name: "+item.GetProperty("Name"));
                Require(item.GetProperty("Rarity").GetString()=="Common","Wrong common rarity");
                Require(item.GetProperty("Stats").GetArrayLength()==1,"Lost common armor stat");
                int armor=location=="inventory" ? 11 : stem=="r01_c01" ? 17 : stem=="r03_c01" ? 8 : 20;
                Require(item.GetProperty("Stats")[0].GetProperty("Value").GetInt32()==armor,"Incorrect common armor value");
                results.RemoveAll(r=>r.File==target);results.Add(result);recovered++;
                Console.WriteLine($"PASS {Path.GetFileName(file)}: {expected}");
            }
            Require(results.Count==(location=="equipped" ? 8 : 1),"Missing recovered item");
            Require(results.All(r=>r.Issue is null),"Unexpected review warnings: "+string.Join("; ",results.Where(r=>r.Issue is not null).Select(r=>r.File+": "+r.Issue)));
            ItemRecognition.WriteReport(destination,results,0,_=>{});
            ScanExportCheck.CheckItemBrowser(destination);
            var originalItems=JsonNode.Parse(File.ReadAllText(Path.Combine(source,"items.json")))!.AsArray();
            var updatedItems=JsonNode.Parse(File.ReadAllText(Path.Combine(destination,"items.json")))!.AsArray();
            foreach(var original in originalItems)
                Require(updatedItems.Any(item=>JsonNode.DeepEquals(item,original)),"Previously exported item changed: "+original!["name"]);
            foreach(var item in updatedItems)merged.Add(item!.DeepClone());
        }
        Require(recovered==9,"Expected nine common-item retry frames");
        CompactExport.WriteItems(output,merged);
        ScanExportCheck.CheckItemBrowser(output);
        File.WriteAllText(Path.Combine(output,"checks.txt"),"Passed: nine equipped items including white gloves, five empty equipped slots, one inventory item, 26 empty inventory slots, nine common tooltip recoveries with OCR, clipped-title rejection and derived exports. Gloves require a new live scan; no tooltip was saved for that skipped slot.");
        Console.WriteLine("PASS common items, empty slots, capture recovery, OCR and exports.");
    }
}
