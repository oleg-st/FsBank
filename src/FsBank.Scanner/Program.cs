using FsBank.Scanner.Export;
using FsBank.Scanner.Ocr;
using FsBank.Scanner.UI;

namespace FsBank.Scanner;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 2 && args[0] == "--export-compact")
        {
            try { Console.WriteLine(CompactExport.ConvertFile(Path.GetFullPath(args[1]))); }
            catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
            return;
        }
        if (args.Length == 2 && args[0] == "--recognize")
        {
            try { ItemRecognition.Run(Path.GetFullPath(args[1]), Console.WriteLine, CancellationToken.None).GetAwaiter().GetResult(); }
            catch (Exception e) { File.WriteAllText(Path.Combine(args[1], "ocr-error.txt"), e.ToString()); Environment.ExitCode = 1; }
            return;
        }
        if (args.Length > 0)
        {
            Console.Error.WriteLine("Unknown command or invalid arguments. Diagnostic commands are available in FsBank.Scanner.Diagnostics.");
            Environment.ExitCode = 2;
            return;
        }
        Application.Run(new MainForm());
    }
}
