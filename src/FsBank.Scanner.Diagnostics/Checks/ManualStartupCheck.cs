using FsBank.Scanner.Game;
using FsBank.Scanner.Imaging;
using FsBank.Scanner.Runtime;
using FsBank.Scanner.Scanning.Manual;

using System.Diagnostics;
using System.Text.Json;

namespace FsBank.Scanner.Diagnostics.Checks;

internal static class ManualStartupCheck
{
    public static void Run(string reference,string output)
    {
        Directory.CreateDirectory(output);
        var frame=Pixels.Load(reference);
        var oldVision=new Vision();var oldWatch=Stopwatch.StartNew();
        var oldLayout=oldVision.FindEquipped(frame) ?? throw new Exception("Missing reference panel.");
        double oldPanelMs=oldWatch.Elapsed.TotalMilliseconds;
        bool oldClear=oldVision.FindTooltip(frame,oldLayout.Scale) is null;
        double oldTotalMs=oldWatch.Elapsed.TotalMilliseconds;
        var newVision=new Vision();var newWatch=Stopwatch.StartNew();
        var newLayout=newVision.FindEquippedFast(frame);
        double newPanelMs=newWatch.Elapsed.TotalMilliseconds;
        if(newLayout!=oldLayout)throw new Exception("Fast startup changed layout.");
        bool newClear=!newVision.FooterPresentNear(frame,newLayout!.Scale,new Point(frame.Width-40,frame.Height/2));
        double newTotalMs=newWatch.Elapsed.TotalMilliseconds;
        if(!oldClear || !newClear)throw new Exception("Expected clean reference baseline.");
        // Keep the busy game background while removing the character panel.
        var closed=frame.Crop(frame.Bounds);
        for(int y=oldLayout.Bounds.Top;y<oldLayout.Bounds.Bottom;y++)
            Array.Clear(closed.Data,closed.Offset(oldLayout.Bounds.Left,y),oldLayout.Bounds.Width*Pixels.BytesPerPixel);
        var progressive=new ManualPanelSearch(new Vision());
        double maxClosedProbeMs=0;
        for(int i=0;i<20;i++)
        {
            long start=Stopwatch.GetTimestamp();var absent=progressive.Find(closed);
            maxClosedProbeMs=Math.Max(maxClosedProbeMs,Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            if(absent is not null)throw new Exception("Closed panel was accepted.");
        }
        long openedStart=Stopwatch.GetTimestamp();var opened=progressive.Find(frame);
        double openedMs=Stopwatch.GetElapsedTime(openedStart).TotalMilliseconds;
        if(opened!=oldLayout)throw new Exception("Panel opening must interrupt fallback on the first new frame.");
        var moved=new Pixels(frame.Width,frame.Height);
        for(int y=100;y<frame.Height;y++)
            Buffer.BlockCopy(frame.Data,frame.Offset(0,y),moved.Data,moved.Offset(160,y-100),(frame.Width-160)*Pixels.BytesPerPixel);
        var movedSearch=new ManualPanelSearch(new Vision());EquippedLayout? movedLayout=null;int movedProbes=0;
        while(movedLayout is null && movedProbes<1000){movedLayout=movedSearch.Find(moved);movedProbes++;}
        if(movedLayout?.Anchor!=new Point(oldLayout.Anchor.X+160,oldLayout.Anchor.Y-100))throw new Exception("Incremental fallback missed a moved panel.");
        File.WriteAllText(Path.Combine(output,"timings.json"),JsonSerializer.Serialize(new
        {Configuration=BuildInfo.Configuration,OldPanelMs=oldPanelMs,OldPanelAndClearMs=oldTotalMs,NewPanelMs=newPanelMs,NewPanelAndClearMs=newTotalMs,
            MaxClosedProbeMs=maxClosedProbeMs,FirstOpenedFrameMs=openedMs,MovedPanelProbes=movedProbes},new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(output,"checks.txt"),"Passed: closed textured frames, detection on first opened frame during fallback, moved-panel incremental fallback and unchanged layout.");
    }
}
