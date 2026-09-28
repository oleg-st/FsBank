using FsBank.Scanner.Imaging;
using FsBank.Scanner.Ocr;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace FsBank.Scanner.Diagnostics.Checks;

internal static class CollapsedTooltipCheck
{
    public static void Run(string source,string valid,string output)
    {
        Directory.CreateDirectory(output);
        using var quality=new TooltipCaptureQuality();
        var cards=Regex.Matches(File.ReadAllText(Path.Combine(source,"scan-issues.html")),@"<article>(.*?)</article>",RegexOptions.Singleline);
        int rejected=0,accepted=0;var failures=new List<string>();var watch=Stopwatch.StartNew();
        foreach(Match card in cards)
        {
            var image=Regex.Match(card.Value,@"data:image/png;base64,([A-Za-z0-9+/=]+)");
            if(!image.Success)continue;
            string file=Path.Combine(output,$"collapsed-{rejected++:00}.png");
            File.WriteAllBytes(file,Convert.FromBase64String(image.Groups[1].Value));
            if(quality.HasReadableStats(Pixels.Load(file)))failures.Add("Accepted collapsed text: "+file);
        }
        foreach(string folder in Directory.GetDirectories(valid))
        foreach(string file in Directory.GetFiles(folder,"r??_c??.png"))
        {
            accepted++;
            if(!quality.HasReadableStats(Pixels.Load(file)))failures.Add("Rejected valid stats: "+file);
        }
        string summary=$"Collapsed: {rejected}; valid: {accepted}; duration: {watch.Elapsed.TotalSeconds:F2}s; failures: {failures.Count}";
        File.WriteAllLines(Path.Combine(output,"checks.txt"),new[]{summary}.Concat(failures));
        Console.WriteLine(summary);foreach(var failure in failures)Console.WriteLine(failure);
        if(rejected!=17 || accepted==0 || failures.Count>0)throw new InvalidOperationException("Capture quality regression failed");
    }
}
