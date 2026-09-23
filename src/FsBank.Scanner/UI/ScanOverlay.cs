using FsBank.Scanner.Game;
using FsBank.Scanner.Input;
using FsBank.Scanner.Scanning;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace FsBank.Scanner.UI;

internal sealed class ScanOverlay : Form
{
    [DllImport("user32.dll",SetLastError=true)] private static extern bool SetWindowDisplayAffinity(nint hwnd,uint affinity);
    private readonly ScanProgressView progressView=new();
    private readonly System.Windows.Forms.Timer focusTimer=new() { Interval=100 };
    private readonly Action<Exception> onError;
    private ScanSnapshot? snapshot;
    private ManualSlotProgress? slots;
    private nint game;
    private bool captureExcluded,failed;
    private long? completedAt;
    protected override bool ShowWithoutActivation=>true;
    protected override CreateParams CreateParams
    {
        get { var value=base.CreateParams;value.ExStyle|=0x08000000|0x00000020|0x00080000|0x00000080;return value; }
    }
    public ScanOverlay(Action<Exception> onError)
    {
        this.onError=onError;
        FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.Manual;ShowInTaskbar=false;TopMost=true;
        AutoScaleMode=AutoScaleMode.Dpi;BackColor=Color.Magenta;TransparencyKey=Color.Magenta;DoubleBuffered=true;
        Controls.Add(progressView);progressView.SizeChanged+=(_,_)=>PlaceProgress();
        focusTimer.Tick+=(_,_)=>RefreshVisibility();focusTimer.Start();
    }
    public void UpdateProgress(ScanSnapshot value)
    {
        if(snapshot is not null && value.Revision<snapshot.Revision)return;
        snapshot=value;
        if(value.Phase is ScanPhase.Completed or ScanPhase.Cancelled or ScanPhase.Failed)completedAt ??= Stopwatch.GetTimestamp();
        if(value.GameBounds is Rectangle bounds)
        {
            Bounds=bounds;
            if(game==0)game=Native.FindGame();
        }
        progressView.UpdateProgress(value);PlaceProgress();RefreshVisibility();Invalidate();
    }
    public void UpdateSlots(ManualSlotProgress value) { slots=value;PlaceProgress();Invalidate(); }
    private void PlaceProgress()
    {
        int inset=Math.Max(10,(int)(12*DeviceDpi/96f));
        progressView.Width=Math.Min((int)(365*DeviceDpi/96f),Math.Max(260,ClientSize.Width-inset*2));
        int x=inset,y=inset;
        if(slots is not null && snapshot?.Mode==ScanMode.Manual)
        {
            // Prefer the side outside the detected character panel.
            if(slots.Layout.Bounds.Left>progressView.Width+inset*2)x=slots.Layout.Bounds.Left-progressView.Width-inset;
            else if(slots.Layout.Bounds.Right+progressView.Width+inset*2<ClientSize.Width)x=slots.Layout.Bounds.Right+inset;
        }
        progressView.Location=new(Math.Clamp(x,0,Math.Max(0,ClientSize.Width-progressView.Width)),y);
    }
    private void RefreshVisibility()
    {
        if(failed || snapshot?.GameBounds is null || game==0)return;
        if(completedAt is long at && Stopwatch.GetElapsedTime(at).TotalSeconds>=10) { if(Visible)Hide();return; }
        try
        {
            if(Native.GetForegroundWindow()!=game || Native.IsIconic(game) || Native.ClientBounds(game)!=snapshot.GameBounds) { if(Visible)Hide();return; }
            if(Visible)return;
            if(!captureExcluded)
            {
                _=Handle;
                if(!SetWindowDisplayAffinity(Handle,0x11))throw new InvalidOperationException("Windows could not exclude the overlay from capture. Scanning stopped to protect captures.");
                captureExcluded=true;
            }
            Show();
            // Initial DPI scaling can change a Form's bounds on first display.
            // Slot rectangles and the game client already use physical pixels.
            if(snapshot.GameBounds is Rectangle bounds)Bounds=bounds;
            PlaceProgress();
        }
        catch(Exception e){failed=true;Hide();onError(e);}
    }
    protected override void WndProc(ref Message message)
    {
        if(message.Msg==0x0084){message.Result=(nint)(-1);return;} // HTTRANSPARENT
        if(message.Msg==0x0021){message.Result=(nint)3;return;} // MA_NOACTIVATE
        base.WndProc(ref message);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if(slots is null || snapshot?.Mode!=ScanMode.Manual || snapshot.Phase is not (ScanPhase.Scanning or ScanPhase.Finishing))return;
        var pending=slots.Pending.ToHashSet();var captured=slots.Captured.ToHashSet();
        var problems=snapshot.Issues.Where(issue=>issue.Area==ScanTarget.Equipped).Select(issue=>(issue.Row-1,issue.Column-1)).ToHashSet();
        foreach(var slot in slots.Layout.Slots())
        {
            var key=(slot.Row,slot.Col);
            if(!pending.Contains(key) && !captured.Contains(key))continue;
            using var pen=new Pen(problems.Contains(key) ? Color.OrangeRed : captured.Contains(key) ? Color.LimeGreen : Color.DeepSkyBlue,2);
            var bounds=slot.Bounds;bounds.Inflate(2,2);e.Graphics.DrawRectangle(pen,bounds);
        }
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing)focusTimer.Dispose();
        base.Dispose(disposing);
    }
}
