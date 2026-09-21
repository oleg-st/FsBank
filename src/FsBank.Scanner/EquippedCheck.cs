using System.Text.Json;

namespace FsBank.Scanner;

internal static class EquippedCheck
{
    public static void CharacterPanel(string reference,string output)
    {
        Directory.CreateDirectory(output);
        var pixels=Pixels.Load(reference);
        var vision=new Vision();
        var layout=vision.FindEquipped(pixels) ?? throw new InvalidOperationException("Equipment panel not detected");
        void Require(bool ok,string message) { if(!ok)throw new InvalidOperationException(message); }
        Require(vision.FindEquippedFast(pixels)==layout,"Fast detection differs from full detection");
        Require(vision.PanelStillOpen(pixels,layout),"Detected panel fails tracking");
        var inventory=(InventoryLayout?)vision.FindLayout(pixels,ScanTarget.Inventory);
        Require(inventory is not null && inventory.Anchor==layout.Anchor && inventory.ContentOffsetX==layout.ContentOffsetX,"Inventory geometry differs");
        Require(layout.Slots().All(s=>Vision.Occupied(pixels,s.Bounds)),"Missed occupied equipment slot");
        var negative=pixels.Crop(pixels.Bounds);
        var markers=layout.Relative(new Rectangle(-4,-4,108,104));
        for(int y=markers.Top;y<markers.Bottom;y++)Array.Clear(negative.Data,negative.Offset(markers.Left,y),markers.Width*Pixels.BytesPerPixel);
        Require(!vision.PanelStillOpen(negative,layout) && vision.FindEquipped(negative) is null,"Panel accepted without headings");
        Require(vision.FindEquipped(new Pixels(pixels.Width,pixels.Height)) is null,"Blank frame accepted");
        using var annotated=pixels.Bitmap();
        using(var g=Graphics.FromImage(annotated))
        using(var pen=new Pen(Color.Lime,2))
            foreach(var slot in layout.Slots())g.DrawRectangle(pen,slot.Bounds);
        annotated.Save(Path.Combine(output,"slots.png"));
        File.WriteAllText(Path.Combine(output,"layout.json"),JsonSerializer.Serialize(layout));
        File.WriteAllText(Path.Combine(output,"checks.txt"),"Passed: full/fast detection, panel tracking, 14 occupied slots, inventory geometry, missing-heading and blank-frame rejection. No live game input.");
    }
    public static void Run(string reference, string output)
    {
        Directory.CreateDirectory(output);
        void Require(bool ok, string message) { if(!ok)throw new InvalidOperationException(message); }
        var vision=new Vision();
        var pixels=Pixels.Load(reference);
        var layout=vision.FindEquipped(pixels) ?? throw new InvalidOperationException("Equipment panel not detected");
        Require(vision.FindEquippedFast(pixels)==layout,"Fast panel detection changed the reference layout");
        var covered=pixels.Crop(pixels.Bounds);
        var markers=Rectangle.Union(layout.Relative(new Rectangle(0,0,100,30)),layout.VerificationArea);
        for(int y=markers.Top;y<markers.Bottom;y++)Array.Clear(covered.Data,covered.Offset(markers.Left,y),markers.Width*Pixels.BytesPerPixel);
        Require(!vision.PanelStillOpen(covered,layout),"Fixture must obscure POWER/STATS");
        Require(ManualPanelPresence.IsVisible(vision,covered,pixels,layout),"Covered markers falsely hide a matching character header");
        Require(!ManualPanelPresence.IsVisible(vision,new Pixels(pixels.Width,pixels.Height),pixels,layout),"Closed panel accepted by header fallback");
        Require(ManualPanelPresence.IsVisible(vision,pixels,pixels,layout),"Restored panel was not accepted");
        Require(layout.Target==ScanTarget.Equipped && Math.Abs(layout.Scale-1)<.01,"Reference scale");
        Require(Math.Abs(layout.Anchor.X-890)<=2 && Math.Abs(layout.Anchor.Y-414)<=2,"Reference anchor");
        Require(layout.SlotCount==14 && layout.Slots().Select(s=>s.Bounds).Distinct().Count()==14,"Equipment slot enumeration");
        var inventory=vision.FindLayout(pixels,ScanTarget.Inventory) as InventoryLayout;
        Require(inventory is not null && inventory.RowCount==3 && inventory.ColumnCount==9 && inventory.SlotCount==27,"Inventory layout");
        Require(inventory!.Slots().All(s=>layout.Bounds.Contains(s.Bounds) && s.Bounds.Top>=864),"Inventory bounds");
        Require(!inventory.Slots().Any(i=>layout.Slots().Any(e=>i.Bounds.IntersectsWith(e.Bounds))),"Inventory overlaps equipment");
        Require(!Vision.Occupied(pixels,inventory.Cell(0,1)) && Vision.Occupied(pixels,inventory.Cell(0,0)),"Empty/occupied inventory slots");
        var translated=layout.Translate(-200,0);
        Require(translated is EquippedLayout && translated.Cell(6,1)==new Rectangle(layout.Cell(6,1).X-200,layout.Cell(6,1).Y,56,56),"Capture region translation loses layout");
        using(var inventoryImage=pixels.Bitmap())
        {
            using(var g=Graphics.FromImage(inventoryImage))
            using(var pen=new Pen(Color.Cyan,2))
                foreach(var slot in inventory.Slots())g.DrawRectangle(pen,slot.Bounds);
            inventoryImage.Save(Path.Combine(output,"inventory-slots.png"));
        }
        using(var annotated=pixels.Bitmap())
        {
            using(var g=Graphics.FromImage(annotated))
            using(var pen=new Pen(Color.Lime,2))
            for(int row=0;row<7;row++)for(int col=0;col<2;col++)
            {
                var cell=layout.Cell(row,col);
                Require(Vision.Occupied(pixels,cell),$"Missed occupied slot {row+1}:{col+1}");
                Require(cell.Bottom<864,"Equipment scan reaches inventory");
                g.DrawRectangle(pen,cell);
                g.DrawString($"{row+1}:{col+1}",SystemFonts.DefaultFont,Brushes.White,cell.Location);
            }
            annotated.Save(Path.Combine(output,"slots.png"));
        }
        // Independently move the panel to ensure coordinates come from detection.
        using(var moved=new Bitmap(pixels.Width,pixels.Height))
        {
            using(var source=pixels.Bitmap())
            using(var g=Graphics.FromImage(moved))
            {
                g.Clear(Color.Black);
                g.DrawImageUnscaled(source,160,-100);
            }
            string path=Path.Combine(output,"moved.png");moved.Save(path);
            var detected=vision.FindEquipped(Pixels.Load(path));
            Require(detected is not null && detected.Anchor==new Point(layout.Anchor.X+160,layout.Anchor.Y-100),"Moved panel detection");
            Require(vision.FindEquippedFast(Pixels.Load(path))==detected,"Fast panel fallback missed moved panel");
            Require(!ManualPanelPresence.IsVisible(vision,Pixels.Load(path),pixels,layout),"Moved panel accepted at the old position");
        }
        var blank=new Pixels(pixels.Width,pixels.Height);
        Require(vision.FindEquipped(blank) is null && !vision.PanelStillOpen(blank,layout),"Closed panel accepted");
        using var rows=JsonDocument.Parse("[{\"file\":\"r07_c02.png\",\"item\":{\"Name\":\"Sample\"}}]");
        foreach(string type in new[]{"bank","equipped","inventory"})
        {
            string folder=Path.Combine(output,type);Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"session.json"),JsonSerializer.Serialize(new {LocationType=type,ActiveTab=type=="bank" ? (int?)3 : null}));
            string path=CompactExport.Write(folder,rows.RootElement.EnumerateArray());
            using var report=JsonDocument.Parse(File.ReadAllText(path));
            var location=report.RootElement[0].GetProperty("location");
            Require(location.GetProperty("type").GetString()==type,"Location type lost");
            Require(location.GetProperty("row").GetInt32()==7 && location.GetProperty("column").GetInt32()==2,"Slot coordinates lost");
            Require(type=="bank" ? location.GetProperty("tab").GetInt32()==3 : location.GetProperty("tab").ValueKind==JsonValueKind.Null,"Invalid tab");
        }
        var legacy=CompactExport.Project(rows.RootElement[0],4);
        Require(legacy["location"]!["type"]!.GetValue<string>()=="bank" && legacy["location"]!["tab"]!.GetValue<int>()==4,"Legacy bank compatibility");
        File.WriteAllText(Path.Combine(output,"checks.txt"),"Passed: 14 occupied equipment slots; inventory layout; capture translation; fast moved-panel fallback; covered POWER/STATS with intact header; closed/moved panel rejection and restored panel acceptance; bank/equipped/inventory exports. No live game input.");
    }
}
