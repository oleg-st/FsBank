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
                int bright=pixels.Bright(x,y);
                if(bright>45 && bright-Math.Min(pixels.Bright(x-2,y),pixels.Bright(x+2,y))>15)count++;
                total++;
            }
            return total>=25 && count>total*.9;
        }
        int? left=null,right=null;
        for(int x=2;x<=pixels.Width/4;x++)if(Outline(x))left=x;
        for(int x=pixels.Width-3;x>=pixels.Width*3/4;x--)if(Outline(x))right=x;
        return (left,right);
    }
}
