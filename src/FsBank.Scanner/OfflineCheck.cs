using System.Diagnostics;
using System.Text.Json;

namespace FsBank.Scanner;

internal static class OfflineCheck
{
    public static void Run(string directory)
    {
        string output=Path.Combine(Directory.GetCurrentDirectory(),"scanner-check");Directory.CreateDirectory(output);
        var vision=new Vision();var results=new List<object>();
        foreach(string path in Directory.GetFiles(directory,"*.png"))
        {
            var pixels=Pixels.Load(path);var watch=Stopwatch.StartNew();
            var bank=vision.FindBank(pixels);var tip=vision.FindTooltip(pixels,bank?.Scale ?? pixels.Height/(double)GameConstants.ReferenceHeight);
            if(tip is not null)pixels.Crop(tip.Bounds).Save(Path.Combine(output,Path.GetFileName(path)));
            using var annotated=pixels.Bitmap();using var g=Graphics.FromImage(annotated);
            int? occupied=null;
            if(bank is not null)
            {
                occupied=0;
                for(int r=0;r<BankGeometry.Rows;r++)for(int c=0;c<BankGeometry.Columns;c++)
                {
                    var cell=bank.Cell(r,c);bool has=Vision.Occupied(pixels,cell);if(has)occupied++;
                    using var pen=new Pen(has?Color.Lime:Color.Red,VerificationConstants.CellOutlineWidthPx);g.DrawRectangle(pen,cell);
                }
            }
            if(tip is not null){using var pen=new Pen(Color.Yellow,VerificationConstants.TooltipOutlineWidthPx);g.DrawRectangle(pen,tip.Bounds);}
            annotated.Save(Path.Combine(output,Path.GetFileNameWithoutExtension(path)+"-annotated.png"));
            results.Add(new {File=path,Bank=bank,Tooltip=tip,Occupied=occupied,ActiveTab=bank is null ? -1 : Vision.ActiveTab(pixels,bank),Milliseconds=watch.ElapsedMilliseconds});
            if(Path.GetFileName(path).Contains(VerificationConstants.CleanBankFixtureNamePart) && bank is not null)
            {
                var negative=pixels.Crop(pixels.Bounds);
                for(int y=bank.Bounds.Top;y<bank.Bounds.Bottom;y++)
                    Array.Clear(negative.Data,negative.Offset(bank.Bounds.Left,y),bank.Bounds.Width*Pixels.BytesPerPixel);
                if(vision.FindBank(negative) is not null)throw new InvalidOperationException("False bank match on masked negative control.");
            }
        }
        File.WriteAllText(Path.Combine(output,"report.json"),JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));
    }
}
