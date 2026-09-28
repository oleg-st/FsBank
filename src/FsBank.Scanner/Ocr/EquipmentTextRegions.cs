using FsBank.Scanner.Imaging;

namespace FsBank.Scanner.Ocr;

internal static class EquipmentTextRegions
{
    internal static (Rectangle Slot,Rectangle Level)? Find(Pixels pixels,Rectangle line)
    {
        // Equipment metadata has two separated columns, each starting with an
        // icon. Find their empty gutters in pixels; all size limits derive from
        // the detected row, so screen position and font scale are irrelevant.
        var ink=new bool[line.Width];
        for(int x=0;x<line.Width;x++)for(int y=line.Top;y<line.Bottom;y++)
            if(pixels.Bright(line.Left+x,y)>=95){ink[x]=true;break;}
        var gaps=new List<(int Left,int Right)>();
        int start=-1;
        for(int x=0;x<=ink.Length;x++)
        {
            if(x<ink.Length && !ink[x]){if(start<0)start=x;}
            else if(start>=0){if(start>0 && x<ink.Length)gaps.Add((start,x));start=-1;}
        }
        if(gaps.Count==0)return null;
        var gutter=gaps.MaxBy(g=>g.Right-g.Left);
        if(gutter.Right-gutter.Left<line.Width*.35)return null;
        Rectangle? TextAfterIcon(int left,int right)
        {
            while(left<right && !ink[left])left++;
            int minGap=(int)Math.Ceiling(line.Height*.15);
            foreach(var gap in gaps)
            {
                if(gap.Left<=left || gap.Right>=right || gap.Right-gap.Left<minGap)continue;
                // The leading graphic fits within approximately one line height.
                // Never chop an arbitrary word to make it match a known slot.
                if(gap.Left-left>line.Height*1.25 || right-gap.Right<line.Height*.5)return null;
                return Rectangle.FromLTRB(line.Left+gap.Right-1,line.Top,line.Left+right+1,line.Bottom);
            }
            return null;
        }
        var slot=TextAfterIcon(0,gutter.Left);var level=TextAfterIcon(gutter.Right,ink.Length-1);
        if(slot is null || level is null || slot.Value.Right>pixels.Width*.5 || level.Value.Left<pixels.Width*.65)return null;
        return (slot.Value,level.Value);
    }
}
