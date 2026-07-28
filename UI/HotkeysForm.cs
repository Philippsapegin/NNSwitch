using INSwitch.Interop;
using INSwitch.Models;
using INSwitch.Services;

namespace INSwitch.UI;

internal sealed class HotkeysForm : Form
{
    private const int HotkeyColumnIndex = 2;

    private readonly IReadOnlyList<KeyboardLayoutDescriptor> _layouts;
    private readonly List<HotkeyRow> _rows;
    private readonly DataGridView _grid;
    private readonly Label _statusLabel;
    private readonly Label _validationLabel;
    private int _capturingRow = -1;

    internal HotkeySettings Result { get; private set; }

    internal HotkeysForm(
        HotkeySettings current,
        IReadOnlyList<KeyboardLayoutDescriptor> layouts)
    {
        _layouts = layouts;
        Result = current.Clone();
        _rows = BuildRows(current, layouts);

        Text = "Hotkeys — NN Switch";
        ClientSize = new Size(760, Math.Clamp(228 + (_rows.Count * 29), 420, 700));
        MinimumSize = new Size(680, 400);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        KeyPreview = true;

        var title = new Label
        {
            Text = "Hotkeys",
            Font = new Font("Segoe UI Semibold", 14F),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4)
        };

        var description = new Label
        {
            Text = "Click a hotkey cell to clear it, then press any key or key combination.",
            ForeColor = DarkTheme.Muted,
            AutoSize = true,
            MaximumSize = new Size(690, 0),
            Margin = new Padding(0, 0, 0, 12)
        };

        _grid = CreateGrid();
        PopulateGrid();

        _statusLabel = new Label
        {
            Text = "Click a hotkey cell to edit it.",
            ForeColor = DarkTheme.Muted,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 10, 0, 0)
        };

        _validationLabel = new Label
        {
            ForeColor = DarkTheme.Danger,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 0, 0)
        };

        var defaultsButton = CreateButton("Defaults", (_, _) => RestoreDefaults());
        var cancelButton = CreateButton("Cancel", (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        });
        var saveButton = CreateButton("Save", SaveButtonOnClick);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            WrapContents = false,
            Margin = new Padding(0, 12, 0, 0)
        };
        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(defaultsButton);

        var feedback = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Dock = DockStyle.Fill,
            Margin = new Padding(0)
        };
        feedback.Controls.Add(_statusLabel);
        feedback.Controls.Add(_validationLabel);

        var content = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 5,
            Dock = DockStyle.Fill,
            Padding = new Padding(22, 18, 22, 18)
        };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.Controls.Add(title, 0, 0);
        content.Controls.Add(description, 0, 1);
        content.Controls.Add(_grid, 0, 2);
        content.Controls.Add(feedback, 0, 3);
        content.Controls.Add(buttons, 0, 4);
        Controls.Add(content);

        CancelButton = cancelButton;
        DarkTheme.Apply(this);
        DarkTheme.StyleGrid(_grid);
        DarkTheme.EnableAccentHover(saveButton);
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (_capturingRow < 0)
        {
            return base.ProcessCmdKey(ref message, keyData);
        }

        var key = keyData & Keys.KeyCode;
        if (key is Keys.None or Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
        {
            return true;
        }

        var modifiers = HotkeyModifiers.None;
        if ((keyData & Keys.Control) != 0)
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if ((keyData & Keys.Alt) != 0)
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if ((keyData & Keys.Shift) != 0)
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (NativeMethods.IsKeyDown(NativeMethods.VkLwin) ||
            NativeMethods.IsKeyDown(NativeMethods.VkRwin))
        {
            modifiers |= HotkeyModifiers.Win;
        }

        var row = _rows[_capturingRow];
        row.Binding = HotkeyBinding.Create(modifiers, key);
        _grid.Rows[_capturingRow].Cells[HotkeyColumnIndex].Value =
            HotkeyFormatter.Format(row.Binding);
        _grid.Rows[_capturingRow].Cells[HotkeyColumnIndex].Style.ForeColor =
            DarkTheme.Foreground;
        _capturingRow = -1;
        _statusLabel.Text = "Click a hotkey cell to edit it.";
        _validationLabel.Text = string.Empty;
        return true;
    }

    private DataGridView CreateGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AllowUserToResizeColumns = true,
            AutoGenerateColumns = false,
            RowHeadersVisible = false,
            MultiSelect = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            CellBorderStyle = DataGridViewCellBorderStyle.None,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
            BorderStyle = BorderStyle.None,
            RowTemplate = { Height = 28 },
            ColumnHeadersHeight = 30,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
        };

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Target",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 38F,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Action",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 34F,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "Hotkey",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 28F,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        grid.CellClick += GridOnCellClick;
        return grid;
    }

    private void PopulateGrid()
    {
        foreach (var row in _rows)
        {
            var rowIndex = _grid.Rows.Add(
                row.Scope,
                row.Action,
                HotkeyFormatter.Format(row.Binding));
            _grid.Rows[rowIndex].Tag = row;
        }
    }

    private void GridOnCellClick(object? sender, DataGridViewCellEventArgs eventArgs)
    {
        if (eventArgs.RowIndex < 0 || eventArgs.ColumnIndex != HotkeyColumnIndex)
        {
            return;
        }

        if (_capturingRow >= 0 && _capturingRow != eventArgs.RowIndex)
        {
            _grid.Rows[_capturingRow].Cells[HotkeyColumnIndex].Style.ForeColor =
                DarkTheme.Foreground;
        }

        _capturingRow = eventArgs.RowIndex;
        var row = _rows[_capturingRow];
        row.Binding = new HotkeyBinding();
        var cell = _grid.Rows[_capturingRow].Cells[HotkeyColumnIndex];
        cell.Value = string.Empty;
        cell.Style.ForeColor = DarkTheme.Accent;
        _statusLabel.Text = $"Press a shortcut for {row.FullName}.";
        _validationLabel.Text = string.Empty;
        _grid.Focus();
    }

    private void RestoreDefaults()
    {
        var defaults = HotkeySettings.Defaults;
        foreach (var row in _rows)
        {
            row.Binding = row.TargetLayoutId is null
                ? row.Mode switch
                {
                    TextSwitchMode.SelectedText => defaults.SelectedText.Clone(),
                    TextSwitchMode.LastWord => defaults.LastWord.Clone(),
                    TextSwitchMode.ActiveField => defaults.ActiveField.Clone(),
                    _ => new HotkeyBinding()
                }
                : new HotkeyBinding();
        }

        _capturingRow = -1;
        RefreshHotkeyCells();
        _statusLabel.Text = "Defaults restored. Language-specific hotkeys are empty.";
        _validationLabel.Text = string.Empty;
    }

    private void SaveButtonOnClick(object? sender, EventArgs eventArgs)
    {
        var duplicate = _rows
            .Where(row => row.Binding.IsConfigured)
            .GroupBy(
                row => $"{(uint)row.Binding.Modifiers}:{(int)row.Binding.Key}",
                StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            _validationLabel.Text =
                $"Duplicate shortcut: {HotkeyFormatter.Format(duplicate.First().Binding)}.";
            return;
        }

        var result = new HotkeySettings
        {
            SelectedText = FindGeneral(TextSwitchMode.SelectedText).Binding.Clone(),
            LastWord = FindGeneral(TextSwitchMode.LastWord).Binding.Clone(),
            ActiveField = FindGeneral(TextSwitchMode.ActiveField).Binding.Clone(),
            TargetLayouts = new Dictionary<string, TargetLayoutHotkeys>(
                StringComparer.OrdinalIgnoreCase)
        };

        foreach (var layout in _layouts)
        {
            result.TargetLayouts[layout.Id] = new TargetLayoutHotkeys
            {
                SelectedText = FindTarget(layout.Id, TextSwitchMode.SelectedText).Binding.Clone(),
                LastWord = FindTarget(layout.Id, TextSwitchMode.LastWord).Binding.Clone(),
                ActiveField = FindTarget(layout.Id, TextSwitchMode.ActiveField).Binding.Clone()
            };
        }

        Result = result;
        DialogResult = DialogResult.OK;
        Close();
    }

    private HotkeyRow FindGeneral(TextSwitchMode mode) =>
        _rows.Single(row => row.TargetLayoutId is null && row.Mode == mode);

    private HotkeyRow FindTarget(string layoutId, TextSwitchMode mode) =>
        _rows.Single(row =>
            row.TargetLayoutId?.Equals(layoutId, StringComparison.OrdinalIgnoreCase) == true &&
            row.Mode == mode);

    private void RefreshHotkeyCells()
    {
        for (var index = 0; index < _rows.Count; index++)
        {
            _grid.Rows[index].Cells[HotkeyColumnIndex].Value =
                HotkeyFormatter.Format(_rows[index].Binding);
            _grid.Rows[index].Cells[HotkeyColumnIndex].Style.ForeColor =
                DarkTheme.Foreground;
        }
    }

    private static List<HotkeyRow> BuildRows(
        HotkeySettings current,
        IReadOnlyList<KeyboardLayoutDescriptor> layouts)
    {
        var rows = new List<HotkeyRow>
        {
            new("Mapped target", "Selected text", TextSwitchMode.SelectedText, null, current.SelectedText.Clone()),
            new(string.Empty, "Last written word", TextSwitchMode.LastWord, null, current.LastWord.Clone()),
            new(string.Empty, "Active text field", TextSwitchMode.ActiveField, null, current.ActiveField.Clone())
        };

        foreach (var layout in layouts)
        {
            var target = current.TargetLayouts.TryGetValue(layout.Id, out var configured)
                ? configured
                : new TargetLayoutHotkeys();
            rows.Add(new HotkeyRow(
                layout.DisplayName,
                "Selected text",
                TextSwitchMode.SelectedText,
                layout.Id,
                target.SelectedText.Clone()));
            rows.Add(new HotkeyRow(
                string.Empty,
                "Last written word",
                TextSwitchMode.LastWord,
                layout.Id,
                target.LastWord.Clone()));
            rows.Add(new HotkeyRow(
                string.Empty,
                "Active text field",
                TextSwitchMode.ActiveField,
                layout.Id,
                target.ActiveField.Clone()));
        }

        return rows;
    }

    private static Button CreateButton(string text, EventHandler onClick)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(92, 32),
            Margin = new Padding(7, 0, 0, 0)
        };
        button.Click += onClick;
        return button;
    }

    private sealed class HotkeyRow
    {
        internal HotkeyRow(
            string scope,
            string action,
            TextSwitchMode mode,
            string? targetLayoutId,
            HotkeyBinding binding)
        {
            Scope = scope;
            Action = action;
            Mode = mode;
            TargetLayoutId = targetLayoutId;
            Binding = binding;
        }

        internal string Scope { get; }

        internal string Action { get; }

        internal TextSwitchMode Mode { get; }

        internal string? TargetLayoutId { get; }

        internal HotkeyBinding Binding { get; set; }

        internal string FullName => string.IsNullOrEmpty(Scope) ? Action : $"{Scope}: {Action}";
    }
}
