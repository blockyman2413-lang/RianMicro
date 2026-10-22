using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace Mouse_Macro;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        ApplicationConfiguration.Initialize();
        Application.Run(new MacroWindow());
    }
}
public sealed class MacroWindow : Form
{
    private readonly MouseRecorder _recorder = new();
    private readonly MouseHook _mouseHook = new();
    private readonly KeyboardHook _keyboardHook = new();
    private readonly MousePlayer _player = new();
    private readonly TabControl _tabs = new();
    private readonly Label _timerLabel = new();
    private readonly Label _statusLabel = new();
    private readonly NumericUpDown _loopCount = new();
    private readonly CheckBox _unlimited = new();
    private readonly ComboBox _speed = new();
    private readonly ComboBox _recordShortcut = new();
    private readonly ComboBox _playShortcut = new();
    private readonly Button _recordButton = MakeButton("Record");
    private readonly Button _playButton = MakeButton("Play");
    private readonly Button _stopButton = MakeButton("Stop");
    private readonly Button _saveButton = MakeButton("Save");
    private readonly Button _openButton = MakeButton("Open");
    private readonly Button _insertButton = MakeButton("Insert");
    private readonly CheckBox _customSizeCheckBox = new();
    private readonly ToolTip _customSizeToolTip = new();
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer;
    private bool _closing;
    private bool _preparing;
    private bool _cancelPreparation;
    private bool _changingShortcuts;
    private bool _loadingWindowSettings;
    private bool _customSizeEnabled;
    private bool _applyingWindowSize;
    private Size _customWindowClientSize = new(640, 450);
    private readonly Size _compactClientSize = new(640, 450);
    private RecordingMode _currentMode = RecordingMode.Mouse;
    private static readonly string SettingsDirectory =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Rian Macro");
    private static readonly string ShortcutSettingsPath =
        Path.Combine(SettingsDirectory, "shortcuts.json");
    private static readonly string WindowSettingsPath =
        Path.Combine(SettingsDirectory, "window-settings.json");
    public MacroWindow()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Text = "RianMacro";
        ClientSize = _compactClientSize;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        Icon = Icon.ExtractAssociatedIcon(
            Application.ExecutablePath) ?? SystemIcons.Application;
        LoadWindowSettings();
        Shown += (_, _) =>
        {
            ApplyWindowSizeMode();
            FitWindowToScreen();
        };
        DpiChanged += (_, _) =>
        {
            if (IsHandleCreated)
                BeginInvoke((Action)FitWindowToScreen);
        };
        _tabs.Dock = DockStyle.Fill;
        _tabs.SizeMode = TabSizeMode.Fixed;
        _tabs.ItemSize = new Size(310, 28);
        _tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        _tabs.Margin = new Padding(0);
        _tabs.DrawItem += (_, e) =>
        {
            bool selected = e.Index == _tabs.SelectedIndex;
            using var brush = new SolidBrush(
                selected
                    ? Color.White
                    : Color.FromArgb(235, 235, 235));
            e.Graphics.FillRectangle(brush, e.Bounds);
            TextRenderer.DrawText(
                e.Graphics,
                _tabs.TabPages[e.Index].Text,
                _tabs.Font,
                e.Bounds,
                selected ? Color.Black : Color.DimGray,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter);
        };
        _tabs.TabPages.Add(new TabPage("Mouse"));
        _tabs.TabPages.Add(new TabPage("Keyboard & Mouse"));
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            _currentMode = _tabs.SelectedIndex == 0
                ? RecordingMode.Mouse
                : RecordingMode.KeyboardAndMouse;
            _statusLabel.Text =
                _currentMode == RecordingMode.Mouse
                    ? "Mouse mode ready"
                    : "Keyboard & Mouse mode ready";
        };
        _timerLabel.Text = "00:00:00.000";
        _timerLabel.Dock = DockStyle.Fill;
        _timerLabel.TextAlign = ContentAlignment.MiddleCenter;
        _timerLabel.Font = new Font(
            "Segoe UI", 22, FontStyle.Bold);
        _statusLabel.Text = "Ready";
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleCenter;
        _loopCount.Minimum = 1;
        _loopCount.Maximum = 100000;
        _loopCount.Value = 1;
        _loopCount.Width = 85;
        _unlimited.Text = "Unlimited";
        _unlimited.AutoSize = true;
        _unlimited.CheckedChanged += (_, _) =>
        {
            _loopCount.Enabled =
                !_unlimited.Checked && !IsBusy;
        };
        _speed.DropDownStyle = ComboBoxStyle.DropDownList;
        _speed.Width = 100;
        _speed.Items.AddRange(new object[]
        {
            "0.5x", "1.0x", "1.5x",
            "2.0x", "3.0x", "4.0x"
        });
        _speed.SelectedIndex = 1;
        ConfigureShortcutSelector(_recordShortcut);
        ConfigureShortcutSelector(_playShortcut);
        LoadShortcutSettings();
        _recordShortcut.SelectedIndexChanged += (_, _) =>
            ChangeShortcuts(_recordShortcut);
        _playShortcut.SelectedIndexChanged += (_, _) =>
            ChangeShortcuts(_playShortcut);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 6,
            Margin = new Padding(0)
        };
        layout.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 66));
        layout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 58));
        layout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 55));
        layout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 65));
        layout.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 68));
        var timerArea = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        timerArea.Controls.Add(_timerLabel);
        layout.Controls.Add(timerArea, 0, 0);
        layout.Controls.Add(_statusLabel, 0, 1);
        var customSizePanel = new Panel
        {
            Size = new Size(137, 31),
            Location = new Point(0, 2),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(245, 245, 245),
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _customSizeCheckBox.Text = "Custom Size";
        _customSizeCheckBox.AutoSize = false;
        _customSizeCheckBox.Size = new Size(130, 27);
        _customSizeCheckBox.Location = new Point(4, 1);
        _customSizeCheckBox.TextAlign = ContentAlignment.MiddleLeft;
        _customSizeCheckBox.CheckAlign = ContentAlignment.MiddleLeft;
        _customSizeCheckBox.Cursor = Cursors.Hand;
        _customSizeCheckBox.BackColor = Color.Transparent;
        _customSizeCheckBox.Checked = _customSizeEnabled;
        _customSizeToolTip.AutoPopDelay = 5000;
        _customSizeToolTip.InitialDelay = 400;
        _customSizeToolTip.ReshowDelay = 100;
        _customSizeToolTip.ShowAlways = true;
        _customSizeToolTip.SetToolTip(
            _customSizeCheckBox,
            "click to custom size");
        _customSizeToolTip.SetToolTip(
            customSizePanel,
            "click to custom size");
        _customSizeCheckBox.CheckedChanged += (_, _) =>
        {
            if (_loadingWindowSettings)
                return;
            _customSizeEnabled = _customSizeCheckBox.Checked;
            ApplyWindowSizeMode();
            SaveWindowSettings();
        };
        customSizePanel.Controls.Add(_customSizeCheckBox);
        timerArea.Controls.Add(customSizePanel);
        customSizePanel.BringToFront();
        var settings = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = true,
            AutoScroll = true
        };
        settings.Controls.Add(new Label
        {
            Text = "Loops:",
            AutoSize = true,
            Margin = new Padding(5, 9, 4, 0)
        });
        settings.Controls.Add(_loopCount);
        settings.Controls.Add(_unlimited);
        settings.Controls.Add(new Label
        {
            Text = "Speed:",
            AutoSize = true,
            Margin = new Padding(20, 9, 4, 0)
        });
        settings.Controls.Add(_speed);
        layout.Controls.Add(settings, 0, 2);
        var files = ThreeColumns();
        files.Controls.Add(_saveButton, 0, 0);
        files.Controls.Add(_openButton, 1, 0);
        files.Controls.Add(_insertButton, 2, 0);
        layout.Controls.Add(files, 0, 3);
        var controls = ThreeColumns();
        controls.Controls.Add(_recordButton, 0, 0);
        controls.Controls.Add(_playButton, 1, 0);
        controls.Controls.Add(_stopButton, 2, 0);
        layout.Controls.Add(controls, 0, 4);
        var shortcutArea = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(5, 2, 5, 2),
            BorderStyle = BorderStyle.FixedSingle
        };
        var shortcutPanel = new TableLayoutPanel
        {
            Dock = DockStyle.None,
            ColumnCount = 3,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0),
            GrowStyle = TableLayoutPanelGrowStyle.FixedSize
        };
        shortcutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
        shortcutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 75));
        shortcutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shortcutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        shortcutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        var modifierLabel = new Label
        {
            Text = "Ctrl + Shift + Alt +",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(0)
        };
        var recordLabel = new Label
        {
            Text = "Record -",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Margin = new Padding(0)
        };
        var playLabel = new Label
        {
            Text = "Play -",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Margin = new Padding(0)
        };
        _recordShortcut.Dock = DockStyle.Fill;
        _recordShortcut.Margin = new Padding(5, 1, 5, 1);
        _playShortcut.Dock = DockStyle.Fill;
        _playShortcut.Margin = new Padding(5, 1, 5, 1);
        shortcutPanel.Controls.Add(modifierLabel, 0, 0);
        shortcutPanel.SetRowSpan(modifierLabel, 2);
        shortcutPanel.Controls.Add(recordLabel, 1, 0);
        shortcutPanel.Controls.Add(_recordShortcut, 2, 0);
        shortcutPanel.Controls.Add(playLabel, 1, 1);
        shortcutPanel.Controls.Add(_playShortcut, 2, 1);
        shortcutArea.Controls.Add(shortcutPanel);
        void CenterShortcutPanel()
        {
            int rowHeight = LogicalToDeviceUnits(30);
            int groupHeight = rowHeight * 2;
            shortcutPanel.RowStyles[0].Height = rowHeight;
            shortcutPanel.RowStyles[1].Height = rowHeight;
            shortcutPanel.SetBounds(
                0,
                Math.Max(0, (shortcutArea.ClientSize.Height - groupHeight) / 2),
                shortcutArea.ClientSize.Width,
                groupHeight);
        }
        shortcutArea.SizeChanged += (_, _) => CenterShortcutPanel();
        DpiChanged += (_, _) =>
        {
            if (IsHandleCreated)
                BeginInvoke((Action)CenterShortcutPanel);
        };
        CenterShortcutPanel();
        layout.Controls.Add(shortcutArea, 0, 5);
        var infoButton = new Button
        {
            Text = "",
            Size = new Size(30, 30),
            Location = new Point(0, 2),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            TabStop = false,
            BackColor = SystemColors.Control,
            UseVisualStyleBackColor = false,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        infoButton.FlatAppearance.BorderSize = 0;
        infoButton.FlatAppearance.MouseOverBackColor =
            SystemColors.Control;
        infoButton.FlatAppearance.MouseDownBackColor =
            SystemColors.Control;
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(
                0,
                0,
                infoButton.Width - 1,
                infoButton.Height - 1);
            infoButton.Region = new Region(path);
        }
        infoButton.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Color.DimGray, 1.5f);
            e.Graphics.DrawEllipse(
                pen,
                1.5f,
                1.5f,
                infoButton.Width - 3.5f,
                infoButton.Height - 3.5f);
            using var font = new Font(
                "Segoe UI",
                17,
                FontStyle.Bold,
                GraphicsUnit.Pixel);
            using var brush = new SolidBrush(Color.Black);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            e.Graphics.DrawString(
                "i",
                font,
                brush,
                new RectangleF(
                    0,
                    -1,
                    infoButton.Width,
                    infoButton.Height),
                format);
        };
        infoButton.Click += (_, _) => ShowCopyrightWindow();
        timerArea.Controls.Add(infoButton);
        void PositionInfoButton()
        {
            infoButton.Location = new Point(
                Math.Max(
                    0,
                    timerArea.ClientSize.Width -
                    infoButton.Width - 2),
                2);
        }
        timerArea.Resize += (_, _) => PositionInfoButton();
        PositionInfoButton();
        infoButton.BringToFront();
        customSizePanel.BringToFront();
        var container = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        container.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100));
        container.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 48));
        container.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));
        container.Controls.Add(_tabs, 0, 0);
        container.Controls.Add(layout, 0, 1);
        var viewport = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        container.Dock = DockStyle.None;
        viewport.Controls.Add(container);
        Controls.Add(viewport);
        void FitContentToViewport()
        {
            int minimumWidth = LogicalToDeviceUnits(640);
            int minimumHeight = LogicalToDeviceUnits(420);
            container.Size = new Size(
                Math.Max(minimumWidth, viewport.ClientSize.Width),
                Math.Max(minimumHeight, viewport.ClientSize.Height));
        }
        viewport.ClientSizeChanged += (_, _) =>
            FitContentToViewport();
        Shown += (_, _) => FitContentToViewport();
        DpiChanged += (_, _) =>
        {
            if (IsHandleCreated)
                BeginInvoke((Action)FitContentToViewport);
        };
        FitContentToViewport();
        bool sizingTabs = false;
        void FitTabs()
        {
            if (sizingTabs)
                return;
            sizingTabs = true;
            try
            {
                int each = Math.Max(
                    1,
                    (container.ClientSize.Width - 8) / 2);
                if (_tabs.ItemSize.Width != each)
                {
                    _tabs.ItemSize = new Size(each, 28);
                }
            }
            finally
            {
                sizingTabs = false;
            }
        }
        container.SizeChanged += (_, _) => FitTabs();
        Shown += (_, _) => FitTabs();
        ResizeEnd += (_, _) =>
        {
            if (_customSizeEnabled &&
                !_applyingWindowSize &&
                WindowState == FormWindowState.Normal)
            {
                _customWindowClientSize = ClientSize;
                SaveWindowSettings();
            }
        };
        SizeChanged += (_, _) =>
        {
            if (_closing ||
                !_customSizeEnabled ||
                _applyingWindowSize ||
                WindowState != FormWindowState.Normal)
            {
                return;
            }
            _customWindowClientSize = ClientSize;
        };
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add(
            "Show Rian Macro",
            null,
            (_, _) => RestoreWindow());
        trayMenu.Items.Add(
            "Stop",
            null,
            (_, _) => StopAll());
        trayMenu.Items.Add(
            "Exit",
            null,
            (_, _) => Close());
        _tray = new NotifyIcon
        {
            Icon = Icon,
            Text = "Rian Macro",
            ContextMenuStrip = trayMenu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => RestoreWindow();
        _timer = new System.Windows.Forms.Timer
        {
            Interval = 30
        };
        _timer.Tick += (_, _) =>
        {
            if (_recorder.IsRecording)
                _timerLabel.Text = FormatTime(_recorder.Elapsed);
        };
        _recordButton.Click += (_, _) => ToggleRecording();
        _playButton.Click += async (_, _) =>
            await StartPlaybackAsync();
        _stopButton.Click += (_, _) => StopAll();
        _saveButton.Click += (_, _) => SaveRecording();
        _openButton.Click += (_, _) => OpenRecording(false);
        _insertButton.Click += (_, _) => OpenRecording(true);
        _mouseHook.MouseActionDetected += (type, x, y) =>
            _recorder.RecordMouse(type, x, y);
        _keyboardHook.KeyEventDetected +=
            (down, key, scan, extended) =>
                _recorder.RecordKey(
                    down,
                    key,
                    scan,
                    extended);
        _keyboardHook.PhysicalKeyPressed += () =>
        {
            if (_preparing)
                _cancelPreparation = true;
            if (_player.IsPlaying)
                _player.Stop();
        };
        _keyboardHook.RecordShortcutPressed += () =>
        {
            if (!_preparing && !_player.IsPlaying)
                ToggleRecording();
        };
        _keyboardHook.PlayShortcutPressed += async () =>
            await StartPlaybackAsync();
        try
        {
            _keyboardHook.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Global keyboard shortcuts could not start:\n" +
                ex.Message,
                "Rian Macro",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        UpdateButtons();
    }
    private sealed class WindowSettings
    {
        public bool CustomSizeEnabled { get; set; }
        public int Width { get; set; } = 640;
        public int Height { get; set; } = 450;
    }
    private void LoadWindowSettings()
    {
        _loadingWindowSettings = true;
        try
        {
            _customSizeEnabled = false;
            _customWindowClientSize = new Size(640, 450);
            if (!File.Exists(WindowSettingsPath))
                return;
            string json = File.ReadAllText(WindowSettingsPath);
            WindowSettings? saved =
                JsonSerializer.Deserialize<WindowSettings>(json);
            if (saved is null)
                return;
            _customSizeEnabled = saved.CustomSizeEnabled;
            int width = Math.Max(640, saved.Width);
            int height = Math.Max(450, saved.Height);
            _customWindowClientSize = new Size(width, height);
        }
        catch
        {
            _customSizeEnabled = false;
            _customWindowClientSize = new Size(640, 450);
        }
        finally
        {
            _loadingWindowSettings = false;
        }
    }
    private void SaveWindowSettings()
    {
        if (_closing)
            return;
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var settings = new WindowSettings
            {
                CustomSizeEnabled = _customSizeEnabled,
                Width = _customWindowClientSize.Width,
                Height = _customWindowClientSize.Height
            };
            string json = JsonSerializer.Serialize(
                settings,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });
            File.WriteAllText(WindowSettingsPath, json);
        }
        catch
        {
        }
    }
    private void ApplyWindowSizeMode()
    {
        if (_applyingWindowSize)
            return;
        _applyingWindowSize = true;
        try
        {
            SuspendLayout();
            if (WindowState != FormWindowState.Normal)
                WindowState = FormWindowState.Normal;
            if (_customSizeEnabled)
            {
                FormBorderStyle = FormBorderStyle.Sizable;
                MaximizeBox = true;
                MinimumSize = new Size(
                    LogicalToDeviceUnits(640),
                    LogicalToDeviceUnits(450));
                MaximumSize = Size.Empty;
                ClientSize = new Size(
                    Math.Max(
                        _compactClientSize.Width,
                        _customWindowClientSize.Width),
                    Math.Max(
                        _compactClientSize.Height,
                        _customWindowClientSize.Height));
            }
            else
            {
                FormBorderStyle = FormBorderStyle.FixedSingle;
                MaximizeBox = false;
                MinimumSize = Size.Empty;
                MaximumSize = Size.Empty;
                ClientSize = _compactClientSize;
            }
            FitWindowToScreen();
        }
        finally
        {
            ResumeLayout(true);
            _applyingWindowSize = false;
        }
    }
    private void FitWindowToScreen()
    {
        Rectangle area = Screen.FromControl(this).WorkingArea;
        Size maximum = new Size(
            Math.Max(240, area.Width - 16),
            Math.Max(200, area.Height - 16));
        if (Width > maximum.Width || Height > maximum.Height)
        {
            Size = new Size(
                Math.Min(Width, maximum.Width),
                Math.Min(Height, maximum.Height));
        }
        int x = Math.Clamp(
            Left,
            area.Left,
            Math.Max(area.Left, area.Right - Width));
        int y = Math.Clamp(
            Top,
            area.Top,
            Math.Max(area.Top, area.Bottom - Height));
        Location = new Point(x, y);
    }
    private void ShowCopyrightWindow()
    {
        using var copyrightWindow = new Form
        {
            Text = "RianMacro Copyright",
            ClientSize = new Size(370, 215),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent,
            Icon = Icon
        };
        var titleLabel = new Label
        {
            Text = "RianMacro",
            Font = new Font("Segoe UI", 17, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(20, 24),
            Size = new Size(330, 40)
        };
        var copyrightLabel = new Label
        {
            Text = "Copyright © 2026 RIAN",
            Font = new Font("Segoe UI", 10, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(20, 82),
            Size = new Size(330, 27)
        };
        var rightsLabel = new Label
        {
            Text = "All rights reserved.",
            Font = new Font("Segoe UI", 10, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(20, 111),
            Size = new Size(330, 27)
        };
        var okButton = new Button
        {
            Text = "OK",
            Size = new Size(95, 31),
            Location = new Point(255, 165),
            DialogResult = DialogResult.OK
        };
        copyrightWindow.Controls.Add(titleLabel);
        copyrightWindow.Controls.Add(copyrightLabel);
        copyrightWindow.Controls.Add(rightsLabel);
        copyrightWindow.Controls.Add(okButton);
        copyrightWindow.AcceptButton = okButton;
        copyrightWindow.ShowDialog(this);
    }
    private static void ConfigureShortcutSelector(ComboBox combo)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.Width = 72;
        foreach (char c in
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")
        {
            combo.Items.Add(c.ToString());
        }
        for (int i = 1; i <= 12; i++)
            combo.Items.Add($"F{i}");
    }
    private static Keys ShortcutKey(string text)
    {
        if (text.Length == 1 && char.IsLetter(text[0]))
            return Enum.Parse<Keys>(text);
        if (text.Length == 1 && char.IsDigit(text[0]))
        {
            return (Keys)(
                (int)Keys.D0 + (text[0] - '0'));
        }
        return Enum.Parse<Keys>(text);
    }
    private static string ShortcutText(Keys key)
    {
        if (key >= Keys.D0 && key <= Keys.D9)
        {
            return ((int)key - (int)Keys.D0).ToString();
        }
        return key.ToString();
    }
    private void LoadShortcutSettings()
    {
        string record = "R";
        string play = "P";
        try
        {
            if (File.Exists(ShortcutSettingsPath))
            {
                var saved =
                    JsonSerializer.Deserialize<ShortcutSettings>(
                        File.ReadAllText(ShortcutSettingsPath));
                if (saved is not null)
                {
                    record = saved.Record;
                    play = saved.Play;
                }
            }
        }
        catch
        {
        }
        if (!_recordShortcut.Items.Contains(record))
            record = "R";
        if (!_playShortcut.Items.Contains(play))
            play = "P";
        if (record == play)
            play = record == "P" ? "R" : "P";
        _recordShortcut.SelectedItem = record;
        _playShortcut.SelectedItem = play;
        _keyboardHook.RecordKey = ShortcutKey(record);
        _keyboardHook.PlayKey = ShortcutKey(play);
    }
    private void ChangeShortcuts(ComboBox changed)
    {
        if (_changingShortcuts)
            return;
        if (_recordShortcut.SelectedItem is not string record ||
            _playShortcut.SelectedItem is not string play)
        {
            return;
        }
        if (record == play)
        {
            MessageBox.Show(
                "Record and Play must use different keys.",
                "Rian Macro",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            _changingShortcuts = true;
            try
            {
                changed.SelectedItem =
                    changed == _recordShortcut
                        ? ShortcutText(_keyboardHook.RecordKey)
                        : ShortcutText(_keyboardHook.PlayKey);
            }
            finally
            {
                _changingShortcuts = false;
            }
            return;
        }
        _keyboardHook.RecordKey = ShortcutKey(record);
        _keyboardHook.PlayKey = ShortcutKey(play);
        try
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(ShortcutSettingsPath)!);
            var settings = new ShortcutSettings(record, play);
            File.WriteAllText(
                ShortcutSettingsPath,
                JsonSerializer.Serialize(settings));
        }
        catch (Exception ex)
        {
            _statusLabel.Text =
                "Could not save shortcuts: " + ex.Message;
        }
    }
    private sealed record ShortcutSettings(
        string Record,
        string Play);
    private bool IsBusy =>
        _recorder.IsRecording ||
        _preparing ||
        _player.IsPlaying;
    private static Button MakeButton(string text)
    {
        return new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(4)
        };
    }
    private static TableLayoutPanel ThreeColumns()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1
        };
        panel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 33.33f));
        panel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 33.33f));
        panel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 33.34f));
        panel.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));
        return panel;
    }
    private static string FormatTime(TimeSpan t)
    {
        return
            $"{(int)t.TotalHours:00}:" +
            $"{t.Minutes:00}:" +
            $"{t.Seconds:00}." +
            $"{t.Milliseconds:000}";
    }
    private void ToggleRecording()
    {
        if (_closing || _preparing || _player.IsPlaying)
            return;
        if (_recorder.IsRecording)
        {
            StopRecording();
            RestoreWindow();
        }
        else
        {
            StartRecording();
        }
    }
    private void StartRecording()
    {
        try
        {
            _recorder.Start(_currentMode);
            _mouseHook.Start();
            _timerLabel.Text = "00:00:00.000";
            _statusLabel.Text =
                _currentMode == RecordingMode.Mouse
                    ? "Recording mouse..."
                    : "Recording keyboard & mouse...";
            _timer.Start();
            UpdateButtons();
            SendWindowToBack();
        }
        catch (Exception ex)
        {
            _mouseHook.Stop();
            _recorder.Stop();
            _timer.Stop();
            MessageBox.Show(
                "Could not start recording:\n" + ex.Message,
                "Rian Macro",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            UpdateButtons();
        }
    }
    private void SendWindowToBack()
    {
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOACTIVATE = 0x0010;
        SetWindowPos(
            Handle,
            new IntPtr(1),
            0,
            0,
            0,
            0,
            SWP_NOSIZE |
            SWP_NOMOVE |
            SWP_NOACTIVATE);
    }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int X,
        int Y,
        int cx,
        int cy,
        uint uFlags);
    private void StopRecording()
    {
        _mouseHook.Stop();
        _recorder.Stop();
        _timer.Stop();
        _timerLabel.Text = FormatTime(_recorder.Elapsed);
        _statusLabel.Text =
            $"Recorded {_recorder.GetActions().Count} actions.";
        UpdateButtons();
    }
    private async Task StartPlaybackAsync()
    {
        if (_closing || IsBusy)
            return;
        var actions = _recorder.GetActions();
        if (actions.Count == 0)
        {
            _statusLabel.Text = "Record or open a macro first.";
            return;
        }
        int loops = (int)_loopCount.Value;
        bool unlimited = _unlimited.Checked;
        double speed = double.Parse(
            _speed.SelectedItem!
                .ToString()!
                .Replace("x", ""),
            CultureInfo.InvariantCulture);
        _preparing = true;
        _cancelPreparation = false;
        _statusLabel.Text = "Preparing playback...";
        UpdateButtons();
        Hide();
        try
        {
            await Task.Delay(250);
            if (_closing || _cancelPreparation)
            {
                if (!_closing)
                    _statusLabel.Text = "Playback cancelled.";
                return;
            }
            _preparing = false;
            _statusLabel.Text = "Playing...";
            UpdateButtons();
            await _player.PlayAsync(
                actions,
                loops,
                speed,
                unlimited);
            if (!_closing)
            {
                _statusLabel.Text =
                    "Playback stopped / finished.";
            }
        }
        catch (Exception ex)
        {
            if (!_closing)
            {
                _statusLabel.Text = "Playback error.";
                MessageBox.Show(
                    ex.Message,
                    "Rian Macro",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
        finally
        {
            _preparing = false;
            if (!_closing)
            {
                UpdateButtons();
                RestoreWindow();
            }
        }
    }
    private void StopAll()
    {
        if (_closing)
            return;
        if (_recorder.IsRecording)
        {
            StopRecording();
            RestoreWindow();
        }
        if (_preparing)
        {
            _cancelPreparation = true;
            _statusLabel.Text = "Cancelling playback...";
        }
        if (_player.IsPlaying)
        {
            _player.Stop();
            _statusLabel.Text = "Stopping playback...";
        }
        UpdateButtons();
    }
    private void SaveRecording()
    {
        if (IsBusy)
            return;
        var actions = _recorder.GetActions();
        if (actions.Count == 0)
        {
            MessageBox.Show("There is no recording to save.");
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Title = "Save Rian Macro recording",
            Filter = "Rian Macro files (*.rian)|*.rian",
            DefaultExt = "rian",
            AddExtension = true,
            FileName = "My Recording.rian"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            RecordingMode mode =
                actions.Any(a =>
                    a.Type is MacroActionType.KeyDown
                    or MacroActionType.KeyUp)
                ? RecordingMode.KeyboardAndMouse
                : _currentMode;
            MacroStorage.Save(
                dialog.FileName,
                actions,
                mode);
            _statusLabel.Text = "Recording saved.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Could not save recording:\n" + ex.Message);
        }
    }
    private void OpenRecording(bool insert)
    {
        if (IsBusy)
            return;
        using var dialog = new OpenFileDialog
        {
            Title = insert
                ? "Insert Rian Macro recording"
                : "Open Rian Macro recording",
            Filter = "Rian Macro files (*.rian)|*.rian",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            var (loaded, mode) =
                MacroStorage.Load(dialog.FileName);
            var combined = insert
                ? MacroStorage.Append(
                    _recorder.GetActions(),
                    loaded)
                : loaded;
            _recorder.SetActions(combined);
            _currentMode =
                combined.Any(a =>
                    a.Type is MacroActionType.KeyDown
                    or MacroActionType.KeyUp)
                ? RecordingMode.KeyboardAndMouse
                : mode;
            _tabs.SelectedIndex =
                _currentMode == RecordingMode.Mouse
                    ? 0
                    : 1;
            _statusLabel.Text = insert
                ? $"Inserted recording. {combined.Count} actions total."
                : $"Opened {combined.Count} actions.";
            _timerLabel.Text = "00:00:00.000";
            UpdateButtons();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Could not open recording:\n" + ex.Message,
                "Rian Macro",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
    private void RestoreWindow()
    {
        if (_closing)
            return;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }
    private void UpdateButtons()
    {
        bool busy = IsBusy;
        _tabs.Enabled = !busy;
        _recordButton.Enabled =
            !_preparing && !_player.IsPlaying;
        _recordButton.Text =
            _recorder.IsRecording
                ? "Stop Recording"
                : "Record";
        _playButton.Enabled = !busy;
        _stopButton.Enabled = busy;
        _saveButton.Enabled = !busy;
        _openButton.Enabled = !busy;
        _insertButton.Enabled = !busy;
        _loopCount.Enabled =
            !busy && !_unlimited.Checked;
        _unlimited.Enabled = !busy;
        _speed.Enabled = !busy;
        _recordShortcut.Enabled = !busy;
        _playShortcut.Enabled = !busy;
        _customSizeCheckBox.Enabled = true;
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_customSizeEnabled &&
            WindowState == FormWindowState.Normal)
        {
            _customWindowClientSize = ClientSize;
        }
        SaveWindowSettings();
        _closing = true;
        _cancelPreparation = true;
        _player.Stop();
        _mouseHook.Dispose();
        _keyboardHook.Dispose();
        _recorder.Stop();
        _timer.Stop();
        _timer.Dispose();
        _customSizeToolTip.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        base.OnFormClosing(e);
    }
}