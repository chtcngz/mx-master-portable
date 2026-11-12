using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MX4Portable;

internal sealed class MainForm : Form
{
    private static readonly Color Canvas = Color.FromArgb(242, 244, 247);
    private static readonly Color Surface = Color.White;
    private static readonly Color SurfaceAlt = Color.FromArgb(246, 248, 250);
    private static readonly Color Ink = Color.FromArgb(36, 42, 51);
    private static readonly Color Muted = Color.FromArgb(104, 115, 128);
    private static readonly Color Accent = Color.FromArgb(22, 128, 112);
    private static readonly Color Stroke = Color.FromArgb(225, 230, 236);
    private static readonly Color ButtonFace = Color.FromArgb(232, 236, 241);
    private readonly ProfileStore _store = new();
    private AppProfile _profile = new();
    private readonly List<MxMasterSession> _sessions = [];
    private MxMasterSession? _current;
    private MouseHook? _mouseHook;
    private bool _active;
    private bool _busy;
    private bool _closing;
    private bool _exitRequested;
    private bool _trayHintShown;
    private readonly NotifyIcon _tray = new();
    private readonly ContextMenuStrip _trayMenu = new();
    private readonly ToolStripMenuItem _trayStop = new("Atamaları durdur");
    private Icon? _brandIcon;

    private bool _restoring;
    private readonly EventWaitHandle _showSignal = new(false, EventResetMode.AutoReset, Program.ShowEventName);
    private RegisteredWaitHandle? _showWait;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
    private const int SwRestore = 9;

    private readonly ComboBox _devices = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Button _scan = new() { Text = "Cihazları tara", AutoSize = true };
    private readonly Button _apply = new() { Text = "Atamaları uygula", AutoSize = true };
    private readonly Button _stop = new() { Text = "Atamaları durdur", AutoSize = true };
    private readonly Button _reset = new() { Text = "Varsayılana sıfırla", AutoSize = true };
    private readonly Button _recover = new() { Text = "Önceki ayarı kurtar", AutoSize = true, Visible = false };
    private readonly ToolTip _tips = new();
    private readonly CheckBox _global = new()
    {
        Text = "Windows geri/ileri/orta tuşlarını bütün farelerde yakala (isteğe bağlı, cihaz bazlı değil)",
        AutoSize = true
    };
    private readonly CheckBox _reuseDiverted = new()
    {
        Text = "Zaten yönlendirilmiş HID++ tuşlarını dinle (diğer uygulama da çalışabilir)",
        AutoSize = true
    };
    private readonly DataGridView _buttons = new()
    {
        Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        ReadOnly = false, MultiSelect = false, RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, EditMode = DataGridViewEditMode.EditOnEnter
    };
    private readonly TextBox _log = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        Font = new Font("Consolas", 9), BackColor = Color.FromArgb(247, 249, 251)
    };
    private readonly Label _state = new() { AutoSize = true, Text = "Hazır" };
    private TableLayoutPanel _layout = null!;
    private const int GridRow = 4;
    private const int MinLogHeight = 110;

    public MainForm()
    {
        Text = "MX Master Portable — MX Master 3S / 4";
        MinimumSize = new Size(900, 650);
        Size = new Size(1120, 790);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);

        var title = new Label
        {
            Text = "MX MASTER PORTABLE",
            Font = new Font("Segoe UI Semibold", 14, FontStyle.Bold),
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft
        };
        var subtitle = new Label
        {
            Text = "MX Master 3S / 4     •     Bluetooth ve Bolt     •     Tuş atamaları",
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Muted
        };
        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            BackColor = Surface, Padding = new Padding(20, 8, 12, 6) };
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        title.ForeColor = Ink;
        subtitle.ForeColor = Muted;
        top.Controls.Add(title, 0, 0);
        top.Controls.Add(subtitle, 0, 1);

        var buttonsLine = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = false,
            WrapContents = false, BackColor = Surface, Padding = new Padding(12, 8, 8, 5) };
        buttonsLine.Controls.AddRange([_scan, _apply, _stop, _reset, _recover, _state]);
        _state.Margin = new Padding(10, 7, 0, 0);
        var deviceLine = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2,
            BackColor = Surface, Padding = new Padding(14, 5, 14, 5) };
        deviceLine.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        deviceLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        deviceLine.Controls.Add(new Label { Text = "Bağlı cihaz", Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft, ForeColor = Muted }, 0, 0);
        deviceLine.Controls.Add(_devices, 1, 0);
        var warning = new Label
        {
            Text = "Pencere kapanınca tepside çalışır. Tam çıkış: tepsi menüsü. Diğer tuş yöneticileriyle atamalar çakışabilir.",
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.FromArgb(255, 247, 232), ForeColor = Color.FromArgb(122, 84, 24), Padding = new Padding(8, 0, 0, 0)
        };

        _buttons.Columns.Add(new DataGridViewTextBoxColumn
        { Name = "Source", HeaderText = "Erişim", FillWeight = 20, ReadOnly = true });
        _buttons.Columns.Add(new DataGridViewTextBoxColumn
        { Name = "Name", HeaderText = "Tuş / Kontrol", FillWeight = 37, ReadOnly = true });
        _buttons.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "Action", HeaderText = "Atanan işlem", FillWeight = 43,
            DataSource = ShortcutActions.All, ValueMember = nameof(ActionOption.Id), DisplayMember = nameof(ActionOption.Title),
            FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox
        });
        _buttons.DataError += (_, e) => e.ThrowException = false;
        _buttons.CellBeginEdit += (_, e) =>
            _editPrevious = e.ColumnIndex == 2 ? _buttons.Rows[e.RowIndex].Cells[2].Value as string : null;
        _buttons.CellValueChanged += (_, e) => OnActionCellChanged(e);
        _buttons.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_buttons.IsCurrentCellDirty) _buttons.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };

        var layout = _layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 1, RowCount = 10,
            BackColor = Canvas
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        // Grid height is set from the row count in FitGridToRows.
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 300));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.Controls.Add(top, 0, 0);
        layout.Controls.Add(buttonsLine, 0, 1);
        layout.Controls.Add(deviceLine, 0, 2);
        layout.Controls.Add(new Label { Text = "Tuşlar ve atamalar", Font = new Font(Font, FontStyle.Bold),
            Dock = DockStyle.Fill, ForeColor = Ink }, 0, 3);
        layout.Controls.Add(_buttons, 0, 4);
        layout.Controls.Add(_global, 0, 5);
        layout.Controls.Add(_reuseDiverted, 0, 6);
        layout.Controls.Add(new Label { Text = "Tanılama / olay günlüğü", Dock = DockStyle.Fill,
            Font = new Font(Font, FontStyle.Bold), ForeColor = Ink }, 0, 7);
        layout.Controls.Add(_log, 0, 8);
        layout.Controls.Add(warning, 0, 9);
        Controls.Add(layout);
        ApplyVisualStyle();
        ConfigureTray();
        _global.Checked = _profile.AllowGlobalMouseHook;
        _scan.Click += async (_, _) => await ScanAsync();
        _apply.Click += async (_, _) => await ApplyAsync();
        _stop.Click += async (_, _) => await StopAsync();
        _reset.Click += (_, _) => ResetToDefaults();
        _recover.Click += async (_, _) => await RestoreJournalAsync();
        _tips.SetToolTip(_stop, "Mouse'taki yönlendirmeleri kaldırır; seçimleriniz profilde saklı kalır.");
        _tips.SetToolTip(_reset, "Bu mouse için bütün atamaları 'Varsayılan (fare işlevi)' yapar.");
        _tips.SetToolTip(_recover, "Program önceki çalışmada kapanmadan çöktüyse mouse'ta kalan yönlendirmeleri kaldırır.");
        _devices.SelectedIndexChanged += (_, _) =>
        {
            if (_busy) return;
            // Preserve edits for the previous physical mouse before switching.
            SaveScreenSettings();
            _current = _devices.SelectedItem as MxMasterSession;
            _profile = _current is null ? new() : _store.LoadProfile(_current.UnitId, _current.Model);
            _global.Checked = _profile.AllowGlobalMouseHook;
            PopulateControls();
            UpdateButtons();
        };
        FormClosing += OnFormClosing;
        Resize += (_, _) => { if (!_restoring && WindowState == FormWindowState.Minimized) HideToTray(); };
        // A second launch signals this event instead of starting another copy.
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showSignal, (_, _) =>
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((Action)ShowFromTray); } catch { }
        }, null, Timeout.Infinite, false);
        PopulateControls();
        UpdateButtons();
        Line($"Ayarlar dizini: {_store.Folder}");
        if (_store.LoadJournal() is not null)
            Line("UYARI: Önceki çalışmadan recovery.json mevcut; aynı mouse'u seçip 'Önceki ayarı kurtar' seçin.");
        Shown += async (_, _) => await ScanAsync();
    }

    private void ApplyVisualStyle()
    {
        BackColor = Canvas;
        ForeColor = Ink;
        _state.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
        _state.ForeColor = Accent;
        _state.Margin = new Padding(18, 8, 0, 0);
        _devices.FlatStyle = FlatStyle.Flat;
        _devices.BackColor = Surface;
        _devices.ForeColor = Ink;
        _global.ForeColor = Ink;
        _reuseDiverted.ForeColor = Muted;
        _buttons.BackgroundColor = Surface;
        _buttons.BorderStyle = BorderStyle.None;
        _buttons.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _buttons.GridColor = Stroke;
        _buttons.RowTemplate.Height = 36;
        _buttons.ColumnHeadersHeight = 40;
        _buttons.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _buttons.EnableHeadersVisualStyles = false;
        _buttons.ColumnHeadersDefaultCellStyle.BackColor = ButtonFace;
        _buttons.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
        _buttons.ColumnHeadersDefaultCellStyle.SelectionBackColor = ButtonFace;
        _buttons.ColumnHeadersDefaultCellStyle.SelectionForeColor = Ink;
        _buttons.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
        _buttons.DefaultCellStyle.BackColor = Surface;
        _buttons.DefaultCellStyle.ForeColor = Ink;
        _buttons.AlternatingRowsDefaultCellStyle.BackColor = SurfaceAlt;
        _buttons.AlternatingRowsDefaultCellStyle.ForeColor = Ink;
        _buttons.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 239, 235);
        _buttons.DefaultCellStyle.SelectionForeColor = Ink;
        _buttons.DefaultCellStyle.Padding = new Padding(7, 2, 7, 2);
        _buttons.EditingControlShowing += (_, e) =>
        {
            if (e.Control is ComboBox combo)
            {
                combo.BackColor = Surface;
                combo.ForeColor = Ink;
                combo.FlatStyle = FlatStyle.Flat;
            }
        };
        _log.BorderStyle = BorderStyle.None;
        _log.BackColor = Surface;
        _log.ForeColor = Muted;
        foreach (var button in new[] { _scan, _apply, _stop, _reset, _recover })
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.Height = 32;
            button.Padding = new Padding(10, 0, 10, 0);
            button.Margin = new Padding(0, 1, 8, 0);
            button.Cursor = Cursors.Hand;
            button.BackColor = ButtonFace;
            button.ForeColor = Ink;
        }
        _apply.BackColor = Accent;
        _apply.ForeColor = Color.White;
    }

    private void ConfigureTray()
    {
        _brandIcon = CreateBrandIcon();
        Icon = _brandIcon;
        _tray.Icon = _brandIcon;
        _tray.Text = "MX Master Portable";
        var open = new ToolStripMenuItem("Pencereyi aç", null, (_, _) => ShowFromTray());
        open.Font = new Font(open.Font, FontStyle.Bold);
        _trayStop.Click += async (_, _) => await StopAsync();
        var exit = new ToolStripMenuItem("Uygulamadan çık", null, (_, _) => ExitFromTray());
        _trayMenu.Items.AddRange([open, _trayStop, new ToolStripSeparator(), exit]);
        _tray.ContextMenuStrip = _trayMenu;
        _tray.DoubleClick += (_, _) => ShowFromTray();
        _tray.Visible = true;
    }

    private static Icon CreateBrandIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var background = new SolidBrush(Color.FromArgb(29, 122, 112));
            using var mouse = new SolidBrush(Color.White);
            using var wheel = new Pen(Color.FromArgb(29, 122, 112), 2);
            graphics.FillEllipse(background, 1, 1, 30, 30);
            graphics.FillEllipse(mouse, 10, 5, 12, 22);
            graphics.DrawLine(wheel, 16, 8, 16, 13);
        }
        IntPtr handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally { DestroyIcon(handle); }
    }

    private void HideToTray()
    {
        if (_closing || !Visible) return;
        if (!_busy && !_active && _current is not null) SaveScreenSettings();
        Hide();
        if (!_trayHintShown)
        {
            _tray.ShowBalloonTip(3000, "MX Master Portable",
                "Atamalar tepside çalışmaya devam ediyor. Tamamen çıkmak için tepsi menüsünü kullanın.",
                ToolTipIcon.Info);
            _trayHintShown = true;
        }
    }

    private void ShowFromTray()
    {
        if (_closing || IsDisposed) return;
        // The window was hidden while minimized. Showing it first fires a Resize
        // that still reports Minimized and would hide it again, so suppress the
        // minimize-to-tray handler until the window is really restored.
        _restoring = true;
        try
        {
            Show();
            if (IsIconic(Handle)) ShowWindow(Handle, SwRestore);
            WindowState = FormWindowState.Normal;
            BringToFront();
            Activate();
            SetForegroundWindow(Handle);
        }
        finally { _restoring = false; }
    }

    private void ExitFromTray()
    {
        if (_busy)
        {
            ShowFromTray();
            Line("Devam eden işlem bitince tepsi menüsünden tekrar çıkın.");
            return;
        }
        _exitRequested = true;
        Close();
    }

    private void Line(string value)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            try { BeginInvoke((Action)(() => Line(value))); } catch { }
            return;
        }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {value}{Environment.NewLine}");
        if (_log.TextLength > 35000) _log.Text = _log.Text[^25000..];
    }

    private void SetBusy(bool value, string title = "Hazır")
    {
        _busy = value;
        _state.Text = title;
        _state.ForeColor = _active ? Accent : Muted;
        _tray.Text = _active ? "MX Master Portable - Atamalar aktif" : "MX Master Portable - Hazır";
        UpdateButtons();
    }

    private static bool MatchesRecoveryJournal(RecoveryJournal? journal, MxMasterSession? session)
    {
        if (journal is null || session is null) return false;
        if (journal.UnitId == session.UnitId) return true;
        // v0.1 saved only the raw 4-byte unit ID for MX Master 4.
        return session.Model.Key == SupportedModels.Master4.Key &&
            journal.UnitId.Length == 8 &&
            session.UnitId.EndsWith(":unit:" + journal.UnitId, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateButtons()
    {
        _scan.Enabled = !_busy && !_active;
        _apply.Enabled = !_busy && !_active && _current is not null && _store.LoadJournal() is null;
        _stop.Enabled = !_busy && _active;
        _reset.Enabled = !_busy && !_active && _buttons.Rows.Count > 0;
        var journal = _store.LoadJournal();
        // Recovery is only meaningful after a crash left recovery.json behind.
        _recover.Visible = journal is not null;
        _recover.Enabled = !_busy && !_active && _current is not null &&
                           MatchesRecoveryJournal(journal, _current);
        _devices.Enabled = !_busy && !_active;
        _buttons.Enabled = !_busy && !_active;
        _global.Enabled = !_busy && !_active;
        _reuseDiverted.Enabled = !_busy && !_active;
        _trayStop.Enabled = !_busy && _active;
    }

    private void PopulateControls()
    {
        _buttons.Rows.Clear();
        if (_current is not null)
        {
            foreach (var c in _current.Controls.Where(c => !c.Virtual))
            {
                string source = c.Editable ? $"HID++ 0x{c.Cid:X4}" : "Değiştirilemez";
                int rowId = _buttons.Rows.Add(source, c.Description, c.Editable ? _profile.Get(c.Key) : "native");
                var row = _buttons.Rows[rowId];
                row.Tag = c.Key;
                if (!c.Editable)
                {
                    // ReadOnly can only be set after the cell belongs to a row.
                    row.Cells[2] = new DataGridViewTextBoxCell { Value = "Cihazın kendi işlevi" };
                    row.Cells[2].ReadOnly = true;
                    row.Cells[0].ToolTipText = "Fare bu kontrolün yeniden atanmasına izin vermiyor.";
                    row.DefaultCellStyle.ForeColor = Muted;
                }
            }
        }
        if (_current is { ThumbwheelFeature: not 0 })
        {
            int rowId = _buttons.Rows.Add($"HID++ 0x2150", "Başparmak tekerleği (yatay)", null);
            var row = _buttons.Rows[rowId];
            row.Tag = ThumbActions.Key;
            string value = _profile.Get(ThumbActions.Key);
            row.Cells[2] = NewActionCell(ThumbActions.All);
            row.Cells[2].Value = ThumbActions.All.Any(a => a.Id == value) ? value : "native";
        }
        foreach (var fallback in new[]
        {
            ("SYS:BACK", "Windows XButton1 / Geri"),
            ("SYS:FORWARD", "Windows XButton2 / İleri"),
            ("SYS:MIDDLE", "Windows Orta tuş")
        })
        {
            int rowId = _buttons.Rows.Add("Windows · genel", fallback.Item2, null);
            _buttons.Rows[rowId].Tag = fallback.Item1;
        }
        // Shortcut rows get their own option list so a recorded custom
        // shortcut ("keys:…") is a valid, displayable value of that cell.
        foreach (DataGridViewRow row in _buttons.Rows)
        {
            if (row.Tag is not string id || id == ThumbActions.Key || row.Cells[2].ReadOnly) continue;
            SetShortcutCell(row, _profile.Get(id));
        }
        FitGridToRows();
    }

    private static DataGridViewComboBoxCell NewActionCell(IEnumerable<ActionOption> options) => new()
    {
        DataSource = options.ToList(),
        ValueMember = nameof(ActionOption.Id),
        DisplayMember = nameof(ActionOption.Title),
        FlatStyle = FlatStyle.Flat,
        DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox
    };

    private static void SetShortcutCell(DataGridViewRow row, string value)
    {
        if (value != "native" && !ShortcutActions.TryParseCustom(value, out _) &&
            ShortcutActions.All.All(a => a.Id != value)) value = "native";
        row.Cells[2] = NewActionCell(ShortcutActions.OptionsFor(value));
        row.Cells[2].Value = value;
    }

    private string? _editPrevious;

    private void OnActionCellChanged(DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex != 2) return;
        var row = _buttons.Rows[e.RowIndex];
        if (row.Cells[2].Value as string != ShortcutActions.RecordCustom) return;
        string previous = _editPrevious is null or ShortcutActions.RecordCustom ? "native" : _editPrevious;
        // Leave the grid's edit cycle before replacing the cell or opening a dialog.
        BeginInvoke(() =>
        {
            _buttons.EndEdit();
            _buttons.CurrentCell = null;
            using var dialog = new ShortcutCaptureForm(previous);
            string result = dialog.ShowDialog(this) == DialogResult.OK && dialog.Result is not null
                ? dialog.Result : previous;
            SetShortcutCell(row, result);
            if (result != previous) Line($"Özel kısayol: {row.Cells[1].Value} → {ShortcutActions.Describe(result)}");
        });
    }

    // Show every assignment row without scrolling; the log takes what is left.
    private void FitGridToRows()
    {
        int gridHeight = _buttons.ColumnHeadersHeight +
            _buttons.Rows.GetRowsHeight(DataGridViewElementStates.Visible) + 12;
        _layout.RowStyles[GridRow].Height = gridHeight;
        int fixedHeight = _layout.Padding.Vertical + MinLogHeight;
        foreach (RowStyle style in _layout.RowStyles)
            if (style.SizeType == SizeType.Absolute) fixedHeight += (int)style.Height;
        int missing = fixedHeight - ClientSize.Height;
        if (missing <= 0 || WindowState != FormWindowState.Normal) return;
        var area = Screen.FromControl(this).WorkingArea;
        Height = Math.Min(Height + missing, area.Height);
        if (Bottom > area.Bottom) Top = Math.Max(area.Top, area.Bottom - Height);
    }

    private void SaveScreenSettings()
    {
        _buttons.EndEdit();
        foreach (DataGridViewRow row in _buttons.Rows)
        {
            if (row.Tag is not string id || row.Cells[2].ReadOnly) continue;
            _profile.Actions[id] = row.Cells[2].Value?.ToString() ?? "native";
        }
        _profile.AllowGlobalMouseHook = _global.Checked;
        if (_current is not null)
            _store.SaveProfile(_current.UnitId, _profile);
    }

    private void ResetToDefaults()
    {
        if (_busy || _active) return;
        _buttons.EndEdit();
        foreach (DataGridViewRow row in _buttons.Rows)
            if (row.Tag is string && !row.Cells[2].ReadOnly) row.Cells[2].Value = "native";
        _profile.Actions.Clear();
        if (_current is not null) SaveScreenSettings();
        Line("Bütün atamalar varsayılana döndürüldü.");
    }

    private async Task ScanAsync()
    {
        if (_busy || _active) return;
        SetBusy(true, "HID taranıyor…");
        try
        {
            _current = null;
            _devices.Items.Clear();
            _buttons.Rows.Clear();
            foreach (var s in _sessions) await s.DisposeAsync();
            _sessions.Clear();
            var candidates = await Task.Run(HidNative.Enumerate);
            Line($"Logitech HID++ aday arayüz sayısı: {candidates.Count}");
            foreach (var device in candidates)
            {
                Line($"Arayüz: {device.TransportName}, PID {device.ProductId:X4}, page {device.UsagePage:X4}");
                HidppConnection? link = null;
                bool retained = false;
                try
                {
                    link = new HidppConnection(device);
                    byte[] slots = device.TransportName == "Bluetooth" ? [0xFF] : [1, 2, 3, 4, 5, 6];
                    foreach (byte slot in slots)
                    {
                        Line($"Slot {slot:X2} sorgulanıyor…");
                        var session = await MxMasterSession.ProbeAsync(link, slot);
                        if (session is null)
                        {
                            if (link.LastError is not null) Line(link.LastError);
                            if (link.IsBroken) break;
                            continue;
                        }
                        session.ButtonPressed += cid => OnHidButton(session, cid);
                        session.ThumbRotated += (rotation, status) => OnThumb(session, rotation, status);
                        _sessions.Add(session);
                        retained = true;
                        Line($"Bulundu: {session.Model.DisplayName} · {session.Name} · " +
                             $"{device.TransportName}, slot {slot:X2}, PID [{session.ModelPids}], " +
                             $"{session.Controls.Count} kontrol · 0x1B04 index {session.ControlsFeature:X2}");
                        // The same Bolt receiver can have 3S and 4 in different
                        // slots; both share a single reader, not separate handles.
                    }
                }
                catch (Exception ex) { Line($"Arayüze erişilemedi: {ex.Message}"); }
                finally { if (link is not null && !retained) await link.DisposeAsync(); }
            }
            foreach (var s in _sessions) _devices.Items.Add(s);
            if (_sessions.Count > 0)
            {
                _current = _sessions[0];
                _profile = _store.LoadProfile(_current.UnitId, _current.Model);
                _global.Checked = _profile.AllowGlobalMouseHook;
                _devices.SelectedIndex = 0;
                Line("Hazır. İstediğiniz mouse'u seçin, tuş atamalarını belirleyin ve 'Atamaları uygula' deyin.");
            }
            else
                Line("MX Master 3S / 4 doğrulanamadı. Mouse'u uyandırın ve BT/Bolt veya HID erişimini kontrol edin.");
            PopulateControls();
        }
        catch (Exception ex) { Line($"Tarama hatası: {ex}"); }
        finally { SetBusy(false); }
    }

    private void OnHidButton(MxMasterSession session, ushort cid)
    {
        if (!_active || session != _current) return;
        string id = $"CID:{cid:X4}";
        string action = _profile.Get(id);
        if (action == "native") return;
        _ = Task.Run(() =>
        {
            try { ShortcutActions.Execute(action); }
            catch (Exception ex) { Line($"Kısayol gönderilemedi: {ex.Message}"); }
        });
        Line($"HID++ tuş {id} → {ShortcutActions.Describe(action)}");
    }

    private int _thumbAccum;
    private bool _thumbFired;
    private long _thumbLast;

    // Runs on the HID read thread. Rotation is accumulated into steps; desktop
    // switching fires once per gesture so a flick never skips several desktops.
    private void OnThumb(MxMasterSession session, short rotation, byte status)
    {
        if (!_active || session != _current) return;
        string action = _profile.Get(ThumbActions.Key);
        if (!ThumbActions.NeedsDivert(action)) return;
        long now = Environment.TickCount64;
        // A pause also ends the gesture, in case the stop event was missed.
        if (status == 1 || now - _thumbLast > 400) { _thumbAccum = 0; _thumbFired = false; }
        _thumbLast = now;
        _thumbAccum += rotation;
        int res = session.ThumbwheelDivertedResolution;
        int step = res > 0 ? Math.Max(1, res / 8) : 15;
        while (Math.Abs(_thumbAccum) >= step && !(_thumbFired && ThumbActions.OncePerGesture(action)))
        {
            bool forward = _thumbAccum > 0;
            _thumbAccum -= forward ? step : -step;
            _thumbFired = true;
            var keys = ThumbActions.Keys(action, forward);
            _ = Task.Run(() =>
            {
                try { ShortcutActions.Send(keys); }
                catch (Exception ex) { Line($"Tekerlek kısayolu gönderilemedi: {ex.Message}"); }
            });
        }
        if (status == 3) { _thumbAccum = 0; _thumbFired = false; }
    }

    private async Task ApplyAsync()
    {
        if (_busy || _active || _current is null) return;
        SetBusy(true, "Uygulanıyor…");
        try
        {
            SaveScreenSettings();
            if (_store.LoadJournal() is not null)
                throw new InvalidOperationException("Önce recovery.json kurtarma işlemi yapılmalı.");
            var desired = _current.Controls
                .Where(c => c.Editable && _profile.Get(c.Key) != "native")
                .ToList();
            var originals = new Dictionary<string, bool>();
            var reused = new HashSet<ushort>();
            foreach (var c in desired)
            {
                ControlReporting existing = await _current.GetReportingAsync(c.Cid);
                if (existing.TemporarilyDiverted)
                {
                    if (!_reuseDiverted.Checked)
                        throw new InvalidOperationException($"0x{c.Cid:X4} önceden yönlendirilmiş. Çakışan bir uygulama çalışıyor olabilir.");
                    reused.Add(c.Cid);
                    Line($"Mevcut yönlendirme dinleniyor: 0x{c.Cid:X4}. Diğer uygulama da bu tuşu işleyebilir.");
                    continue;
                }
                originals[c.Key] = false;
            }
            string thumb = _profile.Get(ThumbActions.Key);
            bool thumbChange = false, thumbInverted = false;
            if (_current.ThumbwheelFeature != 0 && thumb != "native")
            {
                var (diverted, inverted) = await _current.GetThumbwheelStatusAsync();
                thumbInverted = inverted;
                if (diverted && ThumbActions.NeedsDivert(thumb))
                {
                    if (!_reuseDiverted.Checked)
                        throw new InvalidOperationException("Başparmak tekerleği önceden yönlendirilmiş. Çakışan bir uygulama çalışıyor olabilir.");
                    Line("Başparmak tekerleğinin mevcut yönlendirmesi dinleniyor.");
                }
                else
                {
                    originals["THUMB:DIVERT"] = diverted;
                    originals["THUMB:INVERT"] = inverted;
                    thumbChange = true;
                }
            }
            if (originals.Count > 0)
            {
                // Recovery journal must exist BEFORE touching the physical mouse.
                _store.SaveJournal(new RecoveryJournal
                {
                    UnitId = _current.UnitId,
                    OriginalTemporaryDiversions = originals
                });
                foreach (var c in desired.Where(c => !reused.Contains(c.Cid)))
                {
                    await _current.SetTemporaryDivertAsync(c.Cid, true);
                    Line($"Yönlendirildi: {c.Description} · 0x{c.Cid:X4} → {ShortcutActions.Describe(_profile.Get(c.Key))}");
                }
                if (thumbChange)
                {
                    bool divert = ThumbActions.NeedsDivert(thumb);
                    // "Ters yön" flips the device's own setting and keeps native scrolling.
                    bool invert = thumb == "hscroll-invert" ? !thumbInverted : thumbInverted;
                    await _current.SetThumbwheelReportingAsync(divert, invert);
                    Line($"Başparmak tekerleği → {ThumbActions.All.First(a => a.Id == thumb).Title}");
                }
            }
            if (_global.Checked && new[] { "SYS:BACK", "SYS:FORWARD", "SYS:MIDDLE" }
                .Any(id => _profile.Get(id) != "native"))
            {
                _mouseHook = new MouseHook(_profile.Get, action =>
                {
                    _ = Task.Run(() =>
                    {
                        try { ShortcutActions.Execute(action); }
                        catch (Exception ex) { Line($"Windows tuş hatası: {ex.Message}"); }
                    });
                });
                Line("Windows fallback ETKİN: bütün bağlı farelerdeki seçili tuşları etkiler.");
            }
            _active = true;
            Line("Atamalar aktif. Program açık kaldığı sürece geçerli.");
        }
        catch (Exception ex)
        {
            Line($"UYGULAMA HATASI: {ex.Message}");
            try { await RestoreCoreAsync(); }
            catch (Exception recoveryError) { Line($"Geri alma hatası: {recoveryError.Message}"); }
        }
        finally { SetBusy(false, _active ? "Aktif" : "Hazır"); }
    }

    private async Task RestoreCoreAsync()
    {
        _mouseHook?.Dispose();
        _mouseHook = null;
        _active = false;
        var journal = _store.LoadJournal();
        if (journal is null) return;
        if (_current is null || !MatchesRecoveryJournal(journal, _current))
            throw new InvalidOperationException("Kurtarma için aynı fiziksel MX Master mouse'u seçin.");
        List<string> errors = [];
        foreach (var item in journal.OriginalTemporaryDiversions)
        {
            if (!item.Key.StartsWith("CID:", StringComparison.OrdinalIgnoreCase) ||
                !ushort.TryParse(item.Key[4..], System.Globalization.NumberStyles.HexNumber,
                    null, out ushort cid)) continue;
            try
            {
                await _current.SetTemporaryDivertAsync(cid, item.Value);
                Line($"Eski durum geri alındı: {item.Key}");
            }
            catch (Exception ex) { errors.Add($"{item.Key}: {ex.Message}"); }
        }
        if (journal.OriginalTemporaryDiversions.TryGetValue("THUMB:DIVERT", out bool thumbDiverted))
        {
            journal.OriginalTemporaryDiversions.TryGetValue("THUMB:INVERT", out bool thumbInverted);
            try
            {
                await _current.SetThumbwheelReportingAsync(thumbDiverted, thumbInverted);
                Line("Eski durum geri alındı: başparmak tekerleği");
            }
            catch (Exception ex) { errors.Add($"THUMB: {ex.Message}"); }
        }
        if (errors.Count > 0) throw new IOException("Bazı tuşlar geri alınamadı: " + string.Join("; ", errors));
        _store.ClearJournal();
        Line("Tüm geçici yönlendirmeler geri alındı.");
    }

    private async Task RestoreJournalAsync()
    {
        if (_busy || _active) return;
        SetBusy(true, "Kurtarılıyor…");
        try { await RestoreCoreAsync(); }
        catch (Exception ex) { Line($"KURTARMA HATASI: {ex.Message}"); }
        finally { SetBusy(false); }
    }

    private async Task StopAsync()
    {
        if (_busy) return;
        SetBusy(true, "Geri alınıyor…");
        try
        {
            await RestoreCoreAsync();
            Line("Atamalar durduruldu; mouse kendi işlevlerine döndü. Seçimler profilde saklı, " +
                 "temizlemek için 'Varsayılana sıfırla'.");
        }
        catch (Exception ex) { Line($"GERİ ALMA HATASI: {ex.Message}"); }
        finally { SetBusy(false); }
    }

    private async void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closing) return;
        e.Cancel = true;
        if (!_exitRequested && e.CloseReason != CloseReason.WindowsShutDown)
        {
            HideToTray();
            return;
        }
        if (_busy)
        {
            Line("Önce devam eden işlem bitmeli; ardından kapatabilirsiniz.");
            _exitRequested = false;
            return;
        }
        try
        {
            if (_current is not null && !_busy && !_active) SaveScreenSettings();
            if (_active) await StopAsync();
            foreach (var s in _sessions) await s.DisposeAsync();
            _sessions.Clear();
        }
        catch (Exception ex) { Line($"Kapatma uyarısı: {ex.Message}"); }
        _showWait?.Unregister(null);
        _showSignal.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _trayMenu.Dispose();
        _brandIcon?.Dispose();
        _closing = true;
        Close();
    }
}
