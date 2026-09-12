using System.Diagnostics;
using System.Text.Json;

namespace FsBank.Scanner;

internal static class ManualSpeedCheck
{
    public static void Run(string session,string output)
    {
        Directory.CreateDirectory(output);
        using var metadata=JsonDocument.Parse(File.ReadAllText(Path.Combine(session,"session.json")));
        double scale=metadata.RootElement.GetProperty("Layout").GetProperty("Scale").GetDouble();
        var baseline=Pixels.Load(Path.Combine(session,"baseline.png"));
        var results=new List<object>();
        foreach(string file in Directory.GetFiles(session,"waiting-*.png"))
        {
            var frame=Pixels.Load(file);var vision=new Vision();
            var expected=vision.FindTooltip(frame,scale);
            if(expected is null)continue;
            var cursor=new Point(Math.Max(0,expected.Bounds.Left-20),expected.Bounds.Top+30);
            var search=new ManualTooltipSearch(vision,scale);
            long firstStart=Stopwatch.GetTimestamp();var first=search.Find(frame,cursor);
            double firstMs=Stopwatch.GetElapsedTime(firstStart).TotalMilliseconds;
            if(first.Tooltip!=expected)throw new Exception("Local/fallback bounds differ from full search: "+file);
            var globalTimes=new List<double>();var fastTimes=new List<double>();
            for(int i=0;i<5;i++)
            {
                long started=Stopwatch.GetTimestamp();var full=vision.FindTooltip(frame,scale);
                globalTimes.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                started=Stopwatch.GetTimestamp();var fast=search.Find(frame,cursor);
                fastTimes.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                if(full!=expected || !fast.Confirmed || fast.Tooltip!=expected)throw new Exception("Tracking changed crop bounds.");
            }
            long absentStart=Stopwatch.GetTimestamp();var absent=search.Find(baseline,cursor);
            double absentMs=Stopwatch.GetElapsedTime(absentStart).TotalMilliseconds;
            if(absent.Tooltip is not null)throw new Exception("Old tooltip retained on baseline.");
            long wrongCursorStart=Stopwatch.GetTimestamp();var wrong=search.Find(frame,new Point(0,0));
            double wrongCursorMs=Stopwatch.GetElapsedTime(wrongCursorStart).TotalMilliseconds;
            if(wrong.Tooltip is not null)throw new Exception("Tooltip at an unrelated cursor position was accepted.");
            if(search.FullSearches!=0)throw new Exception("Manual search fell back to a global scan.");
            globalTimes.Sort();fastTimes.Sort();
            results.Add(new {Frame=Path.GetFileName(file),Configuration=BuildInfo.Configuration,GlobalMedianMs=globalTimes[2],FirstLocalMs=firstMs,
                TrackedMedianMs=fastTimes[2],AbsentMs=absentMs,WrongCursorMs=wrongCursorMs,search.FullSearches,search.TrackSearches,Bounds=expected.Bounds});
        }
        if(results.Count==0)throw new Exception("No real tooltip frames tested.");
        File.WriteAllText(Path.Combine(output,"timings.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions {WriteIndented=true}));
        File.WriteAllText(Path.Combine(output,"checks.txt"),"Passed: local and tracked searches match full-search crop bounds; absent tooltip and unrelated cursor return no match without global fallback.");
    }
}
