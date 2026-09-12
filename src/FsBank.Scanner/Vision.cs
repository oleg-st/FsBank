using static FsBank.Scanner.BankGeometry;
using static FsBank.Scanner.TooltipGeometry;
using static FsBank.Scanner.DetectionConstants;

namespace FsBank.Scanner;

internal sealed class Pattern
{
    private readonly (int X, int Y, int B, int G, int R)[] points;
    public int Width { get; }
    public int Height { get; }
    public Pattern(Pixels source, double scale)
    {
        Width = (int)Math.Round(source.Width*scale); Height = (int)Math.Round(source.Height*scale);
        var all = new List<(int X,int Y,int B,int G,int R)>();
        for (int y = PatternSampleInsetPx; y < source.Height-PatternSampleInsetPx; y++)
            for (int x = PatternSampleInsetPx; x < source.Width-PatternSampleInsetPx; x++)
            {
                int i = source.Offset(x,y);
                all.Add(((int)Math.Round(x*scale),(int)Math.Round(y*scale), source.Data[i],source.Data[i+1],source.Data[i+2]));
            }
        var random = new Random(PatternSampleSeed);
        points = all.Where(p => Math.Max(p.B,Math.Max(p.G,p.R)) > PatternBrightThreshold).OrderBy(_ => random.Next()).Take(PatternBrightSamples)
            .Concat(all.Where(p => Math.Max(p.B,Math.Max(p.G,p.R)) < PatternDarkThreshold).OrderBy(_ => random.Next()).Take(PatternDarkSamples)).ToArray();
    }
    private double Score(Pixels frame, int x, int y, double limit)
    {
        long sum = 0;
        for (int n = 0; n < points.Length; n++)
        {
            var p = points[n]; int i = frame.Offset(x+p.X,y+p.Y);
            sum += Math.Abs(frame.Data[i]-p.B)+Math.Abs(frame.Data[i+1]-p.G)+Math.Abs(frame.Data[i+2]-p.R);
            if (n == PatternEarlySamples-1 && sum > PatternEarlySamples*Pixels.ColorChannels*PatternEarlyMeanError) return byte.MaxValue;
            if (sum > limit*points.Length*Pixels.ColorChannels) return byte.MaxValue;
        }
        return (double)sum/(points.Length*Pixels.ColorChannels);
    }
    public (Point Point, double Error)? Find(Pixels frame, Rectangle area, double limit = PatternMaxError)
    {
        area.Intersect(frame.Bounds);
        Point best = default; double error = limit;
        bool found = false;
        for (int y = area.Top; y <= area.Bottom-Height; y++)
            for (int x = area.Left; x <= area.Right-Width; x++)
            {
                double score = Score(frame,x,y,error);
                if (score < error) { best = new(x,y); error=score; found=true; if (error<PatternImmediateMatchError) return (best,error); }
            }
        return found ? (best,error) : null;
    }
}

internal sealed record Tooltip(Rectangle Bounds, Rectangle Footer);

internal sealed class Vision
{
    private readonly Pixels title = Pixels.Load(Path.Combine(AppContext.BaseDirectory,"Assets",TitleAsset));
    private readonly Pixels footer = Pixels.Load(Path.Combine(AppContext.BaseDirectory,"Assets",FooterAsset));
    private readonly Dictionary<double, Pattern> titles = new(), footers = new();
    private Pattern Title(double scale) => titles.TryGetValue(scale,out var p) ? p : titles[scale]=new(title,scale);
    private Pattern Footer(double scale) => footers.TryGetValue(scale,out var p) ? p : footers[scale]=new(
        footer.Crop(new Rectangle(0,FooterMatchTopInsetPx,footer.Width,footer.Height-FooterMatchTopInsetPx)),scale);
    private readonly Pixels equippedPower = Pixels.Load(Path.Combine(AppContext.BaseDirectory,"Assets","equipped-power.png"));
    private readonly Pixels equippedStats = Pixels.Load(Path.Combine(AppContext.BaseDirectory,"Assets","equipped-stats.png"));
    private readonly Dictionary<double, Pattern> powers = new(), stats = new();
    private Pattern Power(double scale) => powers.TryGetValue(scale,out var p) ? p : powers[scale]=new(equippedPower,scale);
    private Pattern Stats(double scale) => stats.TryGetValue(scale,out var p) ? p : stats[scale]=new(equippedStats,scale);
    public ScanLayout? FindLayout(Pixels frame,ScanTarget target) => target switch
    {
        ScanTarget.Bank => FindBank(frame),
        ScanTarget.Equipped => FindEquipped(frame),
        ScanTarget.Inventory => FindCharacter(frame,ScanTarget.Inventory),
        _ => throw new ArgumentOutOfRangeException(nameof(target))
    };
    public EquippedLayout? FindEquipped(Pixels frame) => (EquippedLayout?)FindCharacter(frame,ScanTarget.Equipped);
    public EquippedLayout? FindEquippedFast(Pixels frame,bool fallback=true)
    {
        // Common position is a search hint, never an assumed layout. Validate
        // both POWER and STATS, then retain the general detector for moved panels.
        foreach(double scale in new[] {frame.Height/(double)GameConstants.ReferenceHeight,NativeScale}.Distinct())
        {
            var pattern=Power(scale);
            var area=new Rectangle((int)Math.Round(890*scale),(int)Math.Round(414*scale),pattern.Width,pattern.Height);
            int padding=(int)Math.Ceiling(32*scale);area.Inflate(padding,padding);
            var layout=FindEquippedIn(frame,scale,area);
            if(layout is not null)return layout;
        }
        return fallback ? FindEquipped(frame) : null;
    }
    internal Size EquippedPatternSize(double scale) => new(Power(scale).Width,Power(scale).Height);
    internal EquippedLayout? FindEquippedIn(Pixels frame,double scale,Rectangle area)
    {
        var found=Power(scale).Find(frame,area);
        if(found is null)return null;
        var layout=new EquippedLayout(Rectangle.Empty,found.Value.Point,scale);
        layout=layout with {Bounds=layout.Relative(CharacterLayout.PanelOffset)};
        return frame.Bounds.Contains(layout.Bounds) && frame.Bounds.Contains(layout.Grid) && PanelStillOpen(frame,layout) ? layout : null;
    }
    private CharacterLayout? FindCharacter(Pixels frame,ScanTarget target)
    {
        double basis = frame.Height/(double)GameConstants.ReferenceHeight;
        foreach(double scale in new[] {basis,NativeScale}.Concat(RelativeScales.ToArray().Skip(1).Select(s=>basis*s)).Distinct())
        {
            var found=Power(scale).Find(frame,frame.Bounds);
            if(found is null)continue;
            CharacterLayout layout=target==ScanTarget.Equipped ? new EquippedLayout(Rectangle.Empty,found.Value.Point,scale) : new InventoryLayout(Rectangle.Empty,found.Value.Point,scale);
            layout=layout with {Bounds=layout.Relative(CharacterLayout.PanelOffset)};
            if(frame.Bounds.Contains(layout.Bounds) && frame.Bounds.Contains(layout.Grid) && PanelStillOpen(frame,layout))return layout;
        }
        return null;
    }
    public BankLayout? FindBank(Pixels frame)
    {
        double basis = frame.Height/(double)GameConstants.ReferenceHeight;
        foreach (double scale in new[] {basis,NativeScale}.Concat(RelativeScales.ToArray().Skip(1).Select(s=>basis*s)).Distinct())
        {
            var found = Title(scale).Find(frame, frame.Bounds);
            if (found is null) continue;
            var point = found.Value.Point;
            var bank = new BankLayout(new(point.X+(int)Math.Round((ReferenceBounds.X-ReferenceTitleAnchor.X)*scale),point.Y+(int)Math.Round((ReferenceBounds.Y-ReferenceTitleAnchor.Y)*scale),
                (int)Math.Round(ReferenceBounds.Width*scale),(int)Math.Round(ReferenceBounds.Height*scale)), point,scale);
            if (!frame.Bounds.Contains(bank.Bounds) || !frame.Bounds.Contains(bank.Cell(Rows-1,Columns-1))) continue;
            // Independent geometry check: repeated vertical tab separators must exist.
            int valid=0;
            for (int col=0; col<TabCount; col++)
            {
                int x=bank.Tabs.Left+(int)Math.Round((TabBorderProbe.X+col*TabPitchPx)*scale);
                int y=bank.Tabs.Top+(int)Math.Round(TabBorderProbe.Y*scale);
                if (frame.Bright(x,y)>TabBorderBrightness) valid++;
            }
            if(valid>=MinimumTabBorders) return bank;
        }
        return null;
    }
    public bool PanelStillOpen(Pixels frame,ScanLayout layout)
    {
        if(layout is BankLayout bank)return BankStillOpen(frame,bank);
        if(layout is not CharacterLayout)return false;
        int padding=(int)Math.Ceiling(TitleTrackingPaddingPx*layout.Scale);
        var power=layout.Relative(new(0,0,equippedPower.Width,equippedPower.Height));power.Inflate(padding,padding);
        var statsArea=layout.VerificationArea;statsArea.Inflate(padding,padding);
        return Power(layout.Scale).Find(frame,power) is not null && Stats(layout.Scale).Find(frame,statsArea) is not null;
    }
    public bool BankStillOpen(Pixels frame,BankLayout bank)
    {
        int padding=(int)Math.Ceiling(TitleTrackingPaddingPx*bank.Scale);
        var area=new Rectangle(bank.Anchor, new Size((int)Math.Round(title.Width*bank.Scale),(int)Math.Round(title.Height*bank.Scale)));
        area.Inflate(padding,padding);
        return Title(bank.Scale).Find(frame,area) is not null;
    }
    public static bool Occupied(Pixels frame,Rectangle cell)
    {
        int colorful=0,total=0;
        for(int y=cell.Top+OccupancyInsetPx;y<cell.Bottom-OccupancyInsetPx;y+=OccupancySampleStep)
            for(int x=cell.Left+OccupancyInsetPx;x<cell.Right-OccupancyInsetPx;x+=OccupancySampleStep)
            {
                int i=frame.Offset(x,y); int max=Math.Max(frame.Data[i],Math.Max(frame.Data[i+1],frame.Data[i+2]));
                int min=Math.Min(frame.Data[i],Math.Min(frame.Data[i+1],frame.Data[i+2]));
                if(max>OccupancyBrightness && max-min>OccupancyContrast) colorful++;
                total++;
            }
        return colorful>Math.Max(OccupancyMinimumPixels,total*OccupancyMinimumFraction); // Only clearly dark/desaturated cells are skipped.
    }
    public Tooltip? FindTooltip(Pixels frame,double scale,Rectangle? search=null,bool repairHeader=true)
    {
        var match=Footer(scale).Find(frame, search ?? frame.Bounds, FooterMaxError);
        if(match is null) return null;
        var at=match.Value.Point;
        at.Y-=(int)Math.Round(FooterMatchTopInsetPx*scale);
        var footerRect=new Rectangle(at,new Size((int)Math.Round(footer.Width*scale),(int)Math.Round(footer.Height*scale)));
        int center=footerRect.Left+footerRect.Width/2;
        int baseY=at.Y-(int)Math.Round(FooterToBodyProbePx*scale);
        if(baseY<MinimumBodyProbeY) return null;
        // Find the two vertical panel outlines above the fixed footer.
        int left=FindEdge(frame,center-(int)(EdgeDistanceMaxPx*scale),center-(int)(EdgeDistanceMinPx*scale),baseY,scale);
        int right=FindEdge(frame,center+(int)(EdgeDistanceMinPx*scale),center+(int)(EdgeDistanceMaxPx*scale),baseY,scale);
        if(left<0 || right<0) return null;
        int gap=0,top=baseY, maxGap=Math.Max(MinimumVerticalGapPx,(int)(VerticalGapPx*scale));
        for(int y=baseY;y>=0;y--)
        {
            if(Edge(frame,left,y)||Edge(frame,right,y)) {top=y;gap=0;}
            else if(++gap>maxGap) break;
        }
        // The coloured top outline separates the tooltip from item frames behind it.
        // Following vertical edges alone can accidentally continue into the bank grid.
        int? headerBottom=null;
        for(int y=baseY;y>=Math.Max(0,top-HeaderSearchExtraPx*scale);y--)
        {
            int count=0,run=0,longest=0;
            for(int x=left+HeaderHorizontalInsetPx;x<right-HeaderHorizontalInsetPx;x++)
            {
                int i=frame.Offset(x,y);int max=Math.Max(frame.Data[i],Math.Max(frame.Data[i+1],frame.Data[i+2]));
                int min=Math.Min(frame.Data[i],Math.Min(frame.Data[i+1],frame.Data[i+2]));
                if(max>HeaderBrightness && max-min>HeaderContrast){count++;run++;longest=Math.Max(longest,run);}else run=0;
            }
            if(count>(right-left-2*HeaderHorizontalInsetPx)*HeaderColoredFraction && longest>HeaderMinRunPx*scale)
            {
                if(headerBottom is null) headerBottom=y;
                else if(headerBottom.Value-y>HeaderMinHeightPx*scale){top=y;break;}
            }
        }
        int bottom=footerRect.Bottom+(int)Math.Round(BottomPaddingPx*scale);
        // Both coloured side strokes belong to the title box. Their continuous
        // upper segment is more reliable than inventory edges behind the tooltip.
        int colorTop=0,colorCount=0,colorGap=0;
        for(int y=baseY;y>=0;y--)
        {
            if(ColoredEdge(frame,left,y) && ColoredEdge(frame,right,y))
            {colorTop=y;colorCount++;colorGap=0;}
            else if(colorCount>0 && ++colorGap>VerticalGapPx*scale)
            {
                if(colorCount>=MinimumColoredSidePixels*scale){top=Math.Max(top,colorTop);break;}
                colorCount=0;colorGap=0;
            }
        }
        var rect=Rectangle.FromLTRB(left-(int)Math.Ceiling(CropLeftPaddingPx*scale),Math.Max(0,top-(int)Math.Ceiling(CropTopPaddingPx*scale)),
            right+(int)Math.Ceiling(CropRightPaddingPx*scale),Math.Min(frame.Height,bottom));
        if(rect.Height<MinimumHeightPx*scale || !frame.Bounds.Contains(rect)) return null;
        if(repairHeader && !TooltipSegmenter.HasCompleteTitle(frame.Crop(rect),scale))
        {
            // Recover from interrupted side edges without changing already valid crops.
            // Saturation rejects sky/background; a shorter run admits the ornate red border.
            int? lowerBorder=null;
            for(int y=baseY;y>=0;y--)
            {
                int count=0,run=0,longest=0;
                for(int x=left+HeaderHorizontalInsetPx;x<right-HeaderHorizontalInsetPx;x++)
                {
                    int i=frame.Offset(x,y),max=Math.Max(frame.Data[i],Math.Max(frame.Data[i+1],frame.Data[i+2]));
                    int min=Math.Min(frame.Data[i],Math.Min(frame.Data[i+1],frame.Data[i+2]));
                    if(max>RecoveryHeaderBrightness && max-min>HeaderContrast && max-min>max*HeaderMinimumSaturation)
                    {count++;run++;longest=Math.Max(longest,run);}else run=0;
                }
                if(count<=(right-left-2*HeaderHorizontalInsetPx)*HeaderColoredFraction || longest<=RecoveryHeaderMinRunPx*scale)continue;
                if(lowerBorder is null){lowerBorder=y;continue;}
                if(lowerBorder.Value-y<=HeaderMinHeightPx*scale)continue;
                var recovered=Rectangle.FromLTRB(rect.Left,Math.Max(0,y-(int)Math.Ceiling(CropTopPaddingPx*scale)),rect.Right,rect.Bottom);
                if(TooltipSegmenter.HasCompleteTitle(frame.Crop(recovered),scale))rect=recovered;
                break;
            }
        }
        return new(rect,footerRect);
    }
    public Tooltip? FindTooltipNear(Pixels frame,double scale,Point hover)
    {
        foreach(var area in FooterSearchAreas(frame,scale,hover))
        {
            var found=FindTooltip(frame,scale,area);
            if(found is not null)return found;
        }
        return null;
    }
    public bool FooterPresentNear(Pixels frame,double scale,Point hover)
    {
        foreach(var area in FooterSearchAreas(frame,scale,hover))
            if(Footer(scale).Find(frame,area,FooterMaxError) is not null)return true;
        return false;
    }
    internal bool TooltipNearCursor(Pixels frame,double scale,Point cursor,Tooltip tooltip) =>
        FooterSearchAreas(frame,scale,cursor).Any(area=>tooltip.Footer.Left>=area.Left && tooltip.Footer.Right<=area.Right);
    private IEnumerable<Rectangle> FooterSearchAreas(Pixels frame,double scale,Point hover)
    {
        // Game places the tooltip beside the cursor. Only X is predicted: its
        // height (and hence footer Y) varies by item and may be clamped to screen.
        int footerWidth=Footer(scale).Width;
        int margin=(int)Math.Ceiling(NearSearchPaddingPx*scale);
        int preferred=hover.X+(int)Math.Round((CursorGapPx+FooterLeftInsetPx)*scale);
        var candidates=new List<int>{preferred};
        if(preferred+footerWidth+margin>=frame.Width)
        {
            candidates.Add(frame.Width-(int)Math.Round((TypicalWidthPx+ScreenEdgeMarginPx-FooterLeftInsetPx)*scale));
            candidates.Add(hover.X-(int)Math.Round((CursorGapPx+TypicalWidthPx-FooterLeftInsetPx)*scale));
        }
        foreach(int x in candidates)
            yield return new Rectangle(x-margin,0,footerWidth+2*margin,frame.Height);
    }
    public Tooltip? TrackTooltip(Pixels frame,double scale,Tooltip previous)
    {
        var search=previous.Footer;search.Inflate((int)Math.Ceiling(FooterTrackingPaddingPx*scale),(int)Math.Ceiling(FooterTrackingPaddingPx*scale));
        return FindTooltip(frame,scale,search);
    }
    public bool FooterPresent(Pixels frame,double scale,Rectangle footerArea)
    {
        footerArea.Inflate(FooterTrackingPaddingPx,FooterTrackingPaddingPx);
        return Footer(scale).Find(frame,footerArea,FooterMaxError) is not null;
    }
    private static int FindEdge(Pixels frame,int start,int end,int y,double scale)
    {
        int best=-1,bestCount=0;
        for(int x=Math.Max(EdgeScanInsetPx,start);x<Math.Min(frame.Width-EdgeScanInsetPx,end);x++)
        {
            int count=0;
            for(int yy=y;yy>=Math.Max(0,y-(int)(EdgeSampleHeightPx*scale));yy--) if(Edge(frame,x,yy)) count++;
            if(count>bestCount){bestCount=count;best=x;}
        }
        return bestCount>MinimumEdgePixels*scale ? best : -1;
    }
    private static bool Edge(Pixels f,int x,int y)
    {
        int v=f.Bright(x,y);
        return v>EdgeBrightness && v-Math.Min(f.Bright(x-EdgeNeighborDistancePx,y),f.Bright(x+EdgeNeighborDistancePx,y))>EdgeContrast;
    }
    private static bool ColoredEdge(Pixels f,int x,int y)
    {
        for(int xx=x-ColoredEdgeRadiusPx;xx<=x+ColoredEdgeRadiusPx;xx++)
        {
            int i=f.Offset(xx,y);int max=Math.Max(f.Data[i],Math.Max(f.Data[i+1],f.Data[i+2]));
            int min=Math.Min(f.Data[i],Math.Min(f.Data[i+1],f.Data[i+2]));
            if(max>ColoredEdgeBrightness && max-min>ColoredEdgeContrast)return true;
        }
        return false;
    }
    public static int ActiveTab(Pixels frame,BankLayout bank)
    {
        int selected=-1,best=0;
        for(int c=0;c<TabCount;c++)
        {
            int count=0;
            var rect=new Rectangle(bank.Tabs.Left+(int)(c*TabPitchPx*bank.Scale),bank.Tabs.Top,(int)(TabButtonWidthPx*bank.Scale),bank.Tabs.Height);
            for(int y=rect.Top;y<rect.Bottom;y+=TabSampleStep)for(int x=rect.Left;x<rect.Right;x+=TabSampleStep)
            {
                int i=frame.Offset(x,y);
                if(frame.Data[i+2]>ActiveTabRedThreshold && frame.Data[i+2]>frame.Data[i+1]*ActiveTabRedGreenRatio && frame.Data[i+1]>frame.Data[i]*ActiveTabGreenBlueRatio)count++;
            }
            if(count>best){best=count;selected=c;}
        }
        return best>ActiveTabMinimumPixels ? selected : -1;
    }
}
