using FsBank.Scanner.Game;

using System.Text.Json;

namespace FsBank.Scanner.Scanning.Batch;

internal sealed class BatchTab(int number, string locationType="bank")
{
    public int Tab { get; } = number;
    public string LocationType { get; } = locationType;
    public string Folder { get; } = locationType=="bank" ? (number>0 ? $"tab-{number:00}" : "tab") : locationType;
    public string Status { get; set; } = "pending";
    public int Captured { get; set; }
    public int Missing { get; set; }
    public string? Error { get; set; }
}
internal static class BatchScan
{
    // Separate orchestration from game input, so cancellation/partial batches can be tested offline.
    public static string Run(string root, Action<int,string> scanTab, Action<string> log, CancellationToken token,bool everything=false)
    {
        token.ThrowIfCancellationRequested();
        root=Path.GetFullPath(root);
        string folder=Path.Combine(root,DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+(everything ? "-all" : "-all-tabs"));
        Directory.CreateDirectory(folder);
        var tabs=(everything ? new[]{new BatchTab(-1,"equipped"),new BatchTab(0,"inventory")} : Array.Empty<BatchTab>()).Concat(Enumerable.Range(1,BankGeometry.TabCount).Select(n=>new BatchTab(n))).ToArray();
        string status="running";string? error=null;
        void Save() => File.WriteAllText(Path.Combine(folder,"batch.json"),JsonSerializer.Serialize(new
        { Status=status,Error=error,Mode=everything ? "all" : "all_tabs",TabCount=BankGeometry.TabCount,TooltipMode="alt_details",Tabs=tabs },new JsonSerializerOptions{WriteIndented=true}));
        Save();
        try
        {
            foreach(var tab in tabs)
            {
                token.ThrowIfCancellationRequested();
                tab.Status="running";Save();
                log($"Scanning {tab.Folder}");
                try
                {
                    scanTab(tab.Tab-1,Path.Combine(folder,tab.Folder));
                    using var session=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,tab.Folder,"session.json")));
                    var record=session.RootElement;
                    if(record.GetProperty("Status").GetString()!="completed")throw new InvalidOperationException("Tab capture did not complete: "+record.GetProperty("Status").GetString());
                    if(tab.LocationType=="bank" && record.GetProperty("ActiveTab").GetInt32()!=tab.Tab)throw new InvalidOperationException("The captured tab number does not match the requested tab.");
                    var cells=record.GetProperty("Cells").EnumerateArray().ToArray();
                    tab.Captured=cells.Count(c=>c.GetProperty("Status").GetString()=="captured");
                    tab.Missing=cells.Count(c=>c.GetProperty("Status").GetString() is not ("captured" or "empty"));
                    tab.Status=tab.Missing>0 ? "completed_with_missing" : "completed";
                }
                catch(OperationCanceledException e){tab.Status="cancelled";tab.Error=e.Message;throw;}
                catch(Exception e){tab.Status="failed";tab.Error=e.Message;throw;}
                finally{Save();}
            }
            status=tabs.Any(t=>t.Missing>0) ? "completed_with_missing" : "completed";
            return folder;
        }
        catch(OperationCanceledException e){status="cancelled";error=e.Message;throw;}
        catch(Exception e){status="failed";error=e.Message;throw;}
        finally{Save();log("All-tabs export: "+folder);}
    }
}
