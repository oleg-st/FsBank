namespace FsBank.Scanner.Imaging;

// Shared border evidence for capture bounds and OCR of older saved crops.
internal static class TooltipSideBounds
{
    internal static (int? Left,int? Right) Find(Pixels pixels)
    {
        if(pixels.Height<120 || pixels.Width<40)return (null,null);
        // Search the outer portions of the image, not a fixed inset from its
        // edge: an over-wide capture can contain several bank grid strokes.
        // The tooltip outline remains continuous through almost the whole body;
        // individual glyphs and horizontal cell separators have vertical gaps.
        bool Outline(int x)
        {
            int count=0,total=0;
            for(int y=40;y<pixels.Height-25;y+=2)
            {
                if(OutlineAt(pixels,x,y))count++;
                total++;
            }
            return total>=25 && count>total*.9;
        }
        int? left=null,right=null;
        for(int x=2;x<=pixels.Width/4;x++)if(Outline(x))left=x;
        for(int x=pixels.Width-3;x>=pixels.Width*3/4;x--)if(Outline(x))right=x;
        return (left,right);
    }
    internal static bool StartsNear(Pixels pixels,int outline,double scale)
    {
        int Px(int value)=>(int)Math.Ceiling(value*scale);
        int beforeStart=Math.Max(0,outline-Px(12)),beforeEnd=outline-Px(4);
        int afterStart=outline+Px(8),afterEnd=outline+Px(32);
        if(beforeEnd-beforeStart<Px(4) || afterEnd>pixels.Height)return false;
        var sides=Find(pixels);
        if(sides.Left is not { } left || sides.Right is not { } right)return false;
        // Both vertical sides begin at the upper corners. A divider below the
        // title has these same sides continuing above it, so it cannot pass.
        bool Pair(int y)=>OutlineAt(pixels,left,y) && OutlineAt(pixels,right,y);
        int before=0,after=0;
        for(int y=beforeStart;y<beforeEnd;y++)if(Pair(y))before++;
        for(int y=afterStart;y<afterEnd;y++)if(Pair(y))after++;
        return before<=(beforeEnd-beforeStart)*.25 && after>=(afterEnd-afterStart)*.9;
    }
    private static bool OutlineAt(Pixels pixels,int x,int y)
    {
        int bright=pixels.Bright(x,y);
        return bright>45 && bright-Math.Min(pixels.Bright(x-2,y),pixels.Bright(x+2,y))>15;
    }
}
