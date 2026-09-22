using FsBank.Scanner.Game;
using FsBank.Scanner.Imaging;

using static FsBank.Scanner.Game.BankGeometry;

namespace FsBank.Scanner.Scanning.Manual;

// One small fallback tile per fresh frame. An absent panel never monopolizes
// the capture loop; its usual position is rechecked before every fallback tile.
internal sealed class ManualPanelSearch(Vision vision)
{
    private Size frameSize;
    private double[] scales=[];
    private int scaleIndex,x,y;
    private const int TileWidth=192,TileHeight=48;
    public int TilesSearched { get; private set; }
    public EquippedLayout? Find(Pixels frame)
    {
        var fast=vision.FindEquippedFast(frame,fallback:false);
        if(fast is not null)return fast;
        if(frameSize!=new Size(frame.Width,frame.Height))
        {
            frameSize=new(frame.Width,frame.Height);
            double basis=frame.Height/(double)GameConstants.ReferenceHeight;
            scales=new[] {basis,NativeScale}.Concat(RelativeScales.ToArray().Skip(1).Select(s=>basis*s)).Distinct().ToArray();
            scaleIndex=0;x=0;y=0;
        }
        double scale=scales[scaleIndex];
        // Only anchors for which the complete character panel fits on screen.
        var offset=CharacterLayout.PanelOffset;
        int left=-(int)Math.Round(offset.Left*scale),top=-(int)Math.Round(offset.Top*scale);
        int right=frame.Width-(int)Math.Round(offset.Right*scale),bottom=frame.Height-(int)Math.Round(offset.Bottom*scale);
        int width=right-left+1,height=bottom-top+1;
        if(width<=0 || height<=0){AdvanceScale();return null;}
        var pattern=vision.EquippedPatternSize(scale);
        var area=new Rectangle(left+x,top+y,Math.Min(TileWidth,width-x)+pattern.Width-1,Math.Min(TileHeight,height-y)+pattern.Height-1);
        var found=vision.FindEquippedIn(frame,scale,area);
        TilesSearched++;
        x+=TileWidth;
        if(x>=width){x=0;y+=TileHeight;}
        if(y>=height)AdvanceScale();
        return found;
    }
    private void AdvanceScale(){scaleIndex=(scaleIndex+1)%scales.Length;x=0;y=0;}
}
