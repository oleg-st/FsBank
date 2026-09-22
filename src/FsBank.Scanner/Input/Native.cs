using FsBank.Scanner.Game;

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FsBank.Scanner.Input;

internal static class Native
{
    internal const int WM_HOTKEY = 0x0312;
    internal const uint HotkeyNoRepeat = 0x4000;
    internal const uint HotkeyAlt = 0x0001;
    internal const int StopHotkeyId = 2;
    internal const int AltStopHotkeyId = 4;
    internal const int KeyDownMask = 0x8000;
    private const int ShowRestore = 9;
    private const uint InputKeyboard = 1;
    private const uint KeyEventScanCode = 0x0008;
    private const uint KeyEventUp = 0x0002;
    private const ushort LeftAltScanCode = 0x0038;
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput
    {
        public ushort VirtualKey, ScanCode;
        public uint Flags, Time;
        public nuint ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public nuint ExtraInfo;
    }
    [StructLayout(LayoutKind.Explicit)] private struct InputData
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Input
    {
        public uint Type;
        public InputData Data;
    }
    [DllImport("user32.dll", SetLastError=true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    internal static bool SetLeftAlt(bool down)
    {
        Input[] inputs = [new() { Type=InputKeyboard, Data=new() { Keyboard=new()
            { ScanCode=LeftAltScanCode, Flags=KeyEventScanCode | (down ? 0 : KeyEventUp) } } }];
        return SendInput((uint)inputs.Length,inputs,Marshal.SizeOf<Input>())==(uint)inputs.Length;
    }
    internal static bool SetLeftMouse(bool down)
    {
        Input[] inputs = [new() { Type=0, Data=new() { Mouse=new() { Flags=down ? 0x0002u : 0x0004u } } }];
        return SendInput(1,inputs,Marshal.SizeOf<Input>())==1;
    }
    [DllImport("user32.dll", SetLastError=true)] internal static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(nint hwnd, int command);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")] internal static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] internal static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool ClientToScreen(nint hwnd, ref Point point);
    [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, nint data);
    private delegate bool EnumWindowsProc(nint hwnd, nint data);
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }

    internal static Rectangle ClientBounds(nint hwnd)
    {
        if (!GetClientRect(hwnd, out var rect)) throw new InvalidOperationException("Failed to read the game window dimensions.");
        var point = Point.Empty;
        if (!ClientToScreen(hwnd, ref point)) throw new InvalidOperationException("The game window is inaccessible.");
        return new Rectangle(point.X, point.Y, rect.Right, rect.Bottom);
    }
    internal static nint FindGame()
    {
        var ids = new HashSet<uint>();
        foreach (var p in Process.GetProcessesByName(GameConstants.ProcessName))
        { using (p) ids.Add((uint)p.Id); }
        nint best = 0; long largest = 0;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (!ids.Contains(pid) || !IsWindowVisible(hwnd)) return true;
            if (GetClientRect(hwnd, out var r))
            {
                long area = (long)r.Right * r.Bottom;
                if (area > largest) { largest = area; best = hwnd; }
            }
            return true;
        }, 0);
        return best;
    }
    internal static void Activate(nint hwnd)
    {
        if (IsIconic(hwnd)) ShowWindowAsync(hwnd, ShowRestore);
        if (SetForegroundWindow(hwnd)) return;
        uint current = GetCurrentThreadId();
        uint foreground = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        bool attached = current != foreground && foreground != 0 && AttachThreadInput(current, foreground, true);
        try { SetForegroundWindow(hwnd); }
        finally { if (attached) AttachThreadInput(current, foreground, false); }
    }
}
