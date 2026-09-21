using static FsBank.Scanner.BankGeometry;

namespace FsBank.Scanner;

internal enum ScanTarget { Bank, Equipped, Inventory }

// Capture and OCR only need slot geometry and a stable panel verification area.
// Tab navigation belongs exclusively to BankLayout.
internal abstract record ScanLayout(Rectangle Bounds, Point Anchor, double Scale)
{
    public abstract ScanTarget Target { get; }
    public string LocationType => Target.ToString().ToLowerInvariant();
    public abstract int RowCount { get; }
    public abstract int ColumnCount { get; }
    public int SlotCount => RowCount*ColumnCount;
    public abstract Rectangle Cell(int row,int column);
    public abstract Rectangle VerificationArea { get; }
    public abstract Point Park { get; }
    public Rectangle Grid => Rectangle.Union(Cell(0,0),Cell(RowCount-1,ColumnCount-1));
    public Rectangle Relative(Rectangle rect) => new(Anchor.X+(int)Math.Round(rect.X*Scale),Anchor.Y+(int)Math.Round(rect.Y*Scale),
        (int)Math.Round(rect.Width*Scale),(int)Math.Round(rect.Height*Scale));
    public ScanLayout Translate(int x,int y) => this with
    {
        Anchor=new(Anchor.X+x,Anchor.Y+y),Bounds=new(Bounds.X+x,Bounds.Y+y,Bounds.Width,Bounds.Height)
    };
    public IEnumerable<(int Row,int Col,Rectangle Bounds)> Slots()
    {
        for(int row=0;row<RowCount;row++)for(int col=0;col<ColumnCount;col++)yield return (row,col,Cell(row,col));
    }
}

internal sealed record BankLayout(Rectangle Bounds, Point Anchor, double Scale) : ScanLayout(Bounds,Anchor,Scale)
{
    public override ScanTarget Target => ScanTarget.Bank;
    public override int RowCount => Rows;
    public override int ColumnCount => Columns;
    public override Rectangle Cell(int row,int column) => Relative(new(GridOffsetX+CellPitchPx*column,GridOffsetY+CellPitchPx*row,CellSizePx,CellSizePx));
    public Rectangle Tabs => Relative(new(ReferenceTabs.X-ReferenceTitleAnchor.X,ReferenceTabs.Y-ReferenceTitleAnchor.Y,ReferenceTabs.Width,ReferenceTabs.Height));
    public override Rectangle VerificationArea => Tabs;
    public override Point Park => new(Bounds.Left+(int)(ParkLeftInsetPx*Scale),Bounds.Bottom-(int)(ParkBottomInsetPx*Scale));
    public Point TabCenter(int index)
    {
        if(index<0 || index>=TabCount)throw new ArgumentOutOfRangeException(nameof(index));
        return new(Tabs.Left+(int)Math.Round((index*TabPitchPx+TabButtonWidthPx/2.0)*Scale),Tabs.Top+Tabs.Height/2);
    }
}

// Both character targets share POWER/STATS anchors, regardless of character name.
// Offsets measured from POWER at (890,414) on the 2560x1440 reference.
internal abstract record CharacterLayout(Rectangle Bounds, Point Anchor, double Scale) : ScanLayout(Bounds,Anchor,Scale)
{
    // A visible stats scrollbar shifts the centered headings, but not the slots.
    public int ContentOffsetX { get; init; }
    protected Rectangle ContentRelative(Rectangle rect) => Relative(rect with { X=rect.X+ContentOffsetX });
    public static readonly Rectangle PanelOffset = new(-482,-116,648,824);
    public override Rectangle VerificationArea => Relative(new(7,69,78,23));
    public override Point Park => new(Bounds.Left+(int)(32*Scale),Bounds.Bottom-(int)(40*Scale));
}

internal sealed record EquippedLayout(Rectangle Bounds, Point Anchor, double Scale) : CharacterLayout(Bounds,Anchor,Scale)
{
    public override ScanTarget Target => ScanTarget.Equipped;
    public override int RowCount => 7;
    public override int ColumnCount => 2;
    public override Rectangle Cell(int row,int column) => ContentRelative(new(-442+320*column,64*row,56,56));
}

// Inventory occupies the three rows below the equipment slots.
internal sealed record InventoryLayout(Rectangle Bounds, Point Anchor, double Scale) : CharacterLayout(Bounds,Anchor,Scale)
{
    public override ScanTarget Target => ScanTarget.Inventory;
    public override int RowCount => 3;
    public override int ColumnCount => 9;
    public override Rectangle Cell(int row,int column) => ContentRelative(new(-444+64*column,450+64*row,56,56));
}
