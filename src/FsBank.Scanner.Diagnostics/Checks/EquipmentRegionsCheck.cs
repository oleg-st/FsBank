using FsBank.Scanner.Imaging;
using FsBank.Scanner.Ocr;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FsBank.Scanner.Diagnostics.Checks;

internal static class EquipmentRegionsCheck
{
    public static void Run(string source,string output)
    {
        Directory.CreateDirectory(output);
        using var reader=new NativeTextReader();
        int accepted=0,uncertain=0,variants=0,variantUncertain=0;
        var slots=new HashSet<string>();var files=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var failures=new List<string>();
        foreach(string report in Directory.EnumerateFiles(source,"full.json",SearchOption.AllDirectories))
        {
            using var doc=JsonDocument.Parse(File.ReadAllText(report));
            if(!doc.RootElement.TryGetProperty("items",out var items))continue;
            foreach(var item in items.EnumerateArray())
            {
                string file=Path.Combine(Path.GetDirectoryName(report)!,item.GetProperty("file").GetString()!);
                if(!File.Exists(file) || !item.TryGetProperty("lines",out var lines) || !files.Add(Path.GetFullPath(file)))continue;
                var rows=lines.EnumerateArray().ToArray();
                int rarity=Array.FindIndex(rows,l=>Regex.IsMatch(l.GetProperty("Text").GetString()!,@"^\s*(Common|Uncommon|Rare|Epic|Heroic|Regal|Legendary)\b"));
                if(rarity<1)continue;
                string slot=item.GetProperty("item").GetProperty("Slot").GetString()!;
                int level=item.GetProperty("item").GetProperty("ItemLevel").GetInt32();
                string expected=slot+" "+level;
                var b=rows[rarity-1].GetProperty("Box");var box=new Rectangle(b.GetProperty("X").GetInt32(),b.GetProperty("Y").GetInt32(),b.GetProperty("Width").GetInt32(),b.GetProperty("Height").GetInt32());
                var pixels=Pixels.Load(file);var read=reader.ReadEquipment(pixels,box);
                if(read is null)uncertain++;
                else if(read.Value.Text!=expected)failures.Add(file+": "+read.Value.Text+" != "+expected);
                else accepted++;
                if(!slots.Add(slot))continue;
                foreach(double scale in new[]{.8,1.25,1.5})
                {
                    using var original=pixels.Bitmap();
                    using var bitmap=new Bitmap((int)Math.Round(pixels.Width*scale)+16,(int)Math.Round(pixels.Height*scale)+12);
                    using(var g=Graphics.FromImage(bitmap))
                    {
                        g.Clear(Color.FromArgb(20,30,40));
                        g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(original,new Rectangle(8,6,bitmap.Width-16,bitmap.Height-12));
                    }
                    string variantFile=Path.Combine(output,$"{slot}-{scale:F2}.png");bitmap.Save(variantFile);
                    var shifted=Rectangle.FromLTRB(8+(int)Math.Floor(box.Left*scale),6+(int)Math.Floor(box.Top*scale),
                        8+(int)Math.Ceiling(box.Right*scale),6+(int)Math.Ceiling(box.Bottom*scale));
                    var variant=reader.ReadEquipment(Pixels.Load(variantFile),shifted);variants++;
                    if(variant is null)variantUncertain++;
                    else if(variant.Value.Text!=expected)failures.Add(variantFile+": "+variant.Value.Text+" != "+expected);
                }
            }
        }
        File.WriteAllText(Path.Combine(output,"checks.json"),JsonSerializer.Serialize(new {accepted,uncertain,slots,variants,variantUncertain,failures},new JsonSerializerOptions{WriteIndented=true}));
        if(accepted==0 || failures.Count>0)throw new InvalidOperationException("Equipment region errors: "+string.Join("; ",failures));
        Console.WriteLine($"Equipment regions: {accepted} agreed, {uncertain} uncertain, {slots.Count} slots; transformed cases {variants}, uncertain {variantUncertain}; zero incorrect accepted fields.");
    }
}
