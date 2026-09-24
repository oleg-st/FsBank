using FsBank.Scanner.Game;
using FsBank.Scanner.Imaging;
using System.Diagnostics;
using static FsBank.Scanner.Game.BankGeometry;

namespace FsBank.Scanner.Scanning;

// Validate hints on every fresh frame, then search a bounded group of tiles. A closed
// panel must not stall cancellation or delay detection when the user opens it.
internal sealed class PanelSearch(Vision vision)
{
    private ScanLayout? character,bank;
    private Size frameSize;
    private ScanTarget? previousTarget;
    private double[] scales=[];
    private int scaleIndex,x,y;
    private const int TileWidth=192,TileHeight=48,MaxTilesPerFrame=8,SearchBudgetMs=12;
    public ScanLayout? Find(Pixels frame,ScanTarget target)
    {
        if(frameSize!=new Size(frame.Width,frame.Height) || previousTarget!=target)
        {
            frameSize=new(frame.Width,frame.Height);previousTarget=target;
            double basis=frame.Height/(double)GameConstants.ReferenceHeight;
            scales=new[] {basis,NativeScale}.Concat(RelativeScales.ToArray().Skip(1).Select(s=>basis*s)).Distinct().ToArray();
            scaleIndex=0;x=0;y=0;
        }
        var found=vision.FindLayoutFast(frame,target,target==ScanTarget.Bank ? bank : character,fallback:false);
        long started=Stopwatch.GetTimestamp();
        for(int probe=0;found is null && probe<MaxTilesPerFrame &&
            (probe==0 || Stopwatch.GetElapsedTime(started).TotalMilliseconds<SearchBudgetMs);probe++)
        {
            double scale=scales[scaleIndex];
            var offset=target==ScanTarget.Bank
                ? new Rectangle(ReferenceBounds.X-ReferenceTitleAnchor.X,ReferenceBounds.Y-ReferenceTitleAnchor.Y,ReferenceBounds.Width,ReferenceBounds.Height)
                : CharacterLayout.PanelOffset;
            // Include both character heading positions (with and without scrollbar).
            int left=-(int)Math.Ceiling((offset.Left+(target==ScanTarget.Bank ? 0 : 16))*scale);
            int top=-(int)Math.Ceiling(offset.Top*scale);
            int right=frame.Width-(int)Math.Floor(offset.Right*scale);
            int bottom=frame.Height-(int)Math.Floor(offset.Bottom*scale);
            int width=right-left+1,height=bottom-top+1;
            if(width>0 && height>0)
            {
                var pattern=vision.LayoutPatternSize(target,scale);
                var area=new Rectangle(left+x,top+y,Math.Min(TileWidth,width-x)+pattern.Width-1,Math.Min(TileHeight,height-y)+pattern.Height-1);
                found=vision.FindLayoutIn(frame,target,scale,area);
                x+=TileWidth;
                if(x>=width){x=0;y+=TileHeight;}
            }
            if(width<=0 || height<=0 || y>=height){scaleIndex=(scaleIndex+1)%scales.Length;x=0;y=0;}
        }
        if(found is not null)
        {
            if(target==ScanTarget.Bank)bank=found;else character=found;
            scaleIndex=0;x=0;y=0;
        }
        return found;
    }
}
