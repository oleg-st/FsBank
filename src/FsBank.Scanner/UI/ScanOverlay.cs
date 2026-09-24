using FsBank.Scanner.Game;
using FsBank.Scanner.Input;
using FsBank.Scanner.Scanning;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace FsBank.Scanner.UI;

internal sealed class ScanOverlay : Form
{
    [DllImport("user32.dll",SetLastError=true)] private static extern bool SetWindowDisplayAffinity(nint hwnd,uint affinity);
    private readonly Font headingFont=new("Segoe UI",10,FontStyle.Bold);
    private readonly Font textFont=new("Segoe UI",9);
    private Rectangle progressBounds;
    private readonly System.Windows.Forms.Timer focusTimer=new() { Interval=100 };
    private readonly Action<Exception> onError;
    private ScanSnapshot? snapshot;
    private ManualSlotProgress? slots;
    private nint game;
    private bool captureExcluded,failed;
    private long? completedAt;
    private string ProgressMessage=>snapshot?.Phase is ScanPhase.Completed or ScanPhase.Finishing
        ? "" : snapshot?.Message ?? "";
    private string FooterText=>snapshot?.Phase switch
    {
        ScanPhase.Completed=>"Scan complete",
        ScanPhase.Cancelled or ScanPhase.Failed=>"",
        ScanPhase.Finishing=>"Saving results · You can use the mouse",
        _=>"Esc to stop · Captured items are kept"
    };
    private int Px(int value)=>(int)(value*DeviceDpi/96f);
    private int MessageHeight(int cardWidth)=>string.IsNullOrWhiteSpace(ProgressMessage) ? 0 :
        TextRenderer.MeasureText(ProgressMessage,textFont,new Size(Math.Max(1,cardWidth-Px(24)),int.MaxValue),
            TextFormatFlags.WordBreak|TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix).Height;
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
        // Paint the card and slot markers on the same buffered surface. Child
        // HWNDs repaint separately on a color-keyed layered window and flash
        // through the transparent background during OCR/layout updates.
        SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer,true);
        focusTimer.Tick+=(_,_)=>RefreshVisibility();focusTimer.Start();
    }
    public void UpdateProgress(ScanSnapshot value)
    {
        if(snapshot is not null && value.Revision<snapshot.Revision)return;
        InvalidateContent();
        snapshot=value;
        AccessibleName="Scan progress";
        AccessibleDescription=$"{value.Mode}. {value.Phase}. {value.Items} recognized. {ProgressMessage}";
        if(value.Phase is ScanPhase.Completed or ScanPhase.Cancelled or ScanPhase.Failed)completedAt ??= Stopwatch.GetTimestamp();
        if(value.GameBounds is Rectangle bounds)
        {
            if(Bounds!=bounds)Bounds=bounds;
            if(game==0)game=Native.FindGame();
        }
        PlaceProgress();RefreshVisibility();InvalidateContent();
    }
    public void UpdateSlots(ManualSlotProgress value) { InvalidateContent();slots=value;PlaceProgress();InvalidateContent(); }
    private void InvalidateContent()
    {
        if(!progressBounds.IsEmpty)Invalidate(progressBounds);
        if(slots is not null)
        {
            var bounds=slots.Layout.Bounds;bounds.Inflate(4,4);Invalidate(bounds);
        }
    }
    private void PlaceProgress()
    {
        int inset=Math.Max(10,(int)(12*DeviceDpi/96f));
        int width=Math.Min((int)(365*DeviceDpi/96f),Math.Max(1,ClientSize.Width-inset*2));
        int x=inset,y=inset;
        if(slots is not null && snapshot?.Mode==ScanMode.Manual)
        {
            // Prefer the side outside the detected character panel.
            if(slots.Layout.Bounds.Left>width+inset*2)x=slots.Layout.Bounds.Left-width-inset;
            else if(slots.Layout.Bounds.Right+width+inset*2<ClientSize.Width)x=slots.Layout.Bounds.Right+inset;
        }
        // Reserve the recognition row so draining the OCR queue does not resize the card.
        int messageHeight=MessageHeight(width);
        int height=Px(20)+Px(26)+Px(21)*(snapshot?.Areas.Length ?? 0)
            +Px(18)*(snapshot?.Areas.Count(area=>area.Tab is not null) ?? 0)+Px(8)
            +(messageHeight>0 ? messageHeight+Px(6) : 0)
            +Px(21)+(FooterText.Length>0 ? Px(21) : 0);
        progressBounds=new(Math.Clamp(x,0,Math.Max(0,ClientSize.Width-width)),y,width,height);
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
        PaintProgress(e.Graphics);
        if(slots is null || snapshot?.Mode!=ScanMode.Manual || snapshot.Phase is not (ScanPhase.Scanning or ScanPhase.Finishing))return;
        var pending=slots.Pending.ToHashSet();var captured=slots.Captured.ToHashSet();
        foreach(var slot in slots.Layout.Slots())
        {
            var key=(slot.Row,slot.Col);
            if(!pending.Contains(key) && !captured.Contains(key))continue;
            using var pen=new Pen(captured.Contains(key) ? Color.LimeGreen : Color.DeepSkyBlue,2);
            var bounds=slot.Bounds;bounds.Inflate(2,2);e.Graphics.DrawRectangle(pen,bounds);
        }
    }
    private void PaintProgress(Graphics graphics)
    {
        if(snapshot is null)return;
        var card=progressBounds;
        graphics.FillRectangle(Brushes.Black,card);
        int x=card.Left+Px(12),y=card.Top+Px(10),width=Math.Max(1,card.Width-Px(24));
        void Text(string value,Color color,int height,Font? font=null, bool wrap=false)
        {
            TextRenderer.DrawText(graphics,value,font ?? textFont,new Rectangle(x,y,width,Px(height)),color,
                TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding|TextFormatFlags.EndEllipsis|TextFormatFlags.PreserveGraphicsClipping|
                (wrap ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine));
            y+=Px(height);
        }
        Text($"FsBank · {snapshot.Mode}",Color.White,26,headingFont);
        foreach(var area in snapshot.Areas)
        {
            Color color=area.Phase switch
            {
                AreaPhase.Completed or AreaPhase.NeedsReview=>Color.LightGreen,
                AreaPhase.Waiting=>Color.Orange,
                AreaPhase.Scanning or AreaPhase.Recognizing=>Color.DeepSkyBlue,
                _=>Color.Silver
            };
            string state=area.Phase switch
            {
                AreaPhase.Completed or AreaPhase.NeedsReview=>"",
                AreaPhase.Recognizing=>"Recognizing",
                AreaPhase.Cancelled=>"Stopped",_=>area.Phase.ToString()
            };
            Text($"{area.Area} · {area.Items} {(area.Items==1 ? "item" : "items")}"+(state.Length>0 ? $" · {state}" : ""),color,21);
            string detail=area.Tab is int tab ? area.AllTabs ? $"Stash {tab} / {BankGeometry.TabCount}" : $"Stash {tab} · current tab" : "";
            if(detail.Length>0)Text(detail,Color.Silver,18);
        }
        y+=Px(8);
        int messageHeight=MessageHeight(card.Width);
        if(messageHeight>0)
        {
            TextRenderer.DrawText(graphics,ProgressMessage,textFont,new Rectangle(x,y,width,messageHeight),Color.White,
                TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding|TextFormatFlags.WordBreak|TextFormatFlags.PreserveGraphicsClipping);
            y+=messageHeight+Px(6);
        }
        Text(snapshot.Pending>0 ? $"Recognizing: {snapshot.Pending}" : "",Color.Silver,21);
        if(FooterText.Length>0)Text(FooterText,snapshot.Phase==ScanPhase.Completed ? Color.LightGreen : Color.Silver,21);
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing){focusTimer.Dispose();headingFont.Dispose();textFont.Dispose();}
        base.Dispose(disposing);
    }
}
