using System.Drawing;

namespace FsBank.Scanner;

// Y depends on tooltip height. Use horizontal placement and reset stability per slot.
internal sealed class ManualTooltipGate
{
    private (int,int)? active;
    public bool MatchesCursor { get; private set; }
    public bool Observe((int,int)? slot,Point cursor,Rectangle? tooltip,Size screen,double scale)
    {
        bool changed=slot!=active;
        active=slot;
        MatchesCursor=tooltip is {} bounds && AtCursor(cursor,bounds,screen,scale);
        return changed;
    }
    internal static bool AtCursor(Point cursor,Rectangle bounds,Size screen,double scale)
    {
        int tolerance=Math.Max(4,(int)Math.Ceiling(24*scale));
        int gap=(int)Math.Round(27*scale);
        bool right=Math.Abs(bounds.Left-(cursor.X+gap))<=tolerance;
        bool left=Math.Abs(bounds.Right-(cursor.X-gap))<=tolerance;
        bool overflowsRight=cursor.X+gap+bounds.Width>=screen.Width-tolerance;
        bool clampedRight=overflowsRight && Math.Abs(bounds.Right-screen.Width)<=tolerance;
        return right || (overflowsRight && left) || clampedRight;
    }
}
