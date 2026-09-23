using FsBank.Scanner.Export;
using FsBank.Scanner.Game;

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FsBank.Scanner.Scanning.Batch;

internal static class CaptureSessions
{
    public static bool IsSession(string path) => File.Exists(Path.Combine(path,"batch.json"))
        || File.Exists(Path.Combine(path,"session.json")) || Directory.EnumerateFiles(path,"r??_c??.png").Any();
    public static string Resolve(string folder)
    {
        folder=Path.GetFullPath(folder);
        if(!Directory.Exists(folder))throw new DirectoryNotFoundException(folder);
        if(IsSession(folder))return folder;
        return Directory.EnumerateDirectories(folder).Where(IsSession).OrderByDescending(Path.GetFileName,StringComparer.Ordinal).FirstOrDefault()
            ?? throw new InvalidOperationException("No capture sessions found in the folder.");
    }
}
internal static class BatchRecognition
{
    public static string Run(string folder, Func<string,string> recognizeTab, Action<string> log, CancellationToken token)
    {
        using var batch=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"batch.json")));
        var tabs=batch.RootElement.GetProperty("Tabs").EnumerateArray().ToArray();
        var combined=new JsonArray();var summaries=new JsonArray();var links=new List<string>();
        string output=folder;Directory.CreateDirectory(output);
        foreach(var tab in tabs)
        {
            token.ThrowIfCancellationRequested();
            int number=tab.GetProperty("Tab").GetInt32();
            string location=tab.TryGetProperty("LocationType",out var kind) ? kind.GetString() ?? "bank" : "bank";
            if(location is not ("bank" or "equipped" or "inventory"))throw new InvalidDataException("Unknown scan location.");
            if(location=="bank" && (number<0 || number>BankGeometry.TabCount))throw new InvalidOperationException("Invalid tab number in batch.json.");
            string name=location=="bank" ? (number>0 ? $"tab-{number:00}" : "tab") : location,session=Path.Combine(folder,name);
            string status=tab.GetProperty("Status").GetString() ?? "unknown";
            bool available=Directory.Exists(session) && (File.Exists(Path.Combine(session,"session.json")) || Directory.EnumerateFiles(session,"r??_c??.png").Any());
            int count=0;
            if(available)
            {
                if(location=="bank" && number==0 && File.Exists(Path.Combine(session,"session.json")))
                {
                    using var metadata=JsonDocument.Parse(File.ReadAllText(Path.Combine(session,"session.json")));
                    if(metadata.RootElement.TryGetProperty("ActiveTab",out var actual) && actual.ValueKind==JsonValueKind.Number && actual.TryGetInt32(out int detected)
                        && detected>=1 && detected<=BankGeometry.TabCount)number=detected;
                }
                log($"OCR: {name}");
                recognizeTab(session);
                var data=JsonNode.Parse(File.ReadAllText(CompactExport.FullReportPath(session)))!;
                foreach(var row in data["items"]!.AsArray())
                {
                    var copy=row!.DeepClone();copy["tab"]=location=="bank" && number>0 ? JsonValue.Create(number) : null;copy["location_type"]=location;
                    copy["file"]=name+"/"+copy["file"]!.GetValue<string>();
                    combined.Add(copy);count++;
                }
                links.Add($"<li><a href='{name}/report.html'>{name}</a>: {count} items; capture status: {WebUtility.HtmlEncode(status)}</li>");
            }
            else links.Add($"<li>{name}: no captures; capture status: {WebUtility.HtmlEncode(status)}</li>");
            summaries.Add(new JsonObject { ["tab"]=number,["location_type"]=location,["capture_status"]=status,["items"]=count,["recognition_status"]=available ? "needs_visual_review" : "not_scanned" });
        }
        token.ThrowIfCancellationRequested();
        var summary=new JsonObject { ["items"]=combined.Count,["tabs"]=summaries,["status"]="needs_visual_review",["capture_status"]=batch.RootElement.GetProperty("Status").GetString() };
        File.WriteAllText(Path.Combine(output,"full.json"),new JsonObject { ["summary"]=summary,["items"]=combined }.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
        CompactExport.Write(output, combined.Select(row => JsonSerializer.SerializeToElement(row)));
        string report=Path.Combine(output,"report.html");
        File.WriteAllText(report,"<!doctype html><html lang='en'><meta charset='utf-8'><title>FsBank — all tabs</title><style>body{font:18px system-ui;background:#14202c;color:#eee;margin:32px}a{color:#8bd0ff}li{margin:14px 0}</style><h1>Batch recognition</h1><p><a href='items.html'>Compact items</a></p><p>Total items: "+combined.Count+". Results require review. Empty or incomplete tabs are marked separately.</p><ul>"+string.Join("",links)+"</ul></html>");
        return report;
    }
}
