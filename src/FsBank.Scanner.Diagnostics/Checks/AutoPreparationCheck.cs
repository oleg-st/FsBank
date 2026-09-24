using FsBank.Scanner.Game;
using FsBank.Scanner.Imaging;
using FsBank.Scanner.Scanning;
using System.Diagnostics;
using System.Text.Json;

namespace FsBank.Scanner.Diagnostics.Checks;

internal static class AutoPreparationCheck
{
    public static void Run(string batch,string output)
    {
        Directory.CreateDirectory(output);
        var vision=new Vision();var search=new PanelSearch(vision);var timings=new List<object>();
        void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        foreach(string folder in Directory.GetDirectories(batch).Order())
        {
            string metadata=Path.Combine(folder,"session.json"),image=Path.Combine(folder,"baseline.png");
            if(!File.Exists(metadata) || !File.Exists(image))continue;
            using var doc=JsonDocument.Parse(File.ReadAllText(metadata));
            var root=doc.RootElement;
            var target=Enum.Parse<ScanTarget>(root.GetProperty("LocationType").GetString()!,true);
            var client=root.GetProperty("GameClient");var region=root.GetProperty("CaptureRegion");
            var baseline=Pixels.Load(image);
            var frame=new Pixels(client.GetProperty("Width").GetInt32(),client.GetProperty("Height").GetInt32());
            int dx=region.GetProperty("X").GetInt32()-client.GetProperty("X").GetInt32();
            int dy=region.GetProperty("Y").GetInt32()-client.GetProperty("Y").GetInt32();
            Blit(baseline,baseline.Bounds,frame,new Point(dx,dy));
            // Reproduce the previous UI path: two complete panel searches.
            var watch=Stopwatch.StartNew();
            var expected=vision.FindLayout(frame,target);
            Require(expected is not null,"Reference panel missing: "+folder);
            Require(vision.FindLayout(frame,target)==expected,"Unstable reference detector");
            double oldSearchMs=watch.Elapsed.TotalMilliseconds;
            watch.Restart();var actual=search.Find(frame,target);
            double newSearchMs=watch.Elapsed.TotalMilliseconds;
            Require(actual==expected,"Fast detector changed layout: "+folder);
            watch.Restart();Require(search.Find(frame,target)==expected,"Cached layout changed");
            double cachedSearchMs=watch.Elapsed.TotalMilliseconds;
            watch.Restart();Require(vision.FindTooltip(baseline,expected!.Scale) is null,"Expected a clear baseline");
            double clearMs=watch.Elapsed.TotalMilliseconds;
            watch.Restart();baseline.Save(Path.Combine(output,"baseline-save.png"));
            double saveMs=watch.Elapsed.TotalMilliseconds;
            timings.Add(new {Area=Path.GetFileName(folder),OldDoubleSearchMs=oldSearchMs,NewSearchMs=newSearchMs,CachedSearchMs=cachedSearchMs,TooltipClearMs=clearMs,BaselineSaveMs=saveMs});

            if(target==ScanTarget.Bank && Path.GetFileName(folder)!="tab-01")continue;
            var invalid=frame.Crop(frame.Bounds);
            // A title alone must not validate a panel: remove its independent marker.
            var verification=expected.VerificationArea;verification.Inflate(4,4);verification.Intersect(frame.Bounds);
            for(int y=verification.Top;y<verification.Bottom;y++)
                Array.Clear(invalid.Data,invalid.Offset(verification.Left,y),verification.Width*Pixels.BytesPerPixel);
            Require(vision.FindLayoutFast(invalid,target,expected,fallback:false) is null,"Hint accepted without independent panel geometry");
            var closed=frame.Crop(frame.Bounds);
            for(int y=expected.Bounds.Top;y<expected.Bounds.Bottom;y++)
                Array.Clear(closed.Data,closed.Offset(expected.Bounds.Left,y),expected.Bounds.Width*Pixels.BytesPerPixel);
            for(int attempt=0;attempt<20;attempt++)Require(search.Find(closed,target) is null,"Closed panel accepted from cached hint");
            Require(search.Find(frame,target)==expected,"Opening panel did not interrupt fallback");
            // Keep the textured background; relocate only the panel, outside the hint radius.
            var moved=closed.Crop(closed.Bounds);
            var delta=target==ScanTarget.Bank ? new Point(-120,-80) : new Point(160,-100);
            var destination=new Point(expected.Bounds.X+delta.X,expected.Bounds.Y+delta.Y);
            Blit(frame,expected.Bounds,moved,destination);
            var movedSearch=new PanelSearch(new Vision());ScanLayout? movedLayout=null;
            int probes=0;
            while(movedLayout is null && probes++<2000)movedLayout=movedSearch.Find(moved,target);
            Require(movedLayout==expected.Translate(delta.X,delta.Y),"Fallback missed relocated panel: "+folder);
            Require(movedSearch.Find(moved,target)==movedLayout,"Relocated cached panel changed");
            if(target==ScanTarget.Equipped)
            {
                var inventory=vision.FindLayout(moved,ScanTarget.Inventory);
                Require(movedSearch.Find(moved,ScanTarget.Inventory)==inventory,"Equipped hint changed inventory geometry");
            }
        }
        Require(timings.Count>0,"No saved baselines found");
        File.WriteAllText(Path.Combine(output,"timings.json"),JsonSerializer.Serialize(timings,new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(output,"checks.txt"),"Passed: full/fast/cached geometry, closed panel rejection, immediate reopening, relocated panel fallback, equipped-to-inventory hint. Offline replay; no game input.");
    }
    private static void Blit(Pixels source,Rectangle area,Pixels target,Point destination)
    {
        if(!target.Bounds.Contains(new Rectangle(destination,area.Size)))throw new InvalidOperationException("Fixture outside frame");
        for(int y=0;y<area.Height;y++)
            Buffer.BlockCopy(source.Data,source.Offset(area.X,area.Y+y),target.Data,target.Offset(destination.X,destination.Y+y),area.Width*Pixels.BytesPerPixel);
    }
}
