using FsBank.Scanner.Diagnostics.Benchmarks;
using FsBank.Scanner.Diagnostics.Checks;
using FsBank.Scanner.Diagnostics.Probes;

namespace FsBank.Scanner.Diagnostics;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        if (args.Length == 0 || args is ["--help"])
        {
            Console.WriteLine("""
                FsBank.Scanner.Diagnostics commands:
                  --verify-export <output>
                  --verify-result-quality <output>
                  --verify-ui <output>
                  --verify-review-warnings <20260923-232716-859/equipped> <output>
                  --verify-full-review <saved-full-scan> <output>
                  --probe-review <batch> <output>
                  --inspect-capture-layout <batch> <output>
                  --verify-capture-recovery <20260922-205823-892-all> <output>
                  --verify-character-panel <image> <output>
                  --verify-tooltip-edge <crop> <output.json>
                  --verify-manual-startup <image> <output>
                  --verify-manual-speed <session> <output>
                  --verify-equipped <image> <output>
                  --verify-ocr-pipeline <sample> <output>
                  --benchmark-ocr <session> <output>
                  --verify-header-regression <batch> <output>
                  --verify-headers <batch> <output>
                  --verify-quality <batch>
                  --verify-batch <output> <sample-session>
                  --probe-text <session> <output>
                  --verify-recognition <session>
                  --benchmark-vision <session> <output.json>
                  --verify-screens [screens]
                """);
            return;
        }
        if(args.Length==3 && args[0]=="--verify-full-review")
        {
            try { ReviewWarningsCheck.FullScan(Path.GetFullPath(args[1]),Path.GetFullPath(args[2])); }
            catch(Exception e) { Console.Error.WriteLine(e); Environment.ExitCode=1; }
            return;
        }
        if(args.Length==2 && args[0]=="--verify-result-quality")
        {
            try { ResultQualityCheck.Run(Path.GetFullPath(args[1])); }
            catch(Exception e) { Console.Error.WriteLine(e); Environment.ExitCode=1; }
            return;
        }
        if(args.Length==3 && args[0]=="--probe-review")
        {
            try { ReviewProbe.Run(Path.GetFullPath(args[1]),Path.GetFullPath(args[2])); }
            catch(Exception e) { Console.Error.WriteLine(e); Environment.ExitCode=1; }
            return;
        }
        if(args.Length==3 && args[0]=="--verify-review-warnings")
        {
            try { ReviewWarningsCheck.Run(Path.GetFullPath(args[1]),Path.GetFullPath(args[2])); }
            catch(Exception e) { Console.Error.WriteLine(e); Environment.ExitCode=1; }
            return;
        }
        if(args.Length==2 && args[0]=="--verify-ui")
        {
            try { ScanUiCheck.Run(Path.GetFullPath(args[1])); }
            catch(Exception e) { Console.Error.WriteLine(e); Environment.ExitCode=1; }
            return;
        }
        if(args.Length==2 && args[0]=="--verify-export")
        {
            try { ScanExportCheck.Run(Path.GetFullPath(args[1])); }
            catch(Exception e) { Console.Error.WriteLine(e); Environment.ExitCode=1; }
            return;
        }
        if(args.Length==3 && args[0]=="--inspect-capture-layout")
        {
            try { QualityCheck.InspectCaptureLayout(args[1],args[2]); }
            catch(Exception e) { Console.Error.WriteLine(e); Environment.ExitCode=1; }
            return;
        }
        if(args.Length==3 && args[0]=="--verify-capture-recovery")
        {
            try { CaptureRecoveryCheck.Run(args[1],args[2]); }
            catch(Exception e) { Console.Error.WriteLine(e); Environment.ExitCode=1; }
            return;
        }
        if(args.Length==3 && args[0]=="--verify-character-panel")
        {
            try { EquippedCheck.CharacterPanel(args[1],args[2]); }
            catch(Exception e) { Directory.CreateDirectory(args[2]);File.WriteAllText(Path.Combine(args[2],"error.txt"),e.ToString());Environment.ExitCode=1; }
            return;
        }
        if(args.Length==3 && args[0]=="--verify-tooltip-edge")
        {
            try { VisionBenchmark.EdgeRegression(args[1],args[2]); }
            catch(Exception e) { File.WriteAllText(args[2]+".error",e.ToString());Environment.ExitCode=1; }
            return;
        }
        if(args.Length==3 && args[0]=="--verify-manual-startup")
        {
            try { ManualStartupCheck.Run(args[1],args[2]); }
            catch(Exception e) { Directory.CreateDirectory(args[2]);File.WriteAllText(Path.Combine(args[2],"error.txt"),e.ToString());Environment.ExitCode=1; }
            return;
        }
        if(args.Length==3 && args[0]=="--verify-manual-speed")
        {
            try { ManualSpeedCheck.Run(args[1],args[2]); }
            catch(Exception e) { Directory.CreateDirectory(args[2]);File.WriteAllText(Path.Combine(args[2],"error.txt"),e.ToString());Environment.ExitCode=1; }
            return;
        }
        if(args.Length==3 && args[0]=="--verify-equipped")
        {
            try { EquippedCheck.Run(args[1],args[2]); }
            catch(Exception e) { Directory.CreateDirectory(args[2]);File.WriteAllText(Path.Combine(args[2],"error.txt"),e.ToString());Environment.ExitCode=1; }
            return;
        }

        if(args.Length==3 && args[0]=="--verify-ocr-pipeline")
        {
            try { OcrPipelineCheck.Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2])); }
            catch(Exception e) { Directory.CreateDirectory(args[2]); File.WriteAllText(Path.Combine(args[2], "error.txt"), e.ToString()); Environment.ExitCode=1; }
            return;
        }
        if(args.Length==3 && args[0]=="--benchmark-ocr")
        {
            try { OcrBenchmark.Run(Path.GetFullPath(args[1]), Path.GetFullPath(args[2])); }
            catch(Exception e) { Directory.CreateDirectory(args[2]); File.WriteAllText(Path.Combine(args[2], "error.txt"), e.ToString()); Environment.ExitCode=1; }
            return;
        }
        if(args.Length==3 && args[0]=="--verify-header-regression")
        {
            try{QualityCheck.HeaderRegression(args[1],args[2]);}
            catch(Exception e){Directory.CreateDirectory(args[2]);File.WriteAllText(Path.Combine(args[2],"error.txt"),e.ToString());Environment.ExitCode=1;}
            return;
        }
        if(args.Length==3 && args[0]=="--verify-headers")
        {
            try{QualityCheck.Headers(args[1],args[2]);}
            catch(Exception e){Directory.CreateDirectory(args[2]);File.WriteAllText(Path.Combine(args[2],"error.txt"),e.ToString());Environment.ExitCode=1;}
            return;
        }
        if(args.Length==2 && args[0]=="--verify-quality")
        {
            try{QualityCheck.Run(Path.GetFullPath(args[1]));}
            catch(Exception e){File.WriteAllText(Path.Combine(args[1],"quality-check-error.txt"),e.ToString());Environment.ExitCode=1;}
            return;
        }
        if(args.Length==3 && args[0]=="--verify-batch")
        {
            try{BatchCheck.Run(args[1],args[2]);}
            catch(Exception e){Directory.CreateDirectory(args[1]);File.WriteAllText(Path.Combine(args[1],"error.txt"),e.ToString());Environment.ExitCode=1;}
            return;
        }
        if (args.Length == 3 && args[0] == "--probe-text")
        {
            try { TextProbe.Run(args[1], args[2]); }
            catch (Exception e) { File.WriteAllText(Path.Combine(args[2], "probe-error.txt"), e.ToString()); Environment.ExitCode = 1; }
            return;
        }
        if (args.Length == 2 && args[0] == "--verify-recognition")
        {
            try { RecognitionCheck.Run(Path.GetFullPath(args[1])); }
            catch (Exception e) { File.WriteAllText(Path.Combine(args[1], "recognition-check-error.txt"), e.ToString()); Environment.ExitCode = 1; }
            return;
        }

        if(args.Length==3 && args[0]=="--benchmark-vision")
        {
            try{VisionBenchmark.Run(args[1],args[2]);}
            catch(Exception e){File.WriteAllText(args[2]+".error",e.ToString());Environment.ExitCode=1;}
            return;
        }
        if (args.Length > 0 && args[0] == "--verify-screens")
        {
            try { OfflineCheck.Run(args.Length > 1 ? args[1] : "screens"); }
            catch (Exception e) { File.WriteAllText("verify-error.txt",e.ToString()); Environment.ExitCode=1; }
            return;
        }
        Console.Error.WriteLine("Unknown command or invalid arguments. Use --help to list diagnostic commands.");
        Environment.ExitCode = 2;
    }
}
