using FsBank.Scanner.Export;
using FsBank.Scanner.Game;
using FsBank.Scanner.Ocr;
using FsBank.Scanner.Scanning.Manual;
using System.Text.Json;
using System.Diagnostics;

namespace FsBank.Scanner.Scanning;

internal sealed record ManualSlotProgress(Rectangle Client, ScanLayout Layout, (int,int)[] Pending, (int,int)[] Captured);
internal sealed record ScanOutcome(ScanSnapshot Progress, string[] ExportFolders, string? RecoveryFolder);

internal sealed class ScanController(Action<string> log)
{
    public ScanOutcome Run(ScanOptions options,string destination,bool debug,CancellationToken token,
        Action<ScanSnapshot> changed,Action<ManualSlotProgress> manualSlots)
    {
        if(options.Targets.Length==0)throw new ArgumentException("Select at least one area to scan.");
        // Validate the destination before activating the game or taking input control.
        destination=Path.GetFullPath(destination);
        ValidateDestination(destination);
        var progress=new ScanProgressTracker(options,changed);
        using var export=new ScanExport(destination,debug);
        ScanPhase outcome=ScanPhase.Completed;
        string? problem=null;
        try
        {
            if(options.Mode==ScanMode.Manual)
            {
                new ManualEquippedScanner(log,progress).Run(export.WorkingRoot,token,
                    (client,layout,pending,done)=>manualSlots(new(client,layout,pending,done)));
            }
            else
            {
                var pipeline=new OcrPipeline(log,onQueued:progress.Queued,onRecognized:progress.Recognized);
                Exception? captureError=null;
                try { new ItemScanner(log,pipeline,progress).RunSelected(export.WorkingRoot,ScanConstants.InitialHoverDelayMs,token,options); }
                catch(Exception e) { captureError=e;throw; }
                finally
                {
                    progress.Finishing();
                    try { pipeline.Complete(); }
                    catch(Exception e) when(captureError is not null)
                    {
                        // Keep both failures visible; cancellation must not conceal an OCR failure.
                        problem="Recognition: "+e.Message;
                        log(problem);
                    }
                }
            }
        }
        catch(OperationCanceledException e) { outcome=ScanPhase.Cancelled;problem=Join(problem,string.IsNullOrWhiteSpace(e.Message) ? "Stopped by you." : e.Message); }
        catch(Exception e) { outcome=ScanPhase.Failed;problem=Join(problem,e.Message);log("Scan error: "+e.Message); }
        progress.Finishing("Saving results. You can use the mouse.");
        try { export.Complete(log); }
        catch(Exception e)
        {
            outcome=ScanPhase.Failed;
            problem=Join(problem,$"Could not export: {e.Message} Captures kept at {export.WorkingRoot}");
            log(problem);
        }
        string message=outcome switch
        {
            ScanPhase.Completed => progress.Snapshot.Issues.Length==0 ? "Scan complete. Results are ready." : "Scan complete. Some items need review.",
            ScanPhase.Cancelled => "Stopped. "+problem,
            _ => "Scan could not finish. "+problem
        };
        if(export.Destinations.Count==0 && !export.PreserveWorkingFiles)
            message=(outcome==ScanPhase.Completed ? "Scan complete. No items captured." : message)
                +" No results folder was created.";
        progress.Finish(outcome,message);
        foreach(string folder in export.Destinations)
        {
            var timer=Stopwatch.StartNew();
            try { WriteSummary(folder,progress.Snapshot); }
            catch(Exception e)
            {
                string error="Items were exported, but the scan summary could not be saved: "+e.Message;
                log(error);progress.Finish(ScanPhase.Failed,error);
            }
            finally { log($"Saving: scan summary and issues {timer.Elapsed.TotalSeconds:F3} s."); }
        }
        return new(progress.Snapshot,export.Destinations.ToArray(),export.PreserveWorkingFiles ? export.WorkingRoot : null);
    }
    private static void ValidateDestination(string destination)
    {
        // Probe the nearest existing parent without leaving an empty results root
        // behind when the user cancels while waiting for a game panel.
        string existing=destination;
        while(!Directory.Exists(existing))
        {
            if(File.Exists(existing))throw new IOException("The results folder path points to a file: "+existing);
            existing=Path.GetDirectoryName(existing) ?? throw new DirectoryNotFoundException(destination);
        }
        string probe=Path.Combine(existing,".fsbank-"+Guid.NewGuid().ToString("N"));
        using var file=new FileStream(probe,FileMode.CreateNew,FileAccess.Write,FileShare.None,1,FileOptions.DeleteOnClose);
    }
    private static string Join(string? first,string second) => first is null ? second : first+" "+second;

    internal static void WriteSummary(string folder,ScanSnapshot snapshot)
    {
        File.WriteAllText(Path.Combine(folder,"scan-summary.json"),JsonSerializer.Serialize(new
        {
            Mode=snapshot.Mode.ToString(),Status=snapshot.Phase.ToString(),snapshot.Message,
            Areas=snapshot.Areas.Select(area=>new { Area=area.Area.ToString(),Status=area.Phase.ToString(),area.Items,area.Pending,area.Issues,area.Tab }),
            Issues=snapshot.Issues.Select(issue=>new { Area=issue.Area.ToString(),issue.Tab,issue.Row,issue.Column,issue.Reason, Lines=issue.Evidence?.Lines })
        },new JsonSerializerOptions { WriteIndented=true }));
        ScanIssuesHtml.Write(folder,snapshot);
    }
}
