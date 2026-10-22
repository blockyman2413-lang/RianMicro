
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Mouse_Macro;

public sealed class MousePlayer
{
    private readonly object _sync = new();
    private CancellationTokenSource? _cts;

    public bool IsPlaying
    {
        get { lock (_sync) return _cts != null; }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (_cts is { IsCancellationRequested: false })
                _cts.Cancel();
        }
    }

    public async Task PlayAsync(
        List<MacroAction> actions,
        int loops,
        double speed,
        bool unlimited)
    {
        if (actions.Count == 0) return;

        if (loops < 1 || speed <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(speed));

        CancellationTokenSource cts;

        lock (_sync)
        {
            if (_cts != null) return;

            cts = new CancellationTokenSource();
            _cts = cts;
        }

        try
        {
            await Task.Run(() =>
            {
                int run = 0;

                while (unlimited || run < loops)
                {
                    cts.Token.ThrowIfCancellationRequested();

                    PlayOnce(actions, speed, cts.Token);
                    run++;

                    if (unlimited || run < loops)
                    {
                        if (cts.Token.WaitHandle.WaitOne(100))
                            cts.Token.ThrowIfCancellationRequested();
                    }
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping.
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_cts, cts))
                    _cts = null;
            }

            cts.Dispose();
        }
    }

    private static void PlayOnce(
        List<MacroAction> actions,
        double speed,
        CancellationToken token)
    {
        bool left = false;
        bool right = false;

        Dictionary<int, MacroAction> heldKeys = new();

        long first = actions[0].Timestamp;
        Stopwatch clock = Stopwatch.StartNew();

        try
        {
            foreach (MacroAction a in actions)
            {
                token.ThrowIfCancellationRequested();

                double target =
                    (a.Timestamp - first) / speed;

                WaitUntil(clock, target, token);

                token.ThrowIfCancellationRequested();

                switch (a.Type)
                {
                    case MacroActionType.MouseMove:
                        Move(a.X, a.Y);
                        break;

                    case MacroActionType.LeftDown:
                        Move(a.X, a.Y);
                        SendMouse(0x0002);
                        left = true;
                        break;

                    case MacroActionType.LeftUp:
                        Move(a.X, a.Y);
                        SendMouse(0x0004);
                        left = false;
                        break;

                    case MacroActionType.RightDown:
                        Move(a.X, a.Y);
                        SendMouse(0x0008);
                        right = true;
                        break;

                    case MacroActionType.RightUp:
                        Move(a.X, a.Y);
                        SendMouse(0x0010);
                        right = false;
                        break;

                    case MacroActionType.KeyDown:
                        SendKey(a, false);
                        heldKeys[a.VirtualKey] = a;
                        break;

                    case MacroActionType.KeyUp:
                        SendKey(a, true);
                        heldKeys.Remove(a.VirtualKey);
                        break;
                }
            }
        }
        finally
        {
            // Release macro-held inputs on cancellation.
            if (left)
                TryCleanup(() => SendMouse(0x0004));

            if (right)
                TryCleanup(() => SendMouse(0x0010));

            foreach (MacroAction a in heldKeys.Values)
                TryCleanup(() => SendKey(a, true));
        }
    }

    private static void TryCleanup(Action action)
    {
        try { action(); }
        catch { /* Best-effort cleanup. */ }
    }

    private static void Move(int x, int y)
    {
        if (!SetCursorPos(x, y))
            throw new Win32Exception(
                Marshal.GetLastWin32Error());
    }

    private static void WaitUntil(
        Stopwatch clock,
        double targetTicks,
        CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();

            double remainingMs =
                (targetTicks - clock.ElapsedTicks) *
                1000 / Stopwatch.Frequency;

            if (remainingMs <= 0)
                return;

            if (remainingMs > 3)
            {
                int sleep =
                    Math.Max(1, (int)remainingMs - 2);

                if (token.WaitHandle.WaitOne(sleep))
                    token.ThrowIfCancellationRequested();
            }
            else if (remainingMs > 0.5)
            {
                Thread.Sleep(0);
            }
            else
            {
                Thread.SpinWait(50);
            }
        }
    }

    private static void SendMouse(uint flags)
    {
        Send(new INPUT
        {
            type = 0,
            data = new InputUnion
            {
                mouse = new MouseInput
                {
                    flags = flags
                }
            }
        });
    }

    private static void SendKey(
        MacroAction a, bool up)
    {
        const uint KEYUP = 0x0002;
        const uint SCANCODE = 0x0008;
        const uint EXTENDED = 0x0001;

        bool useScan = a.ScanCode != 0;

        uint flags =
            (up ? KEYUP : 0) |
            (a.Extended ? EXTENDED : 0) |
            (useScan ? SCANCODE : 0);

        Send(new INPUT
        {
            type = 1,
            data = new InputUnion
            {
                keyboard = new KeyboardInput
                {
                    virtualKey = useScan
                        ? (ushort)0
                        : (ushort)a.VirtualKey,
                    scanCode = (ushort)a.ScanCode,
                    flags = flags
                }
            }
        });
    }

    private static void Send(INPUT input)
    {
        INPUT[] inputs = { input };

        if (SendInput(
            1,
            inputs,
            Marshal.SizeOf<INPUT>()) != 1)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error());
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput mouse;

        [FieldOffset(0)]
        public KeyboardInput keyboard;

        [FieldOffset(0)]
        public HardwareInput hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr extraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort virtualKey;
        public ushort scanCode;
        public uint flags;
        public uint time;
        public IntPtr extraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint message;
        public ushort paramLow;
        public ushort paramHigh;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(
        int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint count,
        INPUT[] inputs,
        int size);
}
