using static FsBank.Scanner.ScanConstants;
using static FsBank.Scanner.UiConstants;

namespace FsBank.Scanner;

internal sealed class MainForm : Form
{
    private readonly Button scanCurrentTab = new() { Text="Scan current bank tab", AutoSize=true };
    private readonly Button scanInventory = new() { Text="Scan inventory", AutoSize=true };
    private readonly Button scanEquipped = new() { Text="Scan equipped", AutoSize=true };
    private readonly Button scanEquippedManual = new() { Text="Scan equipped manual", AutoSize=true };
    private readonly Button stop = new() { Text="Stop — Esc",AutoSize=true,Enabled=false };
    private readonly Button recognize = new() { Text="Recognize latest capture", AutoSize=true };
    private readonly Button scanAllTabs = new() { Text="Scan all bank tabs", AutoSize=true };
    private readonly TextBox folder = new() { Width=FolderInputWidth,Text=Path.Combine(Directory.GetCurrentDirectory(),GameConstants.ItemsFolder) };
    private readonly TextBox log = new() { Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill };
    private CancellationTokenSource? cancellation;
    private bool closing;
    public MainForm()
    {
        Text="FsBank"; Size=WindowSize; MinimumSize=MinimumWindowSize;
        var controls=new FlowLayoutPanel { Dock=DockStyle.Top,Height=ControlsHeight,Padding=new(SectionGap),AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink };
        controls.Controls.Add(new Label { Text="Open the stash for bank scans, or the character window for equipped items and inventory.",AutoSize=true,Margin=new(SmallGap,SmallGap,SmallGap,SectionGap) });
        controls.SetFlowBreak(controls.Controls[0],true);
        controls.Controls.Add(scanCurrentTab); controls.Controls.Add(scanAllTabs); controls.Controls.Add(scanEquipped); controls.Controls.Add(scanInventory); controls.Controls.Add(stop);
        controls.SetFlowBreak(stop,true);
        controls.Controls.Add(scanEquippedManual);controls.SetFlowBreak(scanEquippedManual,true);
        controls.Controls.Add(new Label {Text="Folder:",AutoSize=true,Margin=new(SmallGap,FolderLabelTopGap,SmallGap,SmallGap)}); controls.Controls.Add(folder);
        var open=new Button {Text="Open folder",AutoSize=true};controls.Controls.Add(open);
        controls.SetFlowBreak(open,true); controls.Controls.Add(recognize);
        recognize.Click+=async (_,_)=>await Recognize();
        open.Click+=(_,_)=> {Directory.CreateDirectory(folder.Text); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder.Text){UseShellExecute=true});};
        Controls.Add(log);Controls.Add(controls);
        scanCurrentTab.Click+=async (_,_)=>await Scan(ScanTarget.Bank);
        scanAllTabs.Click+=async (_,_)=>await Scan(ScanTarget.Bank,true);
        scanInventory.Click+=async (_,_)=>await Scan(ScanTarget.Inventory);
        scanEquipped.Click+=async (_,_)=>await Scan(ScanTarget.Equipped);
        scanEquippedManual.Click+=async (_,_)=>await Scan(ScanTarget.Equipped,manual:true);
        stop.Click+=(_,_)=>cancellation?.Cancel();
        Shown+=(_,_)=>Write("Ready. Choose Scan current bank tab, Scan all bank tabs, Scan equipped, or Scan inventory. Press Esc to stop. Automatic scans control the mouse. Manual mode: move the mouse and hold Left Alt yourself.");
        FormClosing+=(_,e)=> {if(cancellation is not null){e.Cancel=true;closing=true;cancellation.Cancel();}};
        FormClosed+=(_,_)=> {Native.UnregisterHotKey(Handle,Native.StopHotkeyId);};
    }
    protected override void WndProc(ref Message message)
    {
        if(message.Msg==Native.WM_HOTKEY)
        {
            if(message.WParam==Native.StopHotkeyId || message.WParam==Native.AltStopHotkeyId) cancellation?.Cancel();
        }
        base.WndProc(ref message);
    }
    private async Task Scan(ScanTarget target,bool scanAll=false,bool manual=false)
    {
        if(cancellation is not null)return;
        cancellation=new(); var token=cancellation.Token;
        scanEquippedManual.Enabled=false;scanCurrentTab.Enabled=false;scanAllTabs.Enabled=false;scanEquipped.Enabled=false;scanInventory.Enabled=false;recognize.Enabled=false;stop.Enabled=true;folder.Enabled=false;
        Native.RegisterHotKey(Handle,Native.StopHotkeyId,Native.HotkeyNoRepeat,(uint)Keys.Escape);
        if(!Native.RegisterHotKey(Handle,Native.AltStopHotkeyId,Native.HotkeyNoRepeat|Native.HotkeyAlt,(uint)Keys.Escape))
            Write("Alt+Esc is unavailable: press Esc or use Stop to stop.");
        try
        {
            string output=Path.GetFullPath(folder.Text);
            if(manual)
            {
                using var overlay=new ManualOverlay();
                try
                {
                    await Task.Run(()=>new ManualEquippedScanner(Write).Run(output,token,(client,layout,pending,done)=>
                    {
                        Invoke(()=> { if(!token.IsCancellationRequested)overlay.UpdateSlots(client,layout,pending,done); });
                    },message=>
                    {
                        Invoke(()=>overlay.SetStatus(message));
                    }));
                }
                finally { overlay.Hide(); }
                return;
            }
            Write(target switch
            {
                ScanTarget.Equipped => "Mode: equipped items, 14 slots. Open the character window before scanning.",
                ScanTarget.Inventory => "Mode: inventory, 27 slots (3×9). Open the character window before scanning.",
                _ => scanAll ? "Mode: all 7 bank tabs in order." : "Mode: current bank tab only."
            });
            await Task.Run(()=>
            {
                var ocr = new OcrPipeline(Write);
                Exception? captureError = null;
                try
                {
                    var scanner=new ItemScanner(Write, ocr);
                    if(scanAll)scanner.RunAll(output,InitialHoverDelayMs,token);else scanner.Run(output,InitialHoverDelayMs,token,target);
                }
                catch(Exception e) { captureError = e; throw; }
                finally
                {
                    try { ocr.Complete(); }
                    catch(Exception e) when(captureError is not null) { Write("OCR error: " + e.Message); }
                }
            });
        }
        catch(OperationCanceledException){Write("Capture stopped. Tooltips already captured have been saved.");}
        catch(Exception e){Write("Error: "+e.Message);}
        finally
        {
            Native.UnregisterHotKey(Handle,Native.StopHotkeyId);
            Native.UnregisterHotKey(Handle,Native.AltStopHotkeyId);
            cancellation.Dispose();cancellation=null;scanEquippedManual.Enabled=true;scanCurrentTab.Enabled=true;scanAllTabs.Enabled=true;scanEquipped.Enabled=true;scanInventory.Enabled=true;recognize.Enabled=true;stop.Enabled=false;folder.Enabled=true;
            if(closing) Close();
        }
    }
    private async Task Recognize()
    {
        if(cancellation is not null)return;
        cancellation=new(); var token=cancellation.Token;
        scanEquippedManual.Enabled=false;scanCurrentTab.Enabled=false;scanAllTabs.Enabled=false;scanEquipped.Enabled=false;scanInventory.Enabled=false;recognize.Enabled=false;stop.Enabled=true;folder.Enabled=false;
        Native.RegisterHotKey(Handle,Native.StopHotkeyId,Native.HotkeyNoRepeat,(uint)Keys.Escape);
        try
        {
            string report=await ItemRecognition.Run(Path.GetFullPath(folder.Text),Write,token);
            Write("OCR complete. Results require review; report: "+report);
        }
        catch(OperationCanceledException){Write("Recognition stopped.");}
        catch(Exception e){Write("OCR error: "+e.Message);}
        finally
        {
            Native.UnregisterHotKey(Handle,Native.StopHotkeyId);
            cancellation.Dispose();cancellation=null;scanEquippedManual.Enabled=true;scanCurrentTab.Enabled=true;scanAllTabs.Enabled=true;scanEquipped.Enabled=true;scanInventory.Enabled=true;recognize.Enabled=true;stop.Enabled=false;folder.Enabled=true;
            if(closing)Close();
        }
    }
    private void Write(string message)
    {
        if(IsDisposed || !IsHandleCreated)return;
        if(InvokeRequired){BeginInvoke(()=>Write(message));return;}
        log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }
}


