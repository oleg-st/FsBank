using FsBank.Scanner.Diagnostics.Checks;
using FsBank.Scanner.Game;
using FsBank.Scanner.Imaging;
using FsBank.Scanner.Ocr;
using FsBank.Scanner.Runtime;

using System.Diagnostics;
using System.Text.Json;

namespace FsBank.Scanner.Diagnostics.Benchmarks;

internal static class VisionBenchmark
{
    public static void EdgeRegression(string cropPath,string output)
    {
        var crop=Pixels.Load(cropPath);
        var vision=new Vision();var results=new List<object>();
        // Replay a real tooltip at the right edge of both a 1080p client and
        // its bank capture region. Remove only external padding, never outlines.
        foreach(int width in new[]{1920,1080})
        foreach(int clippedPadding in new[]{0,1})
        {
            var frame=new Pixels(width,1080);
            int x=width-crop.Width+clippedPadding,y=200;
            for(int row=0;row<crop.Height;row++)
                Buffer.BlockCopy(crop.Data,row*crop.Width*Pixels.BytesPerPixel,frame.Data,frame.Offset(x,y+row),(crop.Width-clippedPadding)*Pixels.BytesPerPixel);
            // Full panel runs out of room while the shorter footer still fits.
            var cursor=new Point(width-290,400);
            var full=vision.FindTooltip(frame,1);
            var near=vision.FindTooltipNear(frame,1,cursor);
            if(full is null || near!=full || !TooltipSegmenter.HasCompleteTitle(frame.Crop(full.Bounds),1))
                throw new InvalidOperationException($"Edge tooltip missed: width={width}, clipped padding={clippedPadding}.");
            if(!vision.FooterPresentNear(frame,1,cursor) || !vision.TooltipNearCursor(frame,1,cursor,full))
                throw new InvalidOperationException("Edge footer/cursor checks disagree.");
            results.Add(new {Width=width,ClippedPadding=clippedPadding,full.Bounds});
        }
        File.WriteAllText(output,JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
    }

    // Replay real saved crops over their captured baseline. No game input or live capture.
    public static void Run(string session,string output)
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(session,"session.json")));
        var baseline=Pixels.Load(Path.Combine(session,"baseline.png"));
        var vision=new Vision();double scale=doc.RootElement.GetProperty("Bank").GetProperty("Scale").GetDouble();
        var results=new List<object>();var replay=new List<object>();
        var anchor=doc.RootElement.GetProperty("Bank").GetProperty("Anchor");
        var bank=new BankLayout(Rectangle.Empty,new Point(anchor.GetProperty("X").GetInt32(),anchor.GetProperty("Y").GetInt32()),scale);
        foreach(var entry in doc.RootElement.GetProperty("Cells").EnumerateArray())
        {
            if(entry.GetProperty("Status").GetString()!="captured")continue;
            int row=entry.GetProperty("Row").GetInt32(),col=entry.GetProperty("Column").GetInt32();
            bool measureGlobal=VerificationConstants.PerformanceSampleCells.Contains((row,col));
            var frame=baseline.Crop(baseline.Bounds);
            var tip=entry.GetProperty("Tooltip");int x=tip.GetProperty("X").GetInt32(),y=tip.GetProperty("Y").GetInt32();
            var crop=Pixels.Load(Path.Combine(session,entry.GetProperty("File").GetString()!));
            for(int yy=0;yy<crop.Height;yy++)Buffer.BlockCopy(crop.Data,yy*crop.Width*Pixels.BytesPerPixel,frame.Data,frame.Offset(x,y+yy),crop.Width*Pixels.BytesPerPixel);
            var cell=bank.Cell(row-1,col-1);var hover=new Point(cell.X+cell.Width/2,cell.Y+cell.Height/2);
            var nearby=vision.FindTooltipNear(frame,scale,hover);
            if(nearby is not null && (vision.TrackTooltip(baseline,scale,nearby) is not null || vision.FooterPresent(baseline,scale,nearby.Footer)))throw new InvalidOperationException($"Old tooltip falsely retained on empty baseline: {row}:{col}");
            if(nearby is null)throw new InvalidOperationException($"Near search missed {row}:{col}");
            var nearTimes=new List<double>();var trackTimes=new List<double>();
            for(int repeat=0;repeat<VerificationConstants.BenchmarkRepeats;repeat++)
            {
                long start=Stopwatch.GetTimestamp();var check=vision.FindTooltipNear(frame,scale,hover);
                nearTimes.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                start=Stopwatch.GetTimestamp();var tracked=vision.TrackTooltip(frame,scale,nearby);
                trackTimes.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                if(check!=nearby || tracked!=nearby)throw new InvalidOperationException($"Tracking mismatch on {row}:{col}");
            }
            nearTimes.Sort();trackTimes.Sort();
            replay.Add(new {Row=row,Column=col,NearMedianMs=nearTimes[nearTimes.Count/2],TrackMedianMs=trackTimes[trackTimes.Count/2],Bounds=nearby.Bounds});
            if(!measureGlobal)continue;
            vision.FindTooltip(frame,scale);
            var times=new List<double>();Tooltip? found=null;
            for(int repeat=0;repeat<VerificationConstants.BenchmarkRepeats;repeat++)
            {
                long start=Stopwatch.GetTimestamp();found=vision.FindTooltip(frame,scale);
                times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            }
            if(found!=nearby)throw new InvalidOperationException($"Full search differs from near search on {row}:{col}");
            times.Sort();results.Add(new {Row=row,Column=col,GlobalMedianMs=times[times.Count/2],NearMedianMs=nearTimes[nearTimes.Count/2],TrackMedianMs=trackTimes[trackTimes.Count/2],Bounds=found?.Bounds,Footer=found?.Footer});
        }
        long absentStart=Stopwatch.GetTimestamp();var absent=vision.FindTooltip(baseline,scale);
        double absentMs=Stopwatch.GetElapsedTime(absentStart).TotalMilliseconds;
        absentStart=Stopwatch.GetTimestamp();var firstCell=bank.Cell(0,0);var absentNear=vision.FindTooltipNear(baseline,scale,new Point(firstCell.X+firstCell.Width/2,firstCell.Y+firstCell.Height/2));
        if(absent is not null || absentNear is not null)throw new InvalidOperationException("False tooltip on baseline");
        var report=new {Configuration=BuildInfo.Configuration,Results=results,Replay=replay,AbsentSearchMs=absentMs,
            AbsentNearMs=Stopwatch.GetElapsedTime(absentStart).TotalMilliseconds,FalseTooltip=absent};
        File.WriteAllText(output,JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
    }
}

