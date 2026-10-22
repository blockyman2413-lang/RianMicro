
namespace Mouse_Macro;

public enum MouseActionType
{
    Move,
    LeftDown,
    LeftUp,
    RightDown,
    RightUp
}

public sealed class MouseAction
{
    public MouseActionType Type { get; set; }
    public int X { get; set; }
    public int Y { get; set; }

    // Time measured in Stopwatch ticks.
    public long Timestamp { get; set; }
}
