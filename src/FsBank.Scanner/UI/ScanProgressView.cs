using FsBank.Scanner.Game;
using FsBank.Scanner.Scanning;

namespace FsBank.Scanner.UI;

// Shared by MainForm and the non-activating game overlay.
internal sealed class ScanProgressView : UserControl
{
    internal static readonly Color Ink=Color.FromArgb(34,42,53), Muted=Color.FromArgb(93,102,116);
    internal static readonly Color Accent=Color.FromArgb(25,95,168), Good=Color.FromArgb(40,112,72), Warning=Color.FromArgb(128,89,27);
    private readonly TableLayoutPanel body=new() { Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1,Margin=Padding.Empty };
    private readonly Label heading=new() { AutoSize=true,Margin=new(0,0,0,10) };
    private readonly Label phaseLabel=new() { AutoSize=true,Anchor=AnchorStyles.Right,Margin=new(12,0,0,10) };
    private readonly TableLayoutPanel rows=new() { Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1,Margin=Padding.Empty };
    private readonly Label instruction=new() { AutoSize=true,Padding=new(10),Margin=new(0,10,0,0),BackColor=Color.FromArgb(244,246,249) };
    private readonly Label pending=new() { AutoSize=true,ForeColor=Muted,Margin=new(0,8,0,0) };
    private readonly Label issues=new() { AutoSize=true,ForeColor=Warning,Margin=new(0,6,0,0) };
    private readonly Label footer=new() { AutoSize=true,ForeColor=Muted,Margin=new(0,10,0,0) };
    private readonly Dictionary<ScanTarget,(TableLayoutPanel Panel,Label Marker,Label Name,Label Value,Label Detail)> areaControls=[];
    private readonly Font strong;
    private readonly Font small;

    public ScanProgressView()
    {
        Font=new("Segoe UI",9.5f);strong=new(Font,FontStyle.Bold);small=new("Segoe UI",8.5f);
        ForeColor=Ink;BackColor=Color.White;Padding=new(14);AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;
        BorderStyle=BorderStyle.FixedSingle;MinimumSize=new(260,0);Width=380;
        heading.Font=strong;phaseLabel.Font=small;pending.Font=issues.Font=footer.Font=small;
        body.ColumnStyles.Add(new(SizeType.Percent,100));rows.ColumnStyles.Add(new(SizeType.Percent,100));
        var header=new TableLayoutPanel { Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=2,Margin=Padding.Empty };
        header.ColumnStyles.Add(new(SizeType.Percent,100));header.ColumnStyles.Add(new(SizeType.AutoSize));
        header.Controls.Add(heading);header.Controls.Add(phaseLabel);
        body.Controls.Add(header);body.Controls.Add(rows);body.Controls.Add(instruction);
        body.Controls.Add(pending);body.Controls.Add(issues);body.Controls.Add(footer);Controls.Add(body);
        Resize+=(_,_)=>
        {
            int width=Math.Max(100,ClientSize.Width-Padding.Horizontal-4);
            instruction.MaximumSize=pending.MaximumSize=issues.MaximumSize=footer.MaximumSize=new(width,0);
        };
        AccessibleName="Scan progress";
    }

    public void UpdateProgress(ScanSnapshot snapshot)
    {
        SuspendLayout();body.SuspendLayout();rows.SuspendLayout();
        heading.Text=$"FsBank · {snapshot.Mode}";phaseLabel.Text=PhaseText(snapshot.Phase);
        // Reuse controls while OCR reports progress; do not recreate their handles per item.
        if(!areaControls.Keys.SequenceEqual(snapshot.Areas.Select(area=>area.Area)))
        {
            foreach(var row in areaControls.Values)row.Panel.Dispose();
            areaControls.Clear();rows.Controls.Clear();rows.RowStyles.Clear();rows.RowCount=0;
            foreach(var area in snapshot.Areas)
            {
                var panel=new TableLayoutPanel { Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=3,Margin=new(0,2,0,2),Padding=new(9,8,9,8) };
                panel.ColumnStyles.Add(new(SizeType.Absolute,24));panel.ColumnStyles.Add(new(SizeType.Percent,100));panel.ColumnStyles.Add(new(SizeType.AutoSize));
                var marker=new Label { AutoSize=true,Margin=Padding.Empty };
                var name=new Label { Text=area.Area.ToString(),AutoSize=true,Margin=Padding.Empty };
                var value=new Label { AutoSize=true,Margin=Padding.Empty,Anchor=AnchorStyles.Right };
                var detail=new Label { AutoSize=true,Font=small,ForeColor=Muted,Margin=new(0,3,0,0) };
                panel.Controls.Add(marker,0,0);panel.Controls.Add(name,1,0);panel.Controls.Add(value,2,0);
                panel.Controls.Add(detail,1,1);panel.SetColumnSpan(detail,2);rows.Controls.Add(panel);
                areaControls.Add(area.Area,(panel,marker,name,value,detail));
            }
        }
        foreach(var area in snapshot.Areas)
        {
            var row=areaControls[area.Area];
            bool active=area.Phase is AreaPhase.Scanning or AreaPhase.Recognizing;
            bool waiting=area.Phase==AreaPhase.Waiting;
            row.Panel.BackColor=active ? Color.FromArgb(237,244,252) : waiting ? Color.FromArgb(255,245,223) : Color.White;
            row.Panel.ForeColor=active ? Accent : waiting ? Warning : area.Phase==AreaPhase.Queued ? Muted : Ink;
            row.Marker.Text=area.Phase switch
            {
                AreaPhase.Scanning or AreaPhase.Recognizing or AreaPhase.Waiting=>"→",AreaPhase.Completed=>"✓",
                AreaPhase.NeedsReview=>"!",AreaPhase.Cancelled=>"—",_=>"·"
            };
            row.Marker.ForeColor=area.Phase==AreaPhase.Completed ? Good : area.Phase==AreaPhase.NeedsReview ? Warning : row.Panel.ForeColor;
            row.Name.Font=row.Value.Font=active||waiting ? strong : Font;
            row.Value.Text=snapshot.Phase==ScanPhase.Ready ? "Ready to scan" : $"{area.Items} {(area.Items==1 ? "item" : "items")}";
            string detail=area.Tab is int tab ? area.AllTabs ? $"Stash {tab} / {BankGeometry.TabCount}" : $"Stash {tab} · current tab" : "";
            if(area.Phase==AreaPhase.Recognizing)detail+=(detail.Length>0 ? " · " : "")+"Finishing recognition";
            if(area.Phase==AreaPhase.NeedsReview)detail+=(detail.Length>0 ? " · " : "")+$"{area.Issues} need review";
            row.Detail.Text=detail;row.Detail.Visible=detail.Length>0;
            row.Panel.AccessibleName=$"{area.Area}: {row.Value.Text}, {area.Phase}";
        }
        instruction.Text=snapshot.Message;instruction.Visible=!string.IsNullOrWhiteSpace(snapshot.Message);
        pending.Text=$"Recognizing: {snapshot.Pending}";pending.Visible=snapshot.Pending>0;
        issues.Text=$"! Needs review: {snapshot.Issues.Length} · Details in results";issues.Visible=snapshot.Issues.Length>0;
        footer.Text=snapshot.Phase switch
        {
            ScanPhase.Ready=>"Panels are checked after switching to Fellowship.",
            ScanPhase.Finishing=>"Saving results · You can use the mouse",
            ScanPhase.Completed or ScanPhase.Cancelled or ScanPhase.Failed=>"Results in FsBank · The game keeps focus",
            _=>"Esc to stop · Captured items are kept"
        };
        AccessibleDescription=$"{snapshot.Mode}. {snapshot.Phase}. {snapshot.Items} recognized. {snapshot.Issues.Length} need review. {snapshot.Message}";
        rows.ResumeLayout(true);body.ResumeLayout(true);ResumeLayout(true);
    }
    internal static string PhaseText(ScanPhase phase) => phase switch
    { ScanPhase.Cancelled=>"Stopped",ScanPhase.Completed=>"Completed",_=>phase.ToString() };
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if(disposing){strong.Dispose();small.Dispose();Font.Dispose();}
    }
}
