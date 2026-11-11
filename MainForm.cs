using System.Drawing;

namespace MX4Portable;

internal sealed class MainForm : Form
{
    private readonly ProfileStore _store = new();
    private AppProfile _profile = new();
    private readonly List<MxMasterSession> _sessions = [];
    private MxMasterSession? _current;
    private MouseHook? _mouseHook;
    private bool _active;
    private bool _busy;
    private bool _closing;

    private readonly ComboBox _devices = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly Button _scan = new() { Text = "Cihazları tara", AutoSize = true };
    private readonly Button _apply = new() { Text = "Atamaları uygula", AutoSize = true };
    private readonly Button _stop = new() { Text = "Durdur / geri al", AutoSize = true };
    private readonly Button _recover = new() { Text = "Önceki ayarı kurtar", AutoSize = true };
    private readonly CheckBox _global = new()
    {
        Text = "Windows geri/ileri/orta tuşlarını bütün farelerde yakala (isteğe bağlı, cihaz bazlı değil)",
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

    public MainForm()
    {
        Text = "MX Master Portable — MX Master 3S / 4";
        MinimumSize = new Size(840, 620);
        Size = new Size(1010, 760);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);

        var title = new Label
        {
            Text = "MX MASTER PORTABLE   •   cihaz erişimi ve tuş atama (v0.2)",
            Font = new Font("Segoe UI Semibold", 13, FontStyle.Bold),
            AutoSize = true, Padding = new Padding(0, 0, 0, 6)
        };
        var subtitle = new Label
        {
            Text = "MX Master 3S + 4 • Bluetooth + Bolt • HID++ 0x1B04 • geçici tuş yönlendirme",
            AutoSize = true, ForeColor = Color.DimGray
        };
        var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
        top.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        top.Controls.Add(title, 0, 0);
        top.Controls.Add(subtitle, 0, 1);

        var buttonsLine = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = false, WrapContents = true };
        buttonsLine.Controls.AddRange([_scan, _apply, _stop, _recover, _state]);
        _state.Margin = new Padding(10, 7, 0, 0);
        var deviceLine = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        deviceLine.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        deviceLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        deviceLine.Controls.Add(new Label { Text = "MX Master 3S / 4 cihazı:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        deviceLine.Controls.Add(_devices, 1, 0);
        var warning = new Label
        {
            Text = "HID++ tuşları yalnızca seçili mouse üzerinde çalışır. Windows fallback tüm fareleri etkiler. " +
                   "Logi Options+ veya başka remapper açıksa önce kapatın.",
            Dock = DockStyle.Fill, AutoSize = true, ForeColor = Color.FromArgb(115, 78, 8)
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
        _buttons.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_buttons.IsCurrentCellDirty) _buttons.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 9
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 61));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 69));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 31));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        layout.Controls.Add(top, 0, 0);
        layout.Controls.Add(buttonsLine, 0, 1);
        layout.Controls.Add(deviceLine, 0, 2);
        layout.Controls.Add(new Label { Text = "Tuşlar ve atamalar", Font = new Font(Font, FontStyle.Bold), Dock = DockStyle.Fill }, 0, 3);
        layout.Controls.Add(_buttons, 0, 4);
        layout.Controls.Add(_global, 0, 5);
        layout.Controls.Add(new Label { Text = "Tanılama / olay günlüğü", Dock = DockStyle.Fill, Font = new Font(Font, FontStyle.Bold) }, 0, 6);
        layout.Controls.Add(_log, 0, 7);
        layout.Controls.Add(warning, 0, 8);
        Controls.Add(layout);
        _global.Checked = _profile.AllowGlobalMouseHook;
        _scan.Click += async (_, _) => await ScanAsync();
        _apply.Click += async (_, _) => await ApplyAsync();
        _stop.Click += async (_, _) => await StopAsync();
        _recover.Click += async (_, _) => await RestoreJournalAsync();
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
        PopulateControls();
        UpdateButtons();
        Line($"Ayarlar dizini: {_store.Folder}");
        if (_store.LoadJournal() is not null)
            Line("UYARI: Önceki çalışmadan recovery.json mevcut; aynı mouse'u seçip 'Önceki ayarı kurtar' seçin.");
        Shown += async (_, _) => await ScanAsync();
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
        var journal = _store.LoadJournal();
        _recover.Enabled = !_busy && !_active && _current is not null &&
                           MatchesRecoveryJournal(journal, _current);
        _devices.Enabled = !_busy && !_active;
        _buttons.Enabled = !_busy && !_active;
        _global.Enabled = !_busy && !_active;
    }

    private void PopulateControls()
    {
        _buttons.Rows.Clear();
        if (_current is not null)
        {
            foreach (var c in _current.Controls.Where(c => !c.Virtual))
            {
                string source = c.Editable ? $"HID++ 0x{c.Cid:X4}" : "Salt okunur";
                int rowId = _buttons.Rows.Add(source, c.Description, c.Editable ? _profile.Get(c.Key) : "native");
                var row = _buttons.Rows[rowId];
                row.Tag = c.Key;
                if (!c.Editable)
                {
                    row.Cells[2].ReadOnly = true;
                    row.DefaultCellStyle.ForeColor = Color.DimGray;
                }
            }
        }
        foreach (var fallback in new[]
        {
            ("SYS:BACK", "Windows XButton1 / Geri"),
            ("SYS:FORWARD", "Windows XButton2 / İleri"),
            ("SYS:MIDDLE", "Windows Orta tuş")
        })
        {
            int rowId = _buttons.Rows.Add("Windows · genel", fallback.Item2, _profile.Get(fallback.Item1));
            _buttons.Rows[rowId].Tag = fallback.Item1;
        }
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
                        var session = await MxMasterSession.ProbeAsync(link, slot);
                        if (session is null) continue;
                        session.ButtonPressed += cid => OnHidButton(session, cid);
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
        Line($"HID++ tuş {id} → {action}");
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
            foreach (var c in desired)
            {
                ControlReporting existing = await _current.GetReportingAsync(c.Cid);
                if (existing.TemporarilyDiverted)
                    throw new InvalidOperationException($"0x{c.Cid:X4} önceden yönlendirilmiş. Çakışan bir uygulama çalışıyor olabilir.");
                originals[c.Key] = false;
            }
            if (originals.Count > 0)
            {
                // Recovery journal must exist BEFORE touching the physical mouse.
                _store.SaveJournal(new RecoveryJournal
                {
                    UnitId = _current.UnitId,
                    OriginalTemporaryDiversions = originals
                });
                foreach (var c in desired)
                {
                    await _current.SetTemporaryDivertAsync(c.Cid, true);
                    Line($"Yönlendirildi: {c.Description} · 0x{c.Cid:X4} → {_profile.Get(c.Key)}");
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
        try { await RestoreCoreAsync(); }
        catch (Exception ex) { Line($"GERİ ALMA HATASI: {ex.Message}"); }
        finally { SetBusy(false); }
    }

    private async void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_closing) return;
        e.Cancel = true;
        if (_busy)
        {
            Line("Önce devam eden işlem bitmeli; ardından kapatabilirsiniz.");
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
        _closing = true;
        Close();
    }
}
