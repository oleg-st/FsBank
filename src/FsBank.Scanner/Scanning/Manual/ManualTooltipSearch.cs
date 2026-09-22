using FsBank.Scanner.Imaging;

namespace FsBank.Scanner.Scanning.Manual;

internal sealed class ManualTooltipSearch(Vision vision,double scale)
{
    private Tooltip? tracked;
    public int FullSearches => 0;
    public int LocalSearches { get; private set; }
    public int TrackSearches { get; private set; }
    public (Tooltip? Tooltip,bool Confirmed) Find(Pixels frame,Point cursor)
    {
        Tooltip? found=null;
        if(tracked is not null && vision.TooltipNearCursor(frame,scale,cursor,tracked))
        {TrackSearches++;found=vision.TrackTooltip(frame,scale,tracked);}
        if(found is not null && !vision.TooltipNearCursor(frame,scale,cursor,found))found=null;
        if(found is null){LocalSearches++;found=vision.FindTooltipNear(frame,scale,cursor);}
        if(found is not null)tracked=found;
        // Manual capture only accepts the current cursor's placement corridor.
        // Searching outside it cannot produce an acceptable item and stalls input polling.
        return (found,true);
    }
    public bool PreviousFooterVisible(Pixels frame) => tracked is not null && vision.FooterPresent(frame,scale,tracked.Footer);
}
