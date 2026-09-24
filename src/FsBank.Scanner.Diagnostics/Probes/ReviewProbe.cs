using System.Text.Json;
using FsBank.Scanner.Imaging;
using FsBank.Scanner.Ocr;

namespace FsBank.Scanner.Diagnostics.Probes;

internal static class ReviewProbe
{
    public static void Run(string batch,string output)
    {
        Directory.CreateDirectory(output);
        using var summary=JsonDocument.Parse(File.ReadAllText(Path.Combine(batch,"scan-summary.json")));
        using var reader=new NativeTextReader();
        var results=new List<object>();
        foreach(var group in summary.RootElement.GetProperty("Issues").EnumerateArray().GroupBy(i=>
            i.GetProperty("Area").GetString()=="Bank" ? $"tab-{i.GetProperty("Tab").GetInt32():00}" : i.GetProperty("Area").GetString()!.ToLowerInvariant()))
        {
            using var report=JsonDocument.Parse(File.ReadAllText(Path.Combine(batch,group.Key,"full.json")));
            foreach(var issue in group)
            {
                string file=$"r{issue.GetProperty("Row").GetInt32():00}_c{issue.GetProperty("Column").GetInt32():00}.png";
                var item=report.RootElement.GetProperty("items").EnumerateArray().Single(i=>i.GetProperty("file").GetString()==file);
                var pixels=Pixels.Load(Path.Combine(batch,group.Key,file));
                string copyFolder=Path.Combine(output,group.Key);Directory.CreateDirectory(copyFolder);
                string copy=Path.Combine(copyFolder,file);File.Copy(Path.Combine(batch,group.Key,file),copy,true);
                var recheck=ItemRecognition.RecognizeFile(copy,reader,_=> { },CancellationToken.None);
                var parsed=JsonSerializer.SerializeToElement(recheck.Data).GetProperty("item");
                foreach(var line in item.GetProperty("lines").EnumerateArray().Where(l=>l.GetProperty("Confidence").GetInt32()<60))
                {
                    var b=line.GetProperty("Box");var box=new Rectangle(b.GetProperty("X").GetInt32(),b.GetProperty("Y").GetInt32(),b.GetProperty("Width").GetInt32(),b.GetProperty("Height").GetInt32());
                    var variants=new List<object>();
                    foreach(int scale in new[]{2,3,4})foreach(int black in new[]{0,60})
                    {
                        var read=reader.ReadVariant(pixels,box,scale,2,7,black);
                        variants.Add(new{scale,black,read.Text,read.Confidence});
                    }
                    results.Add(new{file=group.Key+"/"+file,index=line.GetProperty("Index").GetInt32(),original=line.GetProperty("Text").GetString(),variants,
                        recheck.Issue,name=parsed.GetProperty("Name").GetString()});
                }
            }
        }
        File.WriteAllText(Path.Combine(output,"probe.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
    }
}
