
using System.Diagnostics;
using System.Text.Json;

namespace Mouse_Macro;

public static class MacroStorage
{
    private sealed class MacroFile
    {
        public string Format { get; set; } = "RianMacro";
        public int Version { get; set; } = 2;
        public long Frequency { get; set; }
        public RecordingMode Mode { get; set; }
        public List<MacroAction> Actions { get; set; } = new();
    }

    private sealed class LegacyFile
    {
        public string? Format { get; set; }
        public int Version { get; set; }
        public long Frequency { get; set; }
        public List<LegacyAction>? Actions { get; set; }
    }

    private sealed class LegacyAction
    {
        public int Type { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public long Timestamp { get; set; }
    }

    private static readonly JsonSerializerOptions Options =
        new() { WriteIndented = true };

    public static void Save(
        string path,
        List<MacroAction> actions,
        RecordingMode mode)
    {
        MacroFile file = new()
        {
            Frequency = Stopwatch.Frequency,
            Mode = mode,
            Actions = actions
        };

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(file, Options));
    }

    public static (
        List<MacroAction> Actions,
        RecordingMode Mode) Load(string path)
    {
        using JsonDocument document =
            JsonDocument.Parse(File.ReadAllText(path));

        JsonElement root = document.RootElement;

        if (!root.TryGetProperty(
            "Version", out JsonElement versionValue))
        {
            throw new InvalidDataException(
                "Missing file version.");
        }

        int version = versionValue.GetInt32();
        string json = root.GetRawText();

        List<MacroAction> actions;
        long frequency;
        RecordingMode mode;

        if (version == 1)
        {
            LegacyFile legacy =
                JsonSerializer.Deserialize<LegacyFile>(json)
                ?? throw new InvalidDataException(
                    "Invalid recording.");

            if (legacy.Format != "RianMacro" ||
                legacy.Actions == null)
            {
                throw new InvalidDataException(
                    "Not a Rian Macro recording.");
            }

            frequency = legacy.Frequency;
            mode = RecordingMode.Mouse;

            actions = legacy.Actions.Select(a =>
                new MacroAction
                {
                    Type = a.Type switch
                    {
                        0 => MacroActionType.MouseMove,
                        1 => MacroActionType.LeftDown,
                        2 => MacroActionType.LeftUp,
                        3 => MacroActionType.RightDown,
                        4 => MacroActionType.RightUp,
                        _ => throw new InvalidDataException(
                            "Unknown mouse action.")
                    },
                    X = a.X,
                    Y = a.Y,
                    Timestamp = a.Timestamp
                }).ToList();
        }
        else if (version == 2)
        {
            MacroFile file =
                JsonSerializer.Deserialize<MacroFile>(json)
                ?? throw new InvalidDataException(
                    "Invalid recording.");

            if (file.Format != "RianMacro" ||
                file.Actions == null ||
                !Enum.IsDefined(file.Mode))
            {
                throw new InvalidDataException(
                    "Unsupported recording.");
            }

            frequency = file.Frequency;
            mode = file.Mode;
            actions = file.Actions;
        }
        else
        {
            throw new InvalidDataException(
                "Unsupported Rian Macro version.");
        }

        if (frequency <= 0 ||
            actions.Count > 2_000_000)
        {
            throw new InvalidDataException(
                "Invalid recording size or timing.");
        }

        long previous = -1;

        foreach (MacroAction a in actions)
        {
            if (a == null ||
                !Enum.IsDefined(a.Type) ||
                a.Timestamp < 0 ||
                a.Timestamp < previous ||
                a.VirtualKey < 0 ||
                a.VirtualKey > 255 ||
                a.ScanCode < 0 ||
                a.ScanCode > 65535)
            {
                throw new InvalidDataException(
                    "Invalid recording action.");
            }

            previous = a.Timestamp;
        }

        List<MacroAction> converted =
            actions.Select(a =>
            {
                MacroAction copy = MouseRecorder.Clone(a);

                copy.Timestamp = checked((long)Math.Round(
                    a.Timestamp *
                    ((double)Stopwatch.Frequency /
                     frequency)));

                return copy;
            }).ToList();

        return (converted, mode);
    }

    public static List<MacroAction> Append(
        List<MacroAction> existing,
        List<MacroAction> incoming)
    {
        List<MacroAction> result =
            existing.Select(MouseRecorder.Clone).ToList();

        if (incoming.Count == 0)
            return result;

        if (existing.Count == 0)
            return incoming.Select(
                MouseRecorder.Clone).ToList();

        long gap = (long)(
            Stopwatch.Frequency * 0.3);

        long offset = checked(
            existing[^1].Timestamp +
            gap -
            incoming[0].Timestamp);

        foreach (MacroAction a in incoming)
        {
            MacroAction copy = MouseRecorder.Clone(a);

            copy.Timestamp = checked(
                copy.Timestamp + offset);

            result.Add(copy);
        }

        return result;
    }
}
