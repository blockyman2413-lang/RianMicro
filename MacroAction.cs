
namespace Mouse_Macro;

public enum MacroActionType
{
    MouseMove,
    LeftDown,
    LeftUp,
    RightDown,
    RightUp,
    KeyDown,
    KeyUp
}

public enum RecordingMode
{
    Mouse,
    KeyboardAndMouse
}

public sealed class MacroAction
{
    public MacroActionType Type { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int VirtualKey { get; set; }
    public int ScanCode { get; set; }
    public bool Extended { get; set; }
    public long Timestamp { get; set; }
}
