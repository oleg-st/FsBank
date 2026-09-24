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
        var compactItems=new JsonArray();var summaries=new JsonArray();var links=new List<string>();
        int itemCount=0;
        string output=folder;Directory.CreateDirectory(output);
        // Stream diagnostic rows without materializing/cloning the complete OCR
        // symbol tree. Keep only the small browser projection in memory.
        string fullPath=Path.Combine(output,"full.json");
        string pendingPath=fullPath+".tmp";
        try
        {
            using (var stream=File.Create(pendingPath))
            using (var writer=new Utf8JsonWriter(stream,new JsonWriterOptions { Indented=true }))
            {
                writer.WriteStartObject();writer.WriteStartArray("items");
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
                    int count=0, reviewCount=0;
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
                        using var input=File.OpenRead(CompactExport.FullReportPath(session));
                        using var data=JsonDocument.Parse(input);
                        foreach(var row in data.RootElement.GetProperty("items").EnumerateArray())
                        {
                            token.ThrowIfCancellationRequested();
                            int? itemTab=location=="bank" && number>0 ? number : null;
                            writer.WriteStartObject();
                            foreach(var property in row.EnumerateObject())
                                if(property.Name is not ("tab" or "location_type" or "file"))property.WriteTo(writer);
                            if(itemTab is { } detectedTab)writer.WriteNumber("tab",detectedTab);else writer.WriteNull("tab");
                            writer.WriteString("location_type",location);
                            writer.WriteString("file",name+"/"+row.GetProperty("file").GetString());
                            writer.WriteEndObject();
                            var projectedRow=JsonSerializer.SerializeToElement(new
                            {
                                file=name+"/"+row.GetProperty("file").GetString(), tab=itemTab, location_type=location,
                                item=row.TryGetProperty("item",out var item) ? (JsonElement?)item : null
                            });
                            var compact=CompactExport.Project(projectedRow,itemTab,location);
                            compactItems.Add(compact);count++;itemCount++;
                            // Older reports without a per-item status still require review.
                            if(!row.TryGetProperty("status",out var itemStatus) || itemStatus.GetString() != "recognized")reviewCount++;
                        }
                        writer.Flush();
                        links.Add($"<li><a href='{name}/report.html'>{name}</a>: {count} items; capture status: {WebUtility.HtmlEncode(status)}</li>");
                    }
                    else links.Add($"<li>{name}: no captures; capture status: {WebUtility.HtmlEncode(status)}</li>");
                    summaries.Add(new JsonObject { ["tab"]=number,["location_type"]=location,["capture_status"]=status,["items"]=count,["review_items"]=reviewCount,
                        ["recognition_status"]=available ? reviewCount > 0 ? "needs_visual_review" : "recognized" : "not_scanned" });
                }
                token.ThrowIfCancellationRequested();
                int totalReview=summaries.Sum(s=>s!["review_items"]!.GetValue<int>());
                var summary=new JsonObject { ["items"]=itemCount,["tabs"]=summaries,["review_items"]=totalReview,
                    ["status"]=totalReview > 0 ? "needs_visual_review" : "recognized",["capture_status"]=batch.RootElement.GetProperty("Status").GetString() };
                writer.WriteEndArray();writer.WritePropertyName("summary");summary.WriteTo(writer);writer.WriteEndObject();
            }
            File.Move(pendingPath,fullPath,overwrite:true);
        }
        finally { if(File.Exists(pendingPath))File.Delete(pendingPath); }
        int totalReviewItems=summaries.Sum(s=>s!["review_items"]!.GetValue<int>());
        CompactExport.WriteItems(output,compactItems);
        string report=Path.Combine(output,"report.html");
        File.WriteAllText(report,"<!doctype html><html lang='en'><meta charset='utf-8'><title>FsBank — all tabs</title><style>body{font:18px system-ui;background:#14202c;color:#eee;margin:32px}a{color:#8bd0ff}li{margin:14px 0}</style><h1>Batch recognition</h1><p><a href='items.html'>Compact items</a></p><p>Total items: "+itemCount+". Items needing review: "+totalReviewItems+". Empty or incomplete tabs are marked separately.</p><ul>"+string.Join("",links)+"</ul></html>");
        return report;
    }
}
