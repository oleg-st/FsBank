using FsBank.Scanner.Export;
using FsBank.Scanner.Imaging;
using FsBank.Scanner.Scanning.Batch;

using System.Text.Json;
using System.Text.Json.Nodes;

namespace FsBank.Scanner.Diagnostics.Checks;

internal static class BatchCheck
{
    public static void Run(string output, string sampleSession)
    {
        Directory.CreateDirectory(output);
        void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        void WriteSession(int tab,string folder)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder,"session.json"),JsonSerializer.Serialize(new { Status="completed",ActiveTab=tab+1,Cells=new[]{new {Status=tab==6 ? "empty" : "captured"}} }));
        }
        List<int> order=[];
        string complete=BatchScan.Run(Path.Combine(output,"complete"),(tab,folder)=>{order.Add(tab);WriteSession(tab,folder);},_=>{},CancellationToken.None);
        Require(order.SequenceEqual(Enumerable.Range(0,7)),"Seven tabs not visited once in order");
        Require(CaptureSessions.Resolve(Path.GetDirectoryName(complete)!)==complete,"Latest batch discovery");
        Require(CaptureSessions.Resolve(complete)==complete,"Explicit batch discovery");
        Require(CaptureSessions.Resolve(Path.Combine(complete,"tab-07"))==Path.Combine(complete,"tab-07"),"Empty tab discovery");
        var partial=BatchScan.Run(Path.Combine(output,"incomplete"),(tab,folder)=>
        {
            WriteSession(tab,folder);
            if(tab==0)File.WriteAllText(Path.Combine(folder,"session.json"),JsonSerializer.Serialize(new
            {Status="completed",ActiveTab=1,Cells=new[]{new {Status="incomplete_tooltip"}}}));
        },_=>{},CancellationToken.None);
        using(var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(partial,"batch.json"))))
            Require(doc.RootElement.GetProperty("Status").GetString()=="completed_with_missing"
                && doc.RootElement.GetProperty("Tabs")[0].GetProperty("Missing").GetInt32()==1,"Clipped tooltip silently counted as complete");
        using(var cancellation=new CancellationTokenSource())
        {
            int calls=0;string? cancelled=null;
            try
            {
                BatchScan.Run(Path.Combine(output,"cancel"),(tab,folder)=>
                {
                    calls++;cancelled=Path.GetDirectoryName(folder);WriteSession(tab,folder);
                    if(tab==1){cancellation.Cancel();cancellation.Token.ThrowIfCancellationRequested();}
                },_=>{},cancellation.Token);
                throw new InvalidOperationException("Cancellation swallowed");
            }
            catch(OperationCanceledException){}
            using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(cancelled!,"batch.json")));
            var tabs=doc.RootElement.GetProperty("Tabs");
            Require(calls==2 && doc.RootElement.GetProperty("Status").GetString()=="cancelled" && tabs[0].GetProperty("Status").GetString()=="completed" && tabs[1].GetProperty("Status").GetString()=="cancelled" && tabs[2].GetProperty("Status").GetString()=="pending","Cancellation lost partial results or continued");
        }
        int failedCalls=0;string? failed=null;
        try
        {
            BatchScan.Run(Path.Combine(output,"failed"),(tab,folder)=>
            {failedCalls++;failed=Path.GetDirectoryName(folder);WriteSession(tab,folder);if(tab==2)throw new IOException("simulated failure");},_=>{},CancellationToken.None);
        }
        catch(IOException){}
        using(var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(failed!,"batch.json"))))
            Require(failedCalls==3 && doc.RootElement.GetProperty("Status").GetString()=="failed","Failure did not stop later tabs");
        // Aggregate duplicate filenames without losing their tab identity, including an empty tab.
        string report=BatchRecognition.Run(complete,folder=>
        {
            string ocr=folder;Directory.CreateDirectory(ocr);
            var items=new JsonArray();
            if(Path.GetFileName(folder)!="tab-07")items.Add(new JsonObject{["file"]="r01_c01.png",["status"]="needs_visual_review"});
            File.WriteAllText(Path.Combine(ocr,"full.json"),new JsonObject{["items"]=items}.ToJsonString());
            File.WriteAllText(Path.Combine(ocr,"report.html"),"test");return Path.Combine(ocr,"report.html");
        },_=>{},CancellationToken.None);
        using(var doc=JsonDocument.Parse(File.ReadAllText(CompactExport.FullReportPath(complete))))
        {
            var items=doc.RootElement.GetProperty("items").EnumerateArray().ToArray();
            Require(items.Length==6 && items.Select(x=>x.GetProperty("file").GetString()).Distinct().Count()==6,"Duplicate cell filenames collided");
            Require(doc.RootElement.GetProperty("summary").GetProperty("tabs").GetArrayLength()==7,"Empty tab omitted");
        }
        var allOrder=new List<int>();
        string all=BatchScan.Run(Path.Combine(output,"all"),(index,folder)=>
        {
            allOrder.Add(index);
            WriteSession(index,folder);
        },_=>{},CancellationToken.None,everything:true);
        Require(allOrder.SequenceEqual(Enumerable.Range(-2,9)),"All scan must visit equipped, inventory and seven bank tabs");
        BatchRecognition.Run(all,folder=>
        {
            File.WriteAllText(Path.Combine(folder,"full.json"),"{\"items\":[{\"file\":\"r01_c01.png\",\"item\":{\"Name\":\"Test <item>\"}}]}");
            return Path.Combine(folder,"report.html");
        },_=>{},CancellationToken.None);
        using(var compact=JsonDocument.Parse(File.ReadAllText(Path.Combine(all,"items.json"))))
        {
            var items=compact.RootElement.EnumerateArray().ToArray();
            Require(items.Length==9,"Combined export lost items");
            Require(items[0].GetProperty("location").GetProperty("type").GetString()=="equipped" && items[1].GetProperty("location").GetProperty("type").GetString()=="inventory","Character locations lost");
            Require(items[0].GetProperty("location").GetProperty("tab").ValueKind==JsonValueKind.Null,"Equipped item assigned bank tab");
            Require(items[8].GetProperty("location").GetProperty("tab").GetInt32()==7,"Last bank tab lost");
            string html=File.ReadAllText(Path.Combine(all,"items.html"));
            Require(html.Contains("equipped/r01_c01.png") && html.Contains("tab-07/r01_c01.png") && html.Contains("Test &lt;item&gt;") && !html.Contains("location_type"),"Compact HTML images, escaping or schema incorrect");
        }
        var pixels=Pixels.Load(Path.Combine(sampleSession,"baseline.png"));var bank=new Vision().FindBank(pixels) ?? throw new InvalidOperationException("Sample bank not found");
        Require(Vision.ActiveTab(pixels,bank)>=0,"Active sample tab not recognized");
        using(var strip=pixels.Crop(bank.Tabs).Bitmap())
        {
            using(var g=Graphics.FromImage(strip))
            {
                for(int tab=0;tab<7;tab++)
                {
                    var center=bank.TabCenter(tab);Require(bank.Tabs.Contains(center),"Tab click outside strip");
                    Require(!bank.Grid.Contains(center),"Tab click hits inventory");
                    if(tab>0)Require(center.X>bank.TabCenter(tab-1).X,"Tab centres out of order");
                    g.FillEllipse(Brushes.Lime,center.X-bank.Tabs.X-3,center.Y-bank.Tabs.Y-3,6,6);
                }
            }
            strip.Save(Path.Combine(output,"tab-click-centres.png"));
        }
        File.WriteAllText(Path.Combine(output,"checks.txt"),"Passed: 7-tab order, cancellation, failure stop, empty-tab discovery, batch aggregation with duplicate filenames, and click geometry on saved baseline. No live game input performed.");
    }
}
