using FsBank.Scanner.Export;
using FsBank.Scanner.Game;
using FsBank.Scanner.Ocr;
using FsBank.Scanner.Scanning.Manual;
using System.Net;
using System.Text.Json;

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
            try { WriteSummary(folder,progress.Snapshot); }
            catch(Exception e)
            {
                string error="Items were exported, but the scan summary could not be saved: "+e.Message;
                log(error);progress.Finish(ScanPhase.Failed,error);
            }
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
            Issues=snapshot.Issues.Select(issue=>new { Area=issue.Area.ToString(),issue.Tab,issue.Row,issue.Column,issue.Reason })
        },new JsonSerializerOptions { WriteIndented=true }));
        string rows=string.Concat(snapshot.Issues.Select(issue=>$"<tr><td>{issue.Area}</td><td>{issue.Tab?.ToString() ?? "—"}</td><td>{issue.Row}:{issue.Column}</td><td>{WebUtility.HtmlEncode(issue.Reason)}</td></tr>"));
        File.WriteAllText(Path.Combine(folder,"scan-issues.html"),"<!doctype html><html lang='en'><meta charset='utf-8'><meta name='viewport' content='width=device-width'><title>FsBank scan results</title><style>body{font:16px/1.5 system-ui;max-width:1000px;margin:32px auto;padding:0 20px;color:#202a35}table{border-collapse:collapse;width:100%}td,th{padding:10px;text-align:left;border-bottom:1px solid #ddd;overflow-wrap:anywhere}a{color:#175da8}</style><h1>Scan results</h1><p>"+WebUtility.HtmlEncode(snapshot.Message)+"</p><p><a href='items.html'>View items</a></p><p>Recognized without issues: "+snapshot.Items+" · Needs review: "+snapshot.Issues.Length+". Recognized values should be reviewed before use.</p><table><thead><tr><th>Area</th><th>Bank tab</th><th>Slot</th><th>Details</th></tr></thead><tbody>"+rows+"</tbody></table></html>");
    }
}
