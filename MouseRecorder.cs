
using System.Diagnostics;

namespace Mouse_Macro;

public sealed class MouseRecorder
{
    private readonly object _sync = new();
    private readonly Stopwatch _watch = new();
    private List<MacroAction> _actions = new();
    private readonly HashSet<int> _downKeys = new();
    private bool _recording;

    public RecordingMode Mode { get; private set; }
        = RecordingMode.Mouse;

    public bool IsRecording
    {
        get { lock (_sync) return _recording; }
    }

    public TimeSpan Elapsed
    {
        get { lock (_sync) return _watch.Elapsed; }
    }

    public void Start(RecordingMode mode)
    {
        lock (_sync)
        {
            _actions.Clear();
            _downKeys.Clear();
            Mode = mode;
            _watch.Restart();
            _recording = true;
        }
    }

    public void RecordMouse(
        MacroActionType type, int x, int y)
    {
        lock (_sync)
        {
            if (!_recording) return;

            _actions.Add(new MacroAction
            {
                Type = type,
                X = x,
                Y = y,
                Timestamp = _watch.ElapsedTicks
            });
        }
    }

    public void RecordKey(
        bool down, int key, int scan, bool extended)
    {
        lock (_sync)
        {
            if (!_recording ||
                Mode != RecordingMode.KeyboardAndMouse)
                return;

            if (down)
            {
                if (!_downKeys.Add(key)) return;
            }
            else if (!_downKeys.Remove(key))
            {
                return;
            }

            _actions.Add(new MacroAction
            {
                Type = down
                    ? MacroActionType.KeyDown
                    : MacroActionType.KeyUp,
                VirtualKey = key,
                ScanCode = scan,
                Extended = extended,
                Timestamp = _watch.ElapsedTicks
            });
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (!_recording) return;

            _recording = false;
            _watch.Stop();

            // Remove key-down events without a
            // matching key-up event.
            var unmatched =
                new Dictionary<int, Stack<int>>();

            for (int i = 0; i < _actions.Count; i++)
            {
                MacroAction a = _actions[i];

                if (a.Type == MacroActionType.KeyDown)
                {
                    if (!unmatched.TryGetValue(
                        a.VirtualKey, out var stack))
                    {
                        stack = new Stack<int>();
                        unmatched[a.VirtualKey] = stack;
                    }

                    stack.Push(i);
                }
                else if (a.Type == MacroActionType.KeyUp &&
                         unmatched.TryGetValue(
                             a.VirtualKey, out var stack) &&
                         stack.Count > 0)
                {
                    stack.Pop();
                }
            }

            HashSet<int> remove = new();

            foreach (var stack in unmatched.Values)
                foreach (int index in stack)
                    remove.Add(index);

            _actions = _actions
                .Where((_, i) => !remove.Contains(i))
                .ToList();

            _downKeys.Clear();
        }
    }

    public List<MacroAction> GetActions()
    {
        lock (_sync)
            return _actions.Select(Clone).ToList();
    }

    public void SetActions(List<MacroAction> actions)
    {
        lock (_sync)
        {
            if (_recording)
                throw new InvalidOperationException(
                    "Stop recording first.");

            _actions = actions.Select(Clone).ToList();
            _watch.Reset();
        }
    }

    public static MacroAction Clone(MacroAction a) => new()
    {
        Type = a.Type,
        X = a.X,
        Y = a.Y,
        VirtualKey = a.VirtualKey,
        ScanCode = a.ScanCode,
        Extended = a.Extended,
        Timestamp = a.Timestamp
    };
}
