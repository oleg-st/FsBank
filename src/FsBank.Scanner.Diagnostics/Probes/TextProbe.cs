using FsBank.Scanner.Imaging;
using FsBank.Scanner.Ocr;

using System.Text.Json;
namespace FsBank.Scanner.Diagnostics.Probes;
internal static class TextProbe
{
    public static void Run(string session, string audit)
    {
        using var input = JsonDocument.Parse(File.ReadAllText(Path.Combine(audit, "snapshot.json")));
        using var errorDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(audit, "observed-errors.json")));
        var errors = errorDoc.RootElement.EnumerateArray().ToArray();
        using var reader = new NativeTextReader();
        List<object> results=[];
        foreach(var row in input.RootElement.GetProperty("items").EnumerateArray())
        {
            string file=row.GetProperty("file").GetString()!;
            var pixels=Pixels.Load(Path.Combine(session,file));
            foreach(var line in row.GetProperty("lines").EnumerateArray())
            {
                int index=line.GetProperty("Index").GetInt32();
                if(!errors.Any(e=>e.GetProperty("file").GetString()==file && e.GetProperty("line").GetInt32()==index) && index!=0)continue;
                var b=line.GetProperty("Box");var box=new Rectangle(b.GetProperty("X").GetInt32(),b.GetProperty("Y").GetInt32(),b.GetProperty("Width").GetInt32(),b.GetProperty("Height").GetInt32());
                List<object> variants=[];
                foreach(var v in new[]{(3,2,7,0),(3,2,7,60),(4,2,7,60),(2,2,7,60),(3,2,13,60),(3,0,7,60)})
                {
                    var r=reader.ReadVariant(pixels,box,v.Item1,v.Item2,v.Item3,v.Item4);
                    variants.Add(new{config=$"{v.Item1}/{v.Item2}/{v.Item3}/{v.Item4}",r.Text,r.Confidence});
                }
                results.Add(new{file,index,original=line.GetProperty("Text").GetString(),variants});
            }
        }
        File.WriteAllText(Path.Combine(audit,"probe.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
    }
}
