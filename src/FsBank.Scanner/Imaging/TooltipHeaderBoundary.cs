namespace FsBank.Scanner.Imaging;

// Shared by live capture and recognition of previously saved images.
internal static class TooltipHeaderBoundary
{
    internal sealed record Match(int Outline,int TextStart);
    internal static Match? Find(Pixels pixels,int left,int right,double scale=1)
    {
        // An ornate top stroke may sit below a strip of the world background.
        // Require a broad, sharp saturated stroke followed by the dark title
        // margin. Saturation alone also matches the glow behind green titles.
        int Px(int value)=>(int)Math.Ceiling(value*scale);
        for(int y=0;y<Math.Min(Px(40),pixels.Height-Px(12));y++)
        {
            int colored=0;double blue=0,green=0,red=0;
            for(int x=left;x<right;x++)
            {
                int i=pixels.Offset(x,y),max=Math.Max(pixels.Data[i],Math.Max(pixels.Data[i+1],pixels.Data[i+2]));
                int min=Math.Min(pixels.Data[i],Math.Min(pixels.Data[i+1],pixels.Data[i+2]));
                if(max>60 && max-min>45 && max-min>max*.5
                    && max-Math.Min(pixels.Bright(x,Math.Max(0,y-Px(2))),pixels.Bright(x,y+Px(2)))>23)
                {
                    colored++;blue+=pixels.Data[i]/(double)max;
                    green+=pixels.Data[i+1]/(double)max;red+=pixels.Data[i+2]/(double)max;
                }
            }
            if(colored<(right-left)*.6)continue;
            // World scenery can share the title's hue. A bright component
            // running from the crop's top edge down to this outline is stronger
            // evidence: real title glyphs are separated from the upper frame by
            // a dark margin. Stop above the outline so it cannot join components.
            // The connection must extend beyond the seed rows, even when the
            // external background strip is shorter than a whole title line.
            if(y>Px(3)+Px(4) && BackgroundReachesOutline(pixels,y,left,right,Px(3),Px(4)))
            {
                bool margin=true;
                for(int yy=y+Px(4);yy<=y+Px(8) && margin;yy++)
                {
                    int dark=0;
                    for(int x=left;x<right;x++)if(pixels.Bright(x,yy)<95)dark++;
                    margin=dark>(right-left)*.95;
                }
                if(margin)return new(y,y+Px(4));
            }
            bool darkMargin=true;
            for(int yy=y+Px(8);yy<=y+Px(12) && darkMargin;yy++)
            {
                int dark=0;
                for(int x=left;x<right;x++)if(pixels.Bright(x,yy)<95)dark++;
                darkMargin=dark>(right-left)*.95;
            }
            if(!darkMargin)continue;
            // A lower title divider has the same shape. Never discard the title
            // above it: require the preceding bright pixels to be predominantly
            // a different hue from the frame and its title, as world scenery is.
            blue/=colored;green/=colored;red/=colored;
            int visible=0,background=0;
            for(int yy=Math.Max(0,y-Px(20));yy<y;yy++)for(int x=left;x<right;x++)
            {
                int i=pixels.Offset(x,yy),max=pixels.Bright(x,yy);
                if(max<95)continue;
                visible++;
                if(Math.Abs(pixels.Data[i]/(double)max-blue)>.35
                    || Math.Abs(pixels.Data[i+1]/(double)max-green)>.35
                    || Math.Abs(pixels.Data[i+2]/(double)max-red)>.35)background++;
            }
            if(visible>right-left && background>visible*.75)return new(y,y+Px(8));
        }
        return null;
    }
    internal static bool[] Foreground(Pixels pixels,int height)
    {
        // Smooth title glow may be brighter than the global ink threshold.
        // Glyphs and outline strokes contrast with their local background;
        // retain those edges before finding connected decorative components.
        int width=pixels.Width;
        int radius=Math.Max(1,(int)Math.Round(2d*width/FsBank.Scanner.Game.TooltipGeometry.TypicalWidthPx));
        var foreground=new bool[width*height];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            int bright=pixels.Bright(x,y);
            if(bright<95)continue;
            int background=bright;
            for(int yy=Math.Max(0,y-radius);yy<=Math.Min(pixels.Height-1,y+radius);yy++)
            for(int xx=Math.Max(0,x-radius);xx<=Math.Min(width-1,x+radius);xx++)
                background=Math.Min(background,pixels.Bright(xx,yy));
            foreground[y*width+x]=bright-background>=DetectionConstants.EdgeContrast;
        }
        return foreground;
    }
    private static bool BackgroundReachesOutline(Pixels pixels,int outline,int left,int right,int topRows,int reachPadding)
    {
        var foreground=Foreground(pixels,outline);
        var visited=new bool[pixels.Width*outline];var queue=new Queue<int>();
        for(int y=0;y<topRows;y++)for(int x=left;x<right;x++)
            if(foreground[y*pixels.Width+x]){int index=y*pixels.Width+x;visited[index]=true;queue.Enqueue(index);}
        while(queue.TryDequeue(out int point))
        {
            int x=point%pixels.Width,y=point/pixels.Width;
            if(y>=outline-reachPadding)return true;
            for(int yy=Math.Max(0,y-1);yy<=Math.Min(outline-1,y+1);yy++)
            for(int xx=Math.Max(left,x-1);xx<=Math.Min(right-1,x+1);xx++)
            {
                int next=yy*pixels.Width+xx;
                if(visited[next] || !foreground[next])continue;
                visited[next]=true;queue.Enqueue(next);
            }
        }
        return false;
    }
}
