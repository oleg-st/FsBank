using FsBank.Scanner.Game;
using FsBank.Scanner.Imaging;
using FsBank.Scanner.Input;

namespace FsBank.Scanner.Scanning;

internal static class ScanPreparation
{
    internal static void CheckCancellation(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if((Native.GetAsyncKeyState((int)Keys.Escape)&Native.KeyDownMask)!=0)
            throw new OperationCanceledException("Stopped with Esc.",token);
    }
    private static void Wait(CancellationToken token,int milliseconds=100)
    {
        if(token.WaitHandle.WaitOne(milliseconds))token.ThrowIfCancellationRequested();
        CheckCancellation(token);
    }
    public static nint GetGame(ScanTarget target,bool activate,ScanProgressTracker progress,CancellationToken token)
    {
        bool activationRequested=false;
        while(true)
        {
            CheckCancellation(token);
            nint game=Native.FindGame();
            if(game==0)
            {
                progress.Waiting(target,"Open Fellowship to continue.");
                activationRequested=false;Wait(token,300);continue;
            }
            if(activate && !activationRequested && Native.GetForegroundWindow()!=game)
            {
                Native.Activate(game);activationRequested=true;
            }
            if(Native.GetForegroundWindow()!=game || Native.IsIconic(game))
            {
                progress.Waiting(target,"Switch to Fellowship to continue.");
                Wait(token);continue;
            }
            // Read bounds after restoration/activation has settled, not while minimized.
            var bounds=Native.ClientBounds(game);
            if(bounds.Width<=0 || bounds.Height<=0){Wait(token);continue;}
            progress.SetBounds(bounds);
            return game;
        }
    }

    // No synthetic input is held or sent here. User input is expected while opening panels.
    public static ScanLayout WaitForPanel(nint game,Rectangle client,ScanTarget target,PanelSearch search,
        Func<Pixels> read,ScanProgressTracker progress,CancellationToken token,Pixels? initial=null)
    {
        while(true)
        {
            CheckCancellation(token);
            if(Native.ClientBounds(game)!=client)throw new InvalidOperationException("The game window moved or resized. Start a new scan in its new position.");
            if(Native.GetForegroundWindow()!=game)
            {
                progress.Waiting(target,"Switch to Fellowship to continue.");
                initial=null;
                Wait(token);continue;
            }
            var layout=search.Find(initial ?? read(),target);
            initial=null;
            if(layout is null)
            {
                progress.Waiting(target,target==ScanTarget.Bank
                    ? "Bank not detected. Open your stash to continue."
                    : "Character panel not detected. Open it to continue.");
                Wait(token,10);continue;
            }
            bool held=new[]{Keys.LButton,Keys.RButton,Keys.Menu}.Any(key=>(Native.GetAsyncKeyState((int)key)&Native.KeyDownMask)!=0);
            // Start on the first usable frame. Capture already handles mouse movement
            // and waits for the tooltip itself; no extra countdown is needed here.
            if(!held)return layout;
            progress.Waiting(target,"Release Alt and the mouse buttons to start.");
            Wait(token);
        }
    }
}
