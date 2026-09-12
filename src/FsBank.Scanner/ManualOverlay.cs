using System.Runtime.InteropServices;

namespace FsBank.Scanner;

// A separate desktop window: no game hooks, activation or input interception.
internal sealed class ManualOverlay : Form
{
    private ScanLayout? layout;
    private HashSet<(int,int)> pending = [];
    private HashSet<(int,int)> recognized = [];
    private nint game;
    private string status="Hover a red slot while holding Left Alt.";
    private readonly System.Windows.Forms.Timer focusTimer=new() { Interval=100 };
    [DllImport("user32.dll", SetLastError=true)]
    private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var p=base.CreateParams; p.ExStyle|=0x08000000|0x00000020|0x00080000|0x00000080; return p; }
    }
    public ManualOverlay()
    {
        FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; TopMost=true;
        BackColor=Color.Magenta; TransparencyKey=Color.Magenta; DoubleBuffered=true;
        focusTimer.Tick+=(_,_)=> { if(Visible && Native.GetForegroundWindow()!=game)Hide(); };
        focusTimer.Start();
    }
    public void UpdateSlots(Rectangle client,ScanLayout slots, IEnumerable<(int,int)> waiting,IEnumerable<(int,int)> done)
    {
        Bounds=client;layout=slots;pending=waiting.ToHashSet();recognized=done.ToHashSet();
        if(game==0)game=Native.FindGame();
        if(Native.GetForegroundWindow()!=game)return;
        if(!Visible)
        {
            // Do not allow overlay pixels to contaminate DXGI tooltip captures.
            _=Handle;
            if(!SetWindowDisplayAffinity(Handle,0x11))
                throw new InvalidOperationException("Windows could not exclude the overlay from capture. Manual capture stopped to avoid saving overlay pixels.");
            Show();
        }
        Invalidate();
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing)focusTimer.Dispose();
        base.Dispose(disposing);
    }
    public void SetStatus(string message){status=message;Invalidate();}
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if(layout is null)return;
        var banner=new Rectangle(Math.Clamp(layout.Bounds.Left,0,Math.Max(0,ClientSize.Width-760)),Math.Max(0,layout.Bounds.Top-40),Math.Min(760,ClientSize.Width),32);
        e.Graphics.FillRectangle(Brushes.Black,banner);
        TextRenderer.DrawText(e.Graphics,status,Font,banner,Color.White,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
        foreach(var slot in layout.Slots())
        {
            var key=(slot.Row,slot.Col);
            if(!pending.Contains(key) && !recognized.Contains(key))continue;
            using var pen=new Pen(recognized.Contains(key) ? Color.Lime : Color.Red,2);
            var rect=slot.Bounds;rect.Inflate(2,2);e.Graphics.DrawRectangle(pen,rect);
        }
    }
}
