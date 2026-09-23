using System.Diagnostics;
using FsBank.Scanner.Input;
using FsBank.Scanner.Ocr;
using FsBank.Scanner.Scanning;

namespace FsBank.Scanner.UI;

internal sealed class MainForm : Form
{
    private bool autoEquipped=true,autoInventory=true,autoBank=true;
    private string? lastExport;
    private readonly RadioButton autoMode=new() { Name="modeAuto",Text="Auto",AutoSize=true };
    private readonly RadioButton manualMode=new() { Name="modeManual",Text="Manual",AutoSize=true };
    private readonly CheckBox equipped=new() { Name="targetEquipped",Text="Equipped",AutoSize=true };
    private readonly CheckBox inventory=new() { Name="targetInventory",Text="Inventory",AutoSize=true };
    private readonly CheckBox bank=new() { Name="targetBank",Text="Bank",AutoSize=true };
    private readonly RadioButton allTabs=new() { Text="All tabs",AutoSize=true };
    private readonly RadioButton currentTab=new() { Text="Current tab",AutoSize=true };
    private readonly Label modeHelp=Note("");
    private readonly Label inventoryHint=Note("");
    private readonly Label bankHint=Note("");
    private readonly Label status=Note("");
    private readonly Label scanNotice=Note("");
    private readonly Label location=Note("");
    private readonly Button start=Button("Start scan",true,"scanStart");
    private readonly Button stop=Button("Stop · Esc",false,"scanStop");
    private readonly Button viewItems=Button("View items");
    private readonly Button reviewIssues=Button("Review issues");
    private readonly Button exportJson=Button("Export JSON…");
    private readonly Button browseFolder=Button("Browse…");
    private readonly LinkLabel openFolder=Link("Open folder");
    private readonly LinkLabel recognizeCaptures=Link("Recognize saved captures…");
    private readonly TextBox exportFolder=new() { Dock=DockStyle.Fill };
    private readonly CheckBox diagnostics=new() { Name="debugCaptures",Text="Save diagnostic captures",AutoSize=true };
    private readonly TextBox log=new() { Name="scanLog",Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Height=150,Visible=false,Font=new("Consolas",9) };
    private readonly ScanProgressView progressView=new() { Visible=false };
    private readonly TabControl pages=new() { Name="mainTabs",Dock=DockStyle.Fill,Padding=new(18,7) };
    private readonly TabPage scanPage=new("Scan") { Name="scanPage",BackColor=Color.White,Padding=new(18,10,18,12) };
    private readonly TabPage resultPage=new("Result") { Name="resultPage",BackColor=Color.White,Padding=new(18,14,18,12) };
    private readonly TableLayoutPanel content=new() { Name="scanContent",Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1 };
    private readonly TableLayoutPanel resultContent=new() { Name="resultContent",Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=1 };
    private readonly System.Windows.Forms.Timer stopPoll=new() { Interval=50 };
    private CancellationTokenSource? cancellation;
    private ScanOverlay? overlay;
    private ScanSnapshot? latest;
    private bool refreshing,closing;
    private long runId,revision=-1;
    private string? overlayError;

    public MainForm()
    {
        Text="FsBank";Font=new("Segoe UI",10);BackColor=Color.White;ForeColor=ScanProgressView.Ink;
        AutoScaleMode=AutoScaleMode.Dpi;ClientSize=new(630,610);MinimumSize=new(580,649);StartPosition=FormStartPosition.CenterScreen;
        var shell=new Panel { Dock=DockStyle.Fill,Padding=new(10) };
        shell.Controls.Add(pages);Controls.Add(shell);pages.TabPages.Add(scanPage);
        content.ColumnStyles.Add(new(SizeType.Percent,100));scanPage.Controls.Add(content);
        resultContent.ColumnStyles.Add(new(SizeType.Percent,100));
        var resultLayout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=2 };
        resultLayout.ColumnStyles.Add(new(SizeType.Percent,100));
        resultLayout.RowStyles.Add(new(SizeType.AutoSize));resultLayout.RowStyles.Add(new(SizeType.Percent,100));
        resultLayout.Controls.Add(resultContent,0,0);resultLayout.Controls.Add(log,0,1);resultPage.Controls.Add(resultLayout);
        log.Dock=DockStyle.Fill;log.Margin=new(0,6,0,0);
        Add(Heading("Scan mode"));Add(Flow(autoMode,manualMode));Add(modeHelp);
        Add(Heading("What to scan"));
        Add(TargetRow(equipped,Note("Worn items")));Add(TargetRow(inventory,inventoryHint));Add(TargetRow(bank,bankHint));
        var tabs=Flow(allTabs,currentTab);tabs.Margin=new(24,0,0,8);Add(tabs);
        Add(Heading("Results folder"));
        var folderRow=new TableLayoutPanel { Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=2 };
        folderRow.ColumnStyles.Add(new(SizeType.Percent,100));folderRow.ColumnStyles.Add(new(SizeType.AutoSize));
        folderRow.Controls.Add(exportFolder,0,0);folderRow.Controls.Add(browseFolder,1,0);exportFolder.Anchor=AnchorStyles.Left|AnchorStyles.Right;
        Add(folderRow);diagnostics.Margin=new(0,9,0,5);Add(diagnostics);
        Add(Flow(openFolder,recognizeCaptures));
        var actions=Flow(start);actions.Margin=new(0,18,0,0);Add(actions);Add(scanNotice);
        progressView.Margin=new(0,0,0,8);AddResult(progressView);AddResult(status);
        foreach(var button in new[] { stop,viewItems,reviewIssues,exportJson })button.Padding=new(8,6,8,6);
        AddResult(Flow(stop,viewItems,reviewIssues,exportJson));AddResult(location);
        AddResult(Toggle("Technical details",log));
        status.Visible=scanNotice.Visible=false;
        status.TextChanged+=(_,_)=>status.Visible=status.Text.Length>0;
        scanNotice.TextChanged+=(_,_)=>scanNotice.Visible=scanNotice.Text.Length>0;
        pages.SelectedIndexChanged+=(_,_)=>AcceptButton=pages.SelectedTab==scanPage ? start : null;

        refreshing=true;
        autoMode.Checked=true;allTabs.Checked=true;
        exportFolder.Text=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"FsBank");
        refreshing=false;RefreshSelection();UpdateResults();stop.Enabled=false;stop.Visible=false;
        autoMode.CheckedChanged+=(_,_)=> { if(autoMode.Checked)RefreshSelection(); };
        manualMode.CheckedChanged+=(_,_)=> { if(manualMode.Checked)RefreshSelection(); };
        foreach(var check in new[] { equipped,inventory,bank })check.CheckedChanged+=(_,_)=>
        {
            if(refreshing || manualMode.Checked)return;
            autoEquipped=equipped.Checked;autoInventory=inventory.Checked;autoBank=bank.Checked;RefreshSelection();
        };
        start.Click+=async (_,_)=>await Scan();stop.Click+=(_,_)=>CancelScan();
        browseFolder.Click+=(_,_)=>
        {
            using var dialog=new FolderBrowserDialog { Description="Choose where FsBank saves scan results",UseDescriptionForTitle=true,InitialDirectory=exportFolder.Text };
            if(dialog.ShowDialog(this)==DialogResult.OK)exportFolder.Text=dialog.SelectedPath;
        };
        openFolder.Click+=(_,_)=>OpenPath(exportFolder.Text,true);
        viewItems.Click+=(_,_)=>OpenResult("items.html");
        reviewIssues.Click+=(_,_)=>OpenResult(File.Exists(Path.Combine(lastExport ?? "","scan-issues.html")) ? "scan-issues.html" : "report.html");
        exportJson.Click+=(_,_)=>SaveJson();recognizeCaptures.Click+=async (_,_)=>await Recognize();
        stopPoll.Tick+=(_,_)=> { if(cancellation is not null && latest?.Phase!=ScanPhase.Finishing && (Native.GetAsyncKeyState((int)Keys.Escape)&Native.KeyDownMask)!=0)CancelScan(); };
        FormClosing+=(_,e)=> { if(cancellation is not null){e.Cancel=true;closing=true;CancelScan();} };
        FormClosed+=(_,_)=> { UnregisterStopKeys();overlay?.Dispose(); };
        Resize+=(_,_)=>UpdateWrap();Shown+=(_,_)=> { UpdateWrap();Write("Ready. Select areas and start. Esc stops a scan; captured items are kept."); };
        AcceptButton=start;
    }
    private static Label Note(string text)=>new() { Text=text,AutoSize=true,ForeColor=ScanProgressView.Muted,Margin=new(0,3,0,5) };
    private Label Heading(string text)=>new() { Text=text,AutoSize=true,Font=new(Font,FontStyle.Bold),Margin=new(0,17,0,6) };
    private static Button Button(string text,bool primary=false,string? name=null)
    {
        var button=new Button { Text=text,Name=name ?? "",AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new(12,6,12,6),FlatStyle=FlatStyle.Flat,
            BackColor=primary ? ScanProgressView.Accent : Color.White,ForeColor=primary ? Color.White : ScanProgressView.Ink,Margin=new(0,3,8,3) };
        button.FlatAppearance.BorderColor=primary ? ScanProgressView.Accent : Color.FromArgb(204,211,220);return button;
    }
    private static FlowLayoutPanel Flow(params Control[] controls)
    {
        var flow=new FlowLayoutPanel { Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=true,Margin=Padding.Empty };
        foreach(var control in controls){if(control is RadioButton)control.Margin=new(0,3,26,3);flow.Controls.Add(control);}return flow;
    }
    private static TableLayoutPanel TargetRow(CheckBox check,Label hint)
    {
        var row=new TableLayoutPanel { AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,ColumnCount=2,Margin=new(0,2,0,2) };
        row.ColumnStyles.Add(new(SizeType.Absolute,145));row.ColumnStyles.Add(new(SizeType.Percent,100));
        check.Margin=new(0,3,0,3);row.Controls.Add(check);row.Controls.Add(hint);return row;
    }
    private static LinkLabel Toggle(string text,Control panel)
    {
        var link=new LinkLabel { Text="▸ "+text,AutoSize=true,LinkColor=ScanProgressView.Accent,Margin=new(0,10,0,4),LinkBehavior=LinkBehavior.HoverUnderline };
        link.Click+=(_,_)=> { panel.Visible=!panel.Visible;link.Text=(panel.Visible ? "▾ " : "▸ ")+text; };return link;
    }
    private static LinkLabel Link(string text)=>new() { Text=text,AutoSize=true,LinkColor=ScanProgressView.Accent,Margin=new(0,5,20,3),LinkBehavior=LinkBehavior.HoverUnderline };
    private void Add(Control control) { control.Dock=DockStyle.Top;content.Controls.Add(control); }
    private void AddResult(Control control) { control.Dock=DockStyle.Top;resultContent.Controls.Add(control); }
    internal void ShowResult()
    {
        if(!pages.TabPages.Contains(resultPage))pages.TabPages.Add(resultPage);
        pages.SelectedTab=resultPage;
        UpdateWrap();
    }
    private void UpdateWrap()
    {
        int width=Math.Max(240,content.ClientSize.Width-12);
        modeHelp.MaximumSize=scanNotice.MaximumSize=new(width,0);
        status.MaximumSize=location.MaximumSize=new(Math.Max(240,resultContent.ClientSize.Width-12),0);
    }
    private ScanOptions Options()=>new(manualMode.Checked ? ScanMode.Manual : ScanMode.Auto,equipped.Checked,inventory.Checked,bank.Checked,allTabs.Checked);
    private void RefreshSelection()
    {
        if(refreshing)return;refreshing=true;
        bool manual=manualMode.Checked,busy=cancellation is not null;
        equipped.Checked=manual || autoEquipped;inventory.Checked=!manual && autoInventory;bank.Checked=!manual && autoBank;
        equipped.Enabled=!manual && !busy;inventory.Enabled=bank.Enabled=!manual && !busy;
        allTabs.Enabled=currentTab.Enabled=!manual && bank.Checked && !busy;
        inventoryHint.Text=manual ? "Available in Auto" : "Items in your bags";
        bankHint.Text=manual ? "Available in Auto" : "Items in your stash";
        modeHelp.Text=manual ? "You move the mouse and hold Left Alt. Follow the overlay hints." : "FsBank moves the mouse. Missing panels wait for you to open them.";
        start.Enabled=!busy && Options().Targets.Length>0;
        refreshing=false;
    }
    private void SetBusy(bool busy)
    {
        autoMode.Enabled=manualMode.Enabled=exportFolder.Enabled=browseFolder.Enabled=recognizeCaptures.Enabled=!busy;
        diagnostics.Enabled=!busy;
        stop.Visible=stop.Enabled=busy;RefreshSelection();UpdateResults();
    }
    private void RegisterStopKeys()
    {
        bool escape=Native.RegisterHotKey(Handle,Native.StopHotkeyId,Native.HotkeyNoRepeat,(uint)Keys.Escape);
        bool altEscape=Native.RegisterHotKey(Handle,Native.AltStopHotkeyId,Native.HotkeyNoRepeat|Native.HotkeyAlt,(uint)Keys.Escape);
        if(!escape || !altEscape){Write("Some stop hotkeys are unavailable. Keyboard polling remains active.");stopPoll.Start();}
    }
    private void UnregisterStopKeys()
    {
        stopPoll.Stop();if(!IsHandleCreated)return;
        Native.UnregisterHotKey(Handle,Native.StopHotkeyId);Native.UnregisterHotKey(Handle,Native.AltStopHotkeyId);
    }
    protected override void WndProc(ref Message message)
    {
        if(message.Msg==Native.WM_HOTKEY && (message.WParam==Native.StopHotkeyId || message.WParam==Native.AltStopHotkeyId))CancelScan();
        base.WndProc(ref message);
    }
    private void CancelScan()
    {
        if(cancellation is null || latest?.Phase==ScanPhase.Finishing)return;
        cancellation.Cancel();stop.Enabled=false;status.Text="Stopping… Captured items will be saved.";
    }
    private async Task Scan()
    {
        if(cancellation is not null || Options().Targets.Length==0)return;
        var options=Options();string folder=exportFolder.Text;
        bool debug=diagnostics.Checked;
        cancellation=new();var token=cancellation.Token;long id=++runId;revision=-1;latest=null;overlayError=null;
        overlay?.Dispose();overlay=new(error=> { overlayError=error.Message;Write(error.Message);CancelScan(); });
        lastExport=null;status.Text=scanNotice.Text="";log.Clear();ShowResult();SetBusy(true);RegisterStopKeys();
        DisplayProgress(new ScanProgressTracker(options,_=> { }).Snapshot);
        try
        {
            var result=await Task.Run(()=>new ScanController(Write).Run(options,folder,debug,token,
                snapshot=>Post(()=>
                {
                    if(id!=runId || snapshot.Revision<revision)return;
                    revision=snapshot.Revision;DisplayProgress(snapshot);overlay?.UpdateProgress(snapshot);
                }),slots=>Post(()=> { if(id==runId)overlay?.UpdateSlots(slots); })));
            revision=result.Progress.Revision;DisplayProgress(result.Progress);overlay?.UpdateProgress(result.Progress);
            if(result.ExportFolders.Length>0)lastExport=result.ExportFolders[^1];
            if(result.RecoveryFolder is not null)Write("Recovery captures: "+result.RecoveryFolder);
            if(overlayError is not null)status.Text=overlayError;
        }
        catch(Exception e){status.Text="Could not start scan: "+e.Message;Write(e.ToString());overlay?.Dispose();overlay=null;}
        finally
        {
            UnregisterStopKeys();cancellation.Dispose();cancellation=null;SetBusy(false);if(closing)Close();
        }
    }
    internal void DisplayProgress(ScanSnapshot snapshot)
    {
        if(!pages.TabPages.Contains(resultPage))ShowResult();
        latest=snapshot;progressView.Visible=true;progressView.UpdateProgress(snapshot);
        status.Text=""; // The shared progress card already contains the current instruction/outcome.
        stop.Enabled=cancellation is not null && !cancellation.IsCancellationRequested && snapshot.Phase is ScanPhase.Ready or ScanPhase.Waiting or ScanPhase.Scanning;
    }
    private async Task Recognize()
    {
        if(cancellation is not null)return;
        using var dialog=new FolderBrowserDialog { Description="Select a folder with saved diagnostic captures",UseDescriptionForTitle=true,InitialDirectory=exportFolder.Text };
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        cancellation=new();latest=null;overlay?.Dispose();overlay=null;progressView.Visible=false;
        lastExport=null;scanNotice.Text="";log.Clear();ShowResult();SetBusy(true);RegisterStopKeys();status.Text="Recognizing saved captures…";
        try
        {
            string report=await ItemRecognition.Run(dialog.SelectedPath,Write,cancellation.Token);
            lastExport=Path.GetDirectoryName(report);status.Text="Recognition complete. Results are ready to review.";
        }
        catch(OperationCanceledException){status.Text="Recognition stopped.";}
        catch(Exception e){status.Text="Could not recognize captures: "+e.Message;Write(e.ToString());}
        finally { UnregisterStopKeys();cancellation.Dispose();cancellation=null;SetBusy(false);if(closing)Close(); }
    }
    private void UpdateResults()
    {
        bool available=cancellation is null && lastExport is not null && Directory.Exists(lastExport);
        viewItems.Enabled=available && File.Exists(Path.Combine(lastExport!,"items.html"));
        exportJson.Enabled=available && File.Exists(Path.Combine(lastExport!,"items.json"));
        reviewIssues.Enabled=available && (File.Exists(Path.Combine(lastExport!,"scan-issues.html")) || File.Exists(Path.Combine(lastExport!,"report.html")));
        location.Text=lastExport is null ? "" : "Result: "+Path.GetFileName(lastExport);
        location.Visible=lastExport is not null;
    }
    private void OpenResult(string file) { if(lastExport is not null)OpenPath(Path.Combine(lastExport,file)); }
    private void OpenPath(string path,bool directory=false)
    {
        try { path=Path.GetFullPath(path);if(directory)Directory.CreateDirectory(path);Process.Start(new ProcessStartInfo(path) { UseShellExecute=true }); }
        catch(Exception e){status.Text=scanNotice.Text="Could not open: "+e.Message;Write(e.Message);}
    }
    private void SaveJson()
    {
        if(lastExport is null)return;
        using var dialog=new SaveFileDialog { Filter="JSON files (*.json)|*.json",FileName="fsbank-items.json" };
        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
        try { File.Copy(Path.Combine(lastExport,"items.json"),dialog.FileName,true);status.Text="JSON exported."; }
        catch(Exception e){status.Text="Could not export JSON: "+e.Message;Write(e.Message);}
    }
    private void Post(Action action)
    {
        if(IsDisposed || !IsHandleCreated)return;
        try { if(InvokeRequired)BeginInvoke(()=> { if(!IsDisposed)action(); });else action(); }
        catch(InvalidOperationException) when(IsDisposed || !IsHandleCreated) { }
    }
    private void Write(string message)=>Post(()=>
    {
        if(log.TextLength>200000)log.Text=log.Text[^100000..];
        log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    });
    protected override void Dispose(bool disposing)
    {
        if(disposing){stopPoll.Dispose();overlay?.Dispose();cancellation?.Cancel();if(!pages.TabPages.Contains(resultPage))resultPage.Dispose();}
        base.Dispose(disposing);
    }
}
