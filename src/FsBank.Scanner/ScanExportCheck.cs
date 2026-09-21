using System.Text.Json;

namespace FsBank.Scanner;

internal static class ScanExportCheck
{
    public static void Run(string root)
    {
        foreach (bool debug in new[] { false, true })
        foreach (bool partial in new[] { false, true })
        {
            string output = Path.Combine(root, $"batch-{debug}-{partial}");
            using var export = new ScanExport(output, debug);
            try
            {
                BatchScan.Run(export.WorkingRoot, (index, session) =>
                {
                    Directory.CreateDirectory(session);
                    File.WriteAllText(Path.Combine(session, "session.json"), JsonSerializer.Serialize(new
                    { Status="completed", ActiveTab=index+1, Cells=Array.Empty<object>() }));
                    File.WriteAllText(Path.Combine(session, "full.json"), "{\"items\":[]}");
                    File.WriteAllText(Path.Combine(session, "report.html"), "test");
                    if (partial) throw new OperationCanceledException();
                }, _ => {}, CancellationToken.None, everything:true);
            }
            catch (OperationCanceledException) when (partial) { }
            export.Complete(_ => {});
            string result = Directory.GetDirectories(output).Single();
            int count = Directory.GetDirectories(result).Length;
            if (count != (debug ? partial ? 1 : 9 : 0)) throw new Exception("Unexpected batch folders");
            if (!debug && Directory.GetFiles(result).Length != 2) throw new Exception("Unexpected compact batch files");
        }
        foreach (bool debug in new[] { false, true })
        foreach (string location in new[] { "equipped", "inventory", "bank", "unknown" })
        {
            string output = Path.Combine(root, debug + "-" + location);
            using var export = new ScanExport(output, debug);
            string session = Path.Combine(export.WorkingRoot, "capture");
            Directory.CreateDirectory(session);
            File.WriteAllText(Path.Combine(session, "session.json"), JsonSerializer.Serialize(new
            { LocationType = location == "unknown" ? "bank" : location, ActiveTab = location == "bank" ? 3 : 0, Status = "cancelled", Cells = Array.Empty<object>() }));
            File.WriteAllText(Path.Combine(session, "full.json"), "{\"items\":[{\"file\":\"r01_c01.png\",\"item\":{}}]}");
            File.WriteAllBytes(Path.Combine(session, "r01_c01.png"), [1, 2, 3]);
            File.WriteAllText(Path.Combine(session, "report.html"), "test");
            export.Complete(_ => {});
            string result = Path.Combine(output, "capture");
            string expected = location == "bank" ? "tab-03" : location == "unknown" ? "tab" : location;
            var directories = Directory.GetDirectories(result).Select(Path.GetFileName).ToArray();
            if (debug ? !directories.SequenceEqual(new[] { expected }) : directories.Length != 0)
                throw new Exception("Unexpected export directories: " + output);
            var files = Directory.GetFiles(result).Select(Path.GetFileName).Order().ToArray();
            string[] expectedFiles = debug ? ["batch.json", "full.json", "items.html", "items.json", "report.html"] : ["items.html", "items.json"];
            if (!files.SequenceEqual(expectedFiles)) throw new Exception("Unexpected export files");
            string html = File.ReadAllText(Path.Combine(result, "items.html"));
            if (!debug && (!html.Contains("data:image/png;base64,AQID") || html.Contains("href='report.html'")))
                throw new Exception("Compact HTML is not self-contained");
            using var items = JsonDocument.Parse(File.ReadAllText(Path.Combine(result, "items.json")));
            if (items.RootElement[0].GetProperty("location").GetProperty("type").GetString() != (location == "unknown" ? "bank" : location))
                throw new Exception("Incorrect location");
        }
    }
}
