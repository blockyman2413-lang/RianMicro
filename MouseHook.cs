
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Mouse_Macro;

public sealed class MouseHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const uint LLMHF_INJECTED = 1;

    private readonly HookProc _callback;
    private IntPtr _hook;

    public event Action<MacroActionType, int, int>?
        MouseActionDetected;

    public MouseHook()
    {
        _callback = HookCallback;
    }

    public void Start()
    {
        if (_hook != IntPtr.Zero) return;

        using Process process = Process.GetCurrentProcess();
        using ProcessModule? module = process.MainModule;

        _hook = SetWindowsHookEx(
            WH_MOUSE_LL,
            _callback,
            GetModuleHandle(module?.ModuleName),
            0);

        if (_hook == IntPtr.Zero)
            throw new Win32Exception(
                Marshal.GetLastWin32Error());
    }

    public void Stop()
    {
        if (_hook == IntPtr.Zero) return;

        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr HookCallback(
        int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            MouseData data =
                Marshal.PtrToStructure<MouseData>(
                    lParam);

            if ((data.flags & LLMHF_INJECTED) == 0)
            {
                MacroActionType? type =
                    (int)wParam switch
                    {
                        0x0200 => MacroActionType.MouseMove,
                        0x0201 => MacroActionType.LeftDown,
                        0x0202 => MacroActionType.LeftUp,
                        0x0204 => MacroActionType.RightDown,
                        0x0205 => MacroActionType.RightUp,
                        _ => null
                    };

                if (type.HasValue)
                {
                    MouseActionDetected?.Invoke(
                        type.Value, data.pt.x, data.pt.y);
                }
            }
        }

        return CallNextHookEx(
            _hook, code, wParam, lParam);
    }

    public void Dispose() => Stop();

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseData
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr extraInfo;
    }

    private delegate IntPtr HookProc(
        int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int id, HookProc callback,
        IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(
        IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hook, int code,
        IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(
        string? name);
}
