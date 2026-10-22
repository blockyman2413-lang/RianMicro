
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Mouse_Macro;

public sealed class MousePlayer
{
    private readonly object _sync = new();
    private CancellationTokenSource? _cts;

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    // Keeps short clicks reliable without delaying keyboard events.
    private const double MinimumMouseHoldMs = 50.0;

    public bool IsPlaying
    {
        get
        {
            lock (_sync)
            {
                return _cts != null;
            }
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (_cts is { IsCancellationRequested: false })
            {
                _cts.Cancel();
            }
        }
    }

    public async Task PlayAsync(
        List<MacroAction> actions,
        int loops,
        double speed,
        bool unlimited)
    {
        if (actions == null)
        {
            throw new ArgumentNullException(nameof(actions));
        }

        if (actions.Count == 0)
        {
            return;
        }

        if (loops < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(loops));
        }

        if (double.IsNaN(speed) ||
            double.IsInfinity(speed) ||
            speed <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(speed));
        }

        CancellationTokenSource cts;

        lock (_sync)
        {
            if (_cts != null)
            {
                return;
            }

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

                    PlayOnce(
                        actions,
                        speed,
                        cts.Token);

                    run++;

                    // No artificial 100 ms pause between loops.
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation from Stop().
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_cts, cts))
                {
                    _cts = null;
                }
            }

            cts.Dispose();
        }
    }

    private static void PlayOnce(
        List<MacroAction> actions,
        double speed,
        CancellationToken token)
    {
        bool leftHeld = false;
        bool rightHeld = false;

        bool leftReleasePending = false;
        bool rightReleasePending = false;

        double leftReleaseTicks = 0;
        double rightReleaseTicks = 0;

        long leftDownTicks = 0;
        long rightDownTicks = 0;

        Dictionary<int, MacroAction> heldKeys = new();

        long firstTimestamp = actions[0].Timestamp;

        Stopwatch clock = Stopwatch.StartNew();

        double minimumHoldTicks =
            MinimumMouseHoldMs *
            Stopwatch.Frequency / 1000.0;

        try
        {
            foreach (MacroAction action in actions)
            {
                token.ThrowIfCancellationRequested();

                double targetTicks =
                    (action.Timestamp - firstTimestamp) / speed;

                // Release buttons at their scheduled times
                // without making later keyboard events wait.
                FlushPendingReleases(
                    clock,
                    targetTicks,
                    token,
                    ref leftHeld,
                    ref rightHeld,
                    ref leftReleasePending,
                    ref rightReleasePending,
                    ref leftReleaseTicks,
                    ref rightReleaseTicks);

                WaitUntil(
                    clock,
                    targetTicks,
                    token);

                FlushPendingReleases(
                    clock,
                    clock.ElapsedTicks,
                    token,
                    ref leftHeld,
                    ref rightHeld,
                    ref leftReleasePending,
                    ref rightReleasePending,
                    ref leftReleaseTicks,
                    ref rightReleaseTicks);

                token.ThrowIfCancellationRequested();

                switch (action.Type)
                {
                    case MacroActionType.MouseMove:
                        {
                            MoveAbsolute(action.X, action.Y);
                            break;
                        }

                    case MacroActionType.LeftDown:
                        {
                            MoveAbsolute(action.X, action.Y);

                            // Finish any previous click before
                            // beginning another press.
                            if (leftReleasePending)
                            {
                                WaitUntil(
                                    clock,
                                    leftReleaseTicks,
                                    token);

                                SendMouse(MOUSEEVENTF_LEFTUP);

                                leftHeld = false;
                                leftReleasePending = false;
                            }

                            if (!leftHeld)
                            {
                                SendMouse(MOUSEEVENTF_LEFTDOWN);

                                leftHeld = true;
                                leftDownTicks = clock.ElapsedTicks;
                            }

                            break;
                        }

                    case MacroActionType.LeftUp:
                        {
                            MoveAbsolute(action.X, action.Y);

                            if (leftHeld && !leftReleasePending)
                            {
                                double earliestRelease =
                                    leftDownTicks + minimumHoldTicks;

                                double releaseAt = Math.Max(
                                    clock.ElapsedTicks,
                                    earliestRelease);

                                if (releaseAt <= clock.ElapsedTicks)
                                {
                                    SendMouse(MOUSEEVENTF_LEFTUP);
                                    leftHeld = false;
                                }
                                else
                                {
                                    // Schedule the release instead of
                                    // blocking subsequent keyboard events.
                                    leftReleaseTicks = releaseAt;
                                    leftReleasePending = true;
                                }
                            }
                            else if (!leftHeld)
                            {
                                SendMouse(MOUSEEVENTF_LEFTUP);
                            }

                            break;
                        }

                    case MacroActionType.RightDown:
                        {
                            MoveAbsolute(action.X, action.Y);

                            if (rightReleasePending)
                            {
                                WaitUntil(
                                    clock,
                                    rightReleaseTicks,
                                    token);

                                SendMouse(MOUSEEVENTF_RIGHTUP);

                                rightHeld = false;
                                rightReleasePending = false;
                            }

                            if (!rightHeld)
                            {
                                SendMouse(MOUSEEVENTF_RIGHTDOWN);

                                rightHeld = true;
                                rightDownTicks = clock.ElapsedTicks;
                            }

                            break;
                        }

                    case MacroActionType.RightUp:
                        {
                            MoveAbsolute(action.X, action.Y);

                            if (rightHeld && !rightReleasePending)
                            {
                                double earliestRelease =
                                    rightDownTicks + minimumHoldTicks;

                                double releaseAt = Math.Max(
                                    clock.ElapsedTicks,
                                    earliestRelease);

                                if (releaseAt <= clock.ElapsedTicks)
                                {
                                    SendMouse(MOUSEEVENTF_RIGHTUP);
                                    rightHeld = false;
                                }
                                else
                                {
                                    rightReleaseTicks = releaseAt;
                                    rightReleasePending = true;
                                }
                            }
                            else if (!rightHeld)
                            {
                                SendMouse(MOUSEEVENTF_RIGHTUP);
                            }

                            break;
                        }

                    case MacroActionType.KeyDown:
                        {
                            SendKey(action, false);

                            heldKeys[action.VirtualKey] = action;
                            break;
                        }

                    case MacroActionType.KeyUp:
                        {
                            SendKey(action, true);

                            heldKeys.Remove(action.VirtualKey);
                            break;
                        }
                }
            }

            // Complete scheduled mouse releases after
            // the last recorded action.
            while (leftReleasePending || rightReleasePending)
            {
                token.ThrowIfCancellationRequested();

                double nextRelease = double.PositiveInfinity;

                if (leftReleasePending)
                {
                    nextRelease = Math.Min(
                        nextRelease,
                        leftReleaseTicks);
                }

                if (rightReleasePending)
                {
                    nextRelease = Math.Min(
                        nextRelease,
                        rightReleaseTicks);
                }

                FlushPendingReleases(
                    clock,
                    nextRelease,
                    token,
                    ref leftHeld,
                    ref rightHeld,
                    ref leftReleasePending,
                    ref rightReleasePending,
                    ref leftReleaseTicks,
                    ref rightReleaseTicks);
            }
        }
        finally
        {
            // Release any input still held on cancellation.

            if (leftHeld)
            {
                TryCleanup(() =>
                    SendMouse(MOUSEEVENTF_LEFTUP));
            }

            if (rightHeld)
            {
                TryCleanup(() =>
                    SendMouse(MOUSEEVENTF_RIGHTUP));
            }

            foreach (MacroAction key in heldKeys.Values)
            {
                TryCleanup(() =>
                    SendKey(key, true));
            }
        }
    }

    private static void FlushPendingReleases(
        Stopwatch clock,
        double untilTicks,
        CancellationToken token,
        ref bool leftHeld,
        ref bool rightHeld,
        ref bool leftReleasePending,
        ref bool rightReleasePending,
        ref double leftReleaseTicks,
        ref double rightReleaseTicks)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();

            bool releaseLeft =
                leftReleasePending &&
                leftReleaseTicks <= untilTicks;

            bool releaseRight =
                rightReleasePending &&
                rightReleaseTicks <= untilTicks;

            if (!releaseLeft && !releaseRight)
            {
                return;
            }

            // Process whichever release is due first.
            if (releaseLeft &&
                (!releaseRight ||
                 leftReleaseTicks <= rightReleaseTicks))
            {
                WaitUntil(
                    clock,
                    leftReleaseTicks,
                    token);

                SendMouse(MOUSEEVENTF_LEFTUP);

                leftHeld = false;
                leftReleasePending = false;
                leftReleaseTicks = 0;
            }
            else
            {
                WaitUntil(
                    clock,
                    rightReleaseTicks,
                    token);

                SendMouse(MOUSEEVENTF_RIGHTUP);

                rightHeld = false;
                rightReleasePending = false;
                rightReleaseTicks = 0;
            }
        }
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
                1000.0 / Stopwatch.Frequency;

            if (remainingMs <= 0)
            {
                return;
            }

            if (remainingMs > 3)
            {
                int sleep = Math.Max(
                    1,
                    (int)remainingMs - 2);

                if (token.WaitHandle.WaitOne(sleep))
                {
                    token.ThrowIfCancellationRequested();
                }
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

    private static void TryCleanup(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private static void MoveAbsolute(int x, int y)
    {
        int virtualLeft =
            GetSystemMetrics(SM_XVIRTUALSCREEN);

        int virtualTop =
            GetSystemMetrics(SM_YVIRTUALSCREEN);

        int virtualWidth =
            GetSystemMetrics(SM_CXVIRTUALSCREEN);

        int virtualHeight =
            GetSystemMetrics(SM_CYVIRTUALSCREEN);

        if (virtualWidth <= 0 || virtualHeight <= 0)
        {
            if (!SetCursorPos(x, y))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error());
            }

            return;
        }

        double normalizedX =
            ((double)x - virtualLeft) *
            65535.0 /
            Math.Max(1, virtualWidth - 1);

        double normalizedY =
            ((double)y - virtualTop) *
            65535.0 /
            Math.Max(1, virtualHeight - 1);

        int absoluteX = (int)Math.Clamp(
            Math.Round(normalizedX),
            0.0,
            65535.0);

        int absoluteY = (int)Math.Clamp(
            Math.Round(normalizedY),
            0.0,
            65535.0);

        Send(new INPUT
        {
            type = INPUT_MOUSE,

            data = new InputUnion
            {
                mouse = new MouseInput
                {
                    dx = absoluteX,
                    dy = absoluteY,
                    mouseData = 0,

                    flags =
                        MOUSEEVENTF_MOVE |
                        MOUSEEVENTF_ABSOLUTE |
                        MOUSEEVENTF_VIRTUALDESK,

                    time = 0,
                    extraInfo = IntPtr.Zero
                }
            }
        });
    }

    private static void SendMouse(uint flags)
    {
        Send(new INPUT
        {
            type = INPUT_MOUSE,

            data = new InputUnion
            {
                mouse = new MouseInput
                {
                    dx = 0,
                    dy = 0,
                    mouseData = 0,
                    flags = flags,
                    time = 0,
                    extraInfo = IntPtr.Zero
                }
            }
        });
    }

    private static void SendKey(
        MacroAction action,
        bool keyUp)
    {
        bool useScanCode =
            action.ScanCode != 0;

        uint flags =
            (keyUp ? KEYEVENTF_KEYUP : 0) |
            (action.Extended ? KEYEVENTF_EXTENDEDKEY : 0) |
            (useScanCode ? KEYEVENTF_SCANCODE : 0);

        Send(new INPUT
        {
            type = INPUT_KEYBOARD,

            data = new InputUnion
            {
                keyboard = new KeyboardInput
                {
                    virtualKey = useScanCode
                        ? (ushort)0
                        : (ushort)action.VirtualKey,

                    scanCode =
                        (ushort)action.ScanCode,

                    flags = flags,
                    time = 0,
                    extraInfo = IntPtr.Zero
                }
            }
        });
    }

    private static void Send(INPUT input)
    {
        INPUT[] inputs = { input };

        int expectedSize =
            IntPtr.Size == 8 ? 40 : 28;

        int actualSize =
            Marshal.SizeOf<INPUT>();

        if (actualSize != expectedSize)
        {
            throw new InvalidOperationException(
                $"Unexpected INPUT structure size. " +
                $"Expected {expectedSize} bytes, " +
                $"but got {actualSize} bytes.");
        }

        uint sent = SendInput(
            1,
            inputs,
            actualSize);

        if (sent != 1)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Windows could not send simulated input.");
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
        int x,
        int y);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(
        int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint count,
        [In] INPUT[] inputs,
        int size);
}
