namespace FsBank.Scanner;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if(args.Length==2 && args[0]=="--verify-export")
        {
            try { ScanExportCheck.Run(Path.GetFullPath(args[1])); }
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
        if (args.Length == 2 && args[0] == "--export-compact")
        {
            try { Console.WriteLine(CompactExport.ConvertFile(Path.GetFullPath(args[1]))); }
            catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
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
        if (args.Length == 2 && args[0] == "--recognize")
        {
            try { ItemRecognition.Run(Path.GetFullPath(args[1]), Console.WriteLine, CancellationToken.None).GetAwaiter().GetResult(); }
            catch (Exception e) { File.WriteAllText(Path.Combine(args[1], "ocr-error.txt"), e.ToString()); Environment.ExitCode = 1; }
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
        Application.Run(new MainForm());
    }
}
