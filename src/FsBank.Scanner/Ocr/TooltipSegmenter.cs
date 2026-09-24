using FsBank.Scanner.Imaging;

namespace FsBank.Scanner.Ocr;

internal sealed record TextBand(int Index, Rectangle Box, string Color);

internal static class TooltipSegmenter
{
    // Inspect only the header. A cropped title leaves body text as the first band.
    // Title colours differ from the neutral material/equipment text below it.
    internal static bool HasCompleteTitle(Pixels pixels, double scale=1)
    {
        var header=pixels.Crop(new Rectangle(0,0,pixels.Width,Math.Min(pixels.Height,(int)Math.Ceiling(85*scale))));
        // The first surviving line can be equipment metadata or the second title
        // line. Require evidence of the top outline before accepting either.
        bool topOutline=false;
        for(int y=0;y<Math.Min(header.Height,(int)Math.Ceiling(9*scale));y++)
        {
            int colored=0;
            for(int x=14;x<header.Width-14;x++)
            {
                int i=header.Offset(x,y),max=Math.Max(header.Data[i],Math.Max(header.Data[i+1],header.Data[i+2]));
                int min=Math.Min(header.Data[i],Math.Min(header.Data[i+1],header.Data[i+2]));
                if(max>70 && max-min>45)colored++;
            }
            if(colored>(header.Width-28)*.1){topOutline=true;break;}
        }
        if(!topOutline)return false;
        var upperBand=Find(header,startY:0).FirstOrDefault(b=>b.Box.Height>=11*scale && b.Box.Width>=40*scale);
        if(upperBand is not null && upperBand.Box.Top<5*scale && upperBand.Box.Width<=header.Width*.85)return false;
        // Keep ornament/background pixels above the text margin out of the
        // title bands; they can otherwise merge with a complete first line.
        var bands=Find(header);
        foreach(var band in bands)
        {
            if(band.Box.Height<11*scale || band.Box.Width<40*scale)continue;
            return band.Box.Top>=9*scale && band.Box.Top<=28*scale && band.Color!="white";
        }
        return false;
    }
    // Detailed item tooltips need separate heading, metadata, stat and footer
    // lines. Fewer than ten bands is a conservative rejection of a collapsed
    // layout, not proof that a layout with more bands is correct.
    internal static bool HasReadableLayout(Pixels pixels) => Find(pixels).Count>=10;
    // Current capture profile: discard the ornamental border, not the text margin.
    public static List<TextBand> Find(Pixels pixels, int startY=10, bool excludeHeaderDecoration=false)
    {
        List<TextBand> result = [];
        int left = 14, right = pixels.Width - 14;
        if (right <= left || pixels.Height < 20) return result;
        var decoration=excludeHeaderDecoration ? HeaderDecoration(pixels,left,right) : null;
        bool Ink(int x, int y) => pixels.Bright(x, y) >= 95
            && (decoration is null || y>=decoration.Length/pixels.Width || !decoration[y*pixels.Width+x]);
        bool Row(int y)
        {
            int count = 0, longest = 0, run = 0;
            for (int x = left; x < right; x++)
            {
                if (Ink(x, y)) { count++; longest = Math.Max(longest, ++run); } else run = 0;
            }
            return count >= 3 && longest < (right - left) * .7;
        }
        int start = -1, last = -1;
        for (int y = startY; y <= pixels.Height - 8; y++)
        {
            bool ink = y < pixels.Height - 8 && Row(y);
            if (ink) { if (start < 0) start = y; last = y; }
            if (start < 0 || (ink || y - last <= 2) && y < pixels.Height - 8) continue;
            if (last - start >= 4)
            {
                int min = right, max = left, cyan = 0, white = 0, other = 0;
                for (int yy = start; yy <= last; yy++) for (int x = left; x < right; x++)
                {
                    if (!Ink(x, yy)) continue;
                    min = Math.Min(min, x); max = Math.Max(max, x);
                    int i = pixels.Offset(x, yy), b = pixels.Data[i], g = pixels.Data[i+1], r = pixels.Data[i+2];
                    if (Math.Min(b,g)-r > 35 && Math.Min(b,g)>100) cyan++;
                    else if (Math.Max(r,Math.Max(g,b))-Math.Min(r,Math.Min(g,b))<45) white++;
                    else other++;
                }
                if (max - min >= 3)
                {
                    int total = cyan + white + other;
                    string color = cyan > total * .65 ? "cyan" : white > total * .65 ? "white" : "mixed";
                    result.Add(new(result.Count, Rectangle.FromLTRB(Math.Max(left,min-1), Math.Max(0,start-1), Math.Min(right,max+2), Math.Min(pixels.Height,last+2)), color));
                }
            }
            start = last = -1;
        }
        return result;
    }
    private static bool[] HeaderDecoration(Pixels pixels,int left,int right)
    {
        // Border strokes are long horizontal components. Antialiasing may split
        // the outline into several pieces, so measure their combined coverage
        // on the same rows. Glyph components are much less elongated.
        int height=Math.Min(pixels.Height,85),width=pixels.Width;
        var visited=new bool[width*height];var decoration=new bool[visited.Length];
        var component=new List<int>();var queue=new Queue<int>();
        var strokes=new List<(int Top,int Bottom,int[] Pixels)>();
        for(int y=0;y<height;y++)for(int x=left;x<right;x++)
        {
            int index=y*width+x;
            if(visited[index] || pixels.Bright(x,y)<95)continue;
            component.Clear();queue.Enqueue(index);visited[index]=true;
            int minX=x,maxX=x,minY=y,maxY=y;
            while(queue.TryDequeue(out int point))
            {
                component.Add(point);int px=point%width,py=point/width;
                minX=Math.Min(minX,px);maxX=Math.Max(maxX,px);
                minY=Math.Min(minY,py);maxY=Math.Max(maxY,py);
                for(int yy=Math.Max(0,py-1);yy<=Math.Min(height-1,py+1);yy++)
                for(int xx=Math.Max(left,px-1);xx<=Math.Min(right-1,px+1);xx++)
                {
                    int next=yy*width+xx;
                    if(visited[next] || pixels.Bright(xx,yy)<95)continue;
                    visited[next]=true;queue.Enqueue(next);
                }
            }
            if(maxX-minX+1>=4*(maxY-minY+1))strokes.Add((minY,maxY,component.ToArray()));
        }
        var ordered=strokes.OrderBy(s=>s.Top).ToArray();
        for(int first=0;first<ordered.Length;)
        {
            int end=first+1,bottom=ordered[first].Bottom;
            while(end<ordered.Length && ordered[end].Top<=bottom)
            { bottom=Math.Max(bottom,ordered[end].Bottom);end++; }
            var columns=new HashSet<int>();
            for(int i=first;i<end;i++)foreach(int point in ordered[i].Pixels)columns.Add(point%width);
            if(columns.Count>=(right-left)*.7)
                for(int i=first;i<end;i++)foreach(int point in ordered[i].Pixels)decoration[point]=true;
            first=end;
        }
        return decoration;
    }
}
