using FsBank.Scanner.Game;
using FsBank.Scanner.Imaging;
using FsBank.Scanner.Input;
using System.Diagnostics;

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
    public static ScanLayout WaitForPanel(nint game,Rectangle client,ScanTarget target,Vision vision,
        Func<Pixels> read,ScanProgressTracker progress,CancellationToken token,bool countdown)
    {
        bool waited=false;
        ScanLayout? last=null;
        var settled=Stopwatch.StartNew();
        Native.GetCursorPos(out var lastCursor);
        while(true)
        {
            CheckCancellation(token);
            if(Native.ClientBounds(game)!=client)throw new InvalidOperationException("The game window moved or resized. Start a new scan in its new position.");
            if(Native.GetForegroundWindow()!=game)
            {
                progress.Waiting(target,"Switch to Fellowship to continue.");
                waited=true;last=null;settled.Restart();Wait(token);continue;
            }
            var layout=vision.FindLayout(read(),target);
            if(layout is null)
            {
                progress.Waiting(target,target==ScanTarget.Bank
                    ? "Bank not detected. Open your stash to continue."
                    : "Character panel not detected. Open it to continue.");
                waited=true;last=null;settled.Restart();Wait(token);continue;
            }
            bool held=new[]{Keys.LButton,Keys.RButton,Keys.Menu}.Any(key=>(Native.GetAsyncKeyState((int)key)&Native.KeyDownMask)!=0);
            bool cursorKnown=Native.GetCursorPos(out var cursor);
            if(last is null || layout.Bounds!=last.Bounds || held || !cursorKnown ||
                Math.Abs(cursor.X-lastCursor.X)>ScanConstants.CursorTolerancePx || Math.Abs(cursor.Y-lastCursor.Y)>ScanConstants.CursorTolerancePx)
                settled.Restart();
            last=layout;lastCursor=cursor;
            int delay=waited||countdown ? 2000 : 150;
            if(!held && settled.ElapsedMilliseconds>=delay)return layout;
            progress.Scanning(target,held ? "Release Alt and the mouse buttons to start."
                : $"Panel detected. Starting in {Math.Max(1,(int)Math.Ceiling((delay-settled.ElapsedMilliseconds)/1000d))} s. Keep the mouse still.");
            Wait(token);
        }
    }
}
