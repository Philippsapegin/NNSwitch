using INSwitch.Interop;
using INSwitch.Models;
using INSwitch.Services;

namespace INSwitch.UI;

internal sealed class HotkeysForm : Form
{
    private const int HotkeyColumnIndex = 2;

    private readonly IReadOnlyList<KeyboardLayoutDescriptor> _layouts;
    private readonly List<HotkeyRow> _rows;
    private readonly DataGridView _universalGrid;
    private readonly DataGridView _layoutGrid;
    private readonly DataGridView _targetGrid;
    private readonly Label _statusLabel;
    private readonly Label _validationLabel;
    private HotkeyRow? _capturingRow;
    private DataGridViewRow? _capturingGridRow;

    internal HotkeySettings Result { get; private set; }

    internal Dictionary<string, string> SwitchTargetsResult { get; private set; }

    internal HotkeysForm(
        HotkeySettings current,
        IReadOnlyDictionary<string, string> currentTargets,
        IReadOnlyList<KeyboardLayoutDescriptor> layouts)
    {
        _layouts = layouts;
        Result = current.Clone();
        SwitchTargetsResult = new Dictionary<string, string>(
            currentTargets,
            StringComparer.OrdinalIgnoreCase);
        _rows = BuildRows(current, layouts);

        Text = "Hotkeys — NN Switch";
        ClientSize = new Size(1120, CalculateInitialClientHeight(layouts.Count));
        MinimumSize = new Size(900, 470);
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

        _universalGrid = CreateGrid();
        _layoutGrid = CreateGrid();
        _targetGrid = CreateTargetGrid(layouts, currentTargets);
        PopulateGrid(
            _universalGrid,
            _rows.Where(row => row.TargetLayoutId is null));
        PopulateGrid(
            _layoutGrid,
            _rows.Where(row => row.TargetLayoutId is not null));

        var sections = CreateSections(
            _targetGrid,
            _universalGrid,
            _layoutGrid);

        _statusLabel = new Label
        {
            Text = string.Empty,
            ForeColor = DarkTheme.Muted,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 8, 0, 0),
            Visible = false
        };

        _validationLabel = new Label
        {
            ForeColor = DarkTheme.Danger,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 8, 0, 0),
            Visible = false
        };

        var buttonSize = new Size(92, 32);
        var defaultsButton = DarkTheme.CreateButton(
            "Defaults",
            (_, _) => RestoreDefaults(),
            buttonSize);
        var cancelButton = DarkTheme.CreateButton("Cancel", (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }, buttonSize);
        var saveButton = DarkTheme.CreateButton("Save", SaveButtonOnClick, buttonSize);
        saveButton.Name = "SaveHotkeysButton";

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
        content.Controls.Add(sections, 0, 2);
        content.Controls.Add(feedback, 0, 3);
        content.Controls.Add(buttons, 0, 4);
        Controls.Add(content);

        CancelButton = cancelButton;
        DarkTheme.Apply(this);
        DarkTheme.StyleGrid(_universalGrid);
        DarkTheme.StyleGrid(_layoutGrid);
        DarkTheme.StyleGrid(_targetGrid);
        DarkTheme.EnableAccentHover(saveButton);
    }

    private static int CalculateInitialClientHeight(int layoutCount)
    {
        const int universalSectionHeight = 360;
        const int layoutSectionChromeHeight = 49;
        const int hotkeyHeaderHeight = 30;
        const int hotkeyRowHeight = 28;
        const int formChromeHeight = 128;

        var layoutSectionHeight = layoutSectionChromeHeight +
            hotkeyHeaderHeight +
            (layoutCount * 4 * hotkeyRowHeight);
        return Math.Clamp(
            Math.Max(universalSectionHeight, layoutSectionHeight) + formChromeHeight,
            508,
            760);
    }

    private void ShowStatus(string message)
    {
        _validationLabel.Text = string.Empty;
        _validationLabel.Visible = false;
        _statusLabel.Text = message;
        _statusLabel.Visible = true;
    }

    private void ShowValidation(string message)
    {
        _statusLabel.Text = string.Empty;
        _statusLabel.Visible = false;
        _validationLabel.Text = message;
        _validationLabel.Visible = true;
    }

    private void ClearFeedback()
    {
        _statusLabel.Text = string.Empty;
        _statusLabel.Visible = false;
        _validationLabel.Text = string.Empty;
        _validationLabel.Visible = false;
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (_capturingRow is null || _capturingGridRow is null)
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

        _capturingRow.Binding = HotkeyBinding.Create(modifiers, key);
        _capturingGridRow.Cells[HotkeyColumnIndex].Value =
            HotkeyFormatter.Format(_capturingRow.Binding);
        _capturingGridRow.Cells[HotkeyColumnIndex].Style.ForeColor =
            DarkTheme.Foreground;
        _capturingRow = null;
        _capturingGridRow = null;
        ClearFeedback();
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

    private static DataGridView CreateTargetGrid(
        IReadOnlyList<KeyboardLayoutDescriptor> layouts,
        IReadOnlyDictionary<string, string> currentTargets)
    {
        var grid = new DataGridView
        {
            Name = "TargetMappingGrid",
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = false,
            RowHeadersVisible = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            EditMode = DataGridViewEditMode.EditOnEnter,
            RowTemplate = { Height = 30 }
        };
        grid.DataError += (_, eventArgs) =>
        {
            if (eventArgs.Exception is not null)
            {
                ErrorLog.Write(eventArgs.Exception);
            }

            eventArgs.ThrowException = false;
        };
        grid.CellClick += (_, eventArgs) =>
        {
            if (eventArgs.RowIndex < 0 || eventArgs.ColumnIndex != 1)
            {
                return;
            }

            grid.BeginEdit(selectAll: true);
            if (grid.EditingControl is DataGridViewComboBoxEditingControl comboBox)
            {
                comboBox.BackColor = DarkTheme.Background;
                comboBox.ForeColor = DarkTheme.Foreground;
                comboBox.FlatStyle = FlatStyle.Flat;
                comboBox.DroppedDown = true;
            }
        };

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Source",
            HeaderText = "Current layout",
            ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 52F
        });

        var choices = new List<TargetChoice>
        {
            new(string.Empty, "(Do not switch)")
        };
        choices.AddRange(layouts.Select(layout =>
            new TargetChoice(layout.Id, layout.DisplayName)));

        grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "Target",
            HeaderText = "Correct text to",
            DataSource = choices,
            DisplayMember = nameof(TargetChoice.Name),
            ValueMember = nameof(TargetChoice.Id),
            DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing,
            FlatStyle = FlatStyle.Flat,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 48F
        });

        foreach (var layout in layouts)
        {
            var targetId = currentTargets.TryGetValue(layout.Id, out var configuredTarget) &&
                           choices.Any(choice => choice.Id.Equals(
                               configuredTarget,
                               StringComparison.OrdinalIgnoreCase))
                ? configuredTarget
                : string.Empty;
            var rowIndex = grid.Rows.Add(layout.DisplayName, targetId);
            grid.Rows[rowIndex].Tag = layout.Id;
        }

        return grid;
    }

    private static Control CreateSections(
        DataGridView targetGrid,
        DataGridView universalGrid,
        DataGridView layoutGrid)
    {
        var universalTitle = new Label
        {
            Text = "Universal",
            Font = new Font("Segoe UI Semibold", 12F),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        };
        var targetTitle = new Label
        {
            Text = "Default correction targets",
            ForeColor = DarkTheme.Muted,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 4)
        };
        var universalHotkeysTitle = new Label
        {
            Text = "Hotkeys",
            ForeColor = DarkTheme.Muted,
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 4)
        };

        var targetGridHeight = Math.Clamp(
            targetGrid.ColumnHeadersHeight +
            (targetGrid.Rows.Count * targetGrid.RowTemplate.Height) + 2,
            92,
            160);
        var universalSection = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 5,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0, 0, 12, 0)
        };
        universalSection.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        universalSection.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        universalSection.RowStyles.Add(new RowStyle(SizeType.Absolute, targetGridHeight));
        universalSection.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        universalSection.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        universalSection.Controls.Add(universalTitle, 0, 0);
        universalSection.Controls.Add(targetTitle, 0, 1);
        universalSection.Controls.Add(targetGrid, 0, 2);
        universalSection.Controls.Add(universalHotkeysTitle, 0, 3);
        universalSection.Controls.Add(universalGrid, 0, 4);

        var layoutTitle = new Label
        {
            Text = "By installed layout",
            Font = new Font("Segoe UI Semibold", 12F),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        };
        var layoutDescription = new Label
        {
            Text = "Plain layout switching first, then text conversion to a specific layout.",
            ForeColor = DarkTheme.Muted,
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            Margin = new Padding(0, 0, 0, 4)
        };
        var layoutSection = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 3,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(12, 0, 0, 0)
        };
        layoutSection.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutSection.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layoutSection.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layoutSection.Controls.Add(layoutTitle, 0, 0);
        layoutSection.Controls.Add(layoutDescription, 0, 1);
        layoutSection.Controls.Add(layoutGrid, 0, 2);

        var separator = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            BackColor = DarkTheme.Button
        };

        var sections = new TableLayoutPanel
        {
            ColumnCount = 3,
            RowCount = 1,
            Dock = DockStyle.Fill,
            Margin = new Padding(0)
        };
        sections.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46F));
        sections.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1F));
        sections.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54F));
        sections.Controls.Add(universalSection, 0, 0);
        sections.Controls.Add(separator, 1, 0);
        sections.Controls.Add(layoutSection, 2, 0);
        return sections;
    }

    private static void PopulateGrid(
        DataGridView grid,
        IEnumerable<HotkeyRow> rows)
    {
        foreach (var row in rows)
        {
            var rowIndex = grid.Rows.Add(
                row.Scope,
                row.DisplayName,
                HotkeyFormatter.Format(row.Binding));
            grid.Rows[rowIndex].Tag = row;
        }
    }

    private void GridOnCellClick(object? sender, DataGridViewCellEventArgs eventArgs)
    {
        if (eventArgs.RowIndex < 0 || eventArgs.ColumnIndex != HotkeyColumnIndex)
        {
            return;
        }

        if (sender is not DataGridView grid)
        {
            return;
        }

        if (_capturingGridRow is not null)
        {
            _capturingGridRow.Cells[HotkeyColumnIndex].Style.ForeColor =
                DarkTheme.Foreground;
        }

        var gridRow = grid.Rows[eventArgs.RowIndex];
        if (gridRow.Tag is not HotkeyRow row)
        {
            return;
        }

        _capturingRow = row;
        _capturingGridRow = gridRow;
        row.Binding = new HotkeyBinding();
        var cell = gridRow.Cells[HotkeyColumnIndex];
        cell.Value = string.Empty;
        cell.Style.ForeColor = DarkTheme.Accent;
        ShowStatus($"Press a shortcut for {row.FullName}.");
        grid.Focus();
    }

    private void RestoreDefaults()
    {
        var defaults = HotkeySettings.Defaults;
        foreach (var row in _rows)
        {
            row.Binding = row.GetDefaultBinding(defaults);
        }

        var defaultSettings = new AppSettings();
        SettingsNormalizer.Normalize(defaultSettings, _layouts);
        foreach (DataGridViewRow gridRow in _targetGrid.Rows)
        {
            if (gridRow.Tag is string sourceId &&
                defaultSettings.SwitchTargets.TryGetValue(sourceId, out var targetId))
            {
                gridRow.Cells["Target"].Value = targetId;
            }
        }

        _capturingRow = null;
        _capturingGridRow = null;
        RefreshHotkeyCells();
        ShowStatus("Defaults restored. Case and layout-specific hotkeys are empty.");
    }

    private void SaveButtonOnClick(object? sender, EventArgs eventArgs)
    {
        _targetGrid.EndEdit();
        var duplicate = _rows
            .Where(row => row.Binding.IsConfigured)
            .GroupBy(
                row => (row.Binding.Modifiers, row.Binding.Key))
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            ShowValidation(
                $"Duplicate shortcut: {HotkeyFormatter.Format(duplicate.First().Binding)}.");
            return;
        }

        var result = new HotkeySettings
        {
            TargetLayouts = new Dictionary<string, TargetLayoutHotkeys>(
                StringComparer.OrdinalIgnoreCase)
        };

        foreach (var action in TextSwitchActions.All)
        {
            action.SetBinding(result, FindGeneral(action.Mode).Binding.Clone());
        }

        foreach (var action in TextCaseActions.All)
        {
            action.SetBinding(result, FindCase(action.Mode).Binding.Clone());
        }

        foreach (var layout in _layouts)
        {
            var targetHotkeys = new TargetLayoutHotkeys
            {
                ActivateLayout = FindLayoutActivation(layout.Id).Binding.Clone()
            };
            foreach (var action in TextSwitchActions.All)
            {
                action.SetBinding(
                    targetHotkeys,
                    FindTarget(layout.Id, action.Mode).Binding.Clone());
            }

            result.TargetLayouts[layout.Id] = targetHotkeys;
        }

        Result = result;
        SwitchTargetsResult = ReadSwitchTargets();
        DialogResult = DialogResult.OK;
        Close();
    }

    private HotkeyRow FindGeneral(TextSwitchMode mode) =>
        _rows.Single(row =>
            row.TargetLayoutId is null && row.SwitchAction?.Mode == mode);

    private HotkeyRow FindCase(TextCaseMode mode) =>
        _rows.Single(row => row.CaseAction?.Mode == mode);

    private HotkeyRow FindTarget(string layoutId, TextSwitchMode mode) =>
        _rows.Single(row =>
            row.TargetLayoutId?.Equals(layoutId, StringComparison.OrdinalIgnoreCase) == true &&
            row.SwitchAction?.Mode == mode);

    private HotkeyRow FindLayoutActivation(string layoutId) =>
        _rows.Single(row =>
            row.TargetLayoutId?.Equals(layoutId, StringComparison.OrdinalIgnoreCase) == true &&
            row.IsLayoutActivation);

    private Dictionary<string, string> ReadSwitchTargets()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataGridViewRow row in _targetGrid.Rows)
        {
            if (row.Tag is string sourceId)
            {
                result[sourceId] = row.Cells["Target"].Value as string ?? string.Empty;
            }
        }

        return result;
    }

    private void RefreshHotkeyCells()
    {
        foreach (var grid in new[] { _universalGrid, _layoutGrid })
        {
            foreach (DataGridViewRow gridRow in grid.Rows)
            {
                if (gridRow.Tag is not HotkeyRow row)
                {
                    continue;
                }

                gridRow.Cells[HotkeyColumnIndex].Value =
                    HotkeyFormatter.Format(row.Binding);
                gridRow.Cells[HotkeyColumnIndex].Style.ForeColor =
                    DarkTheme.Foreground;
            }
        }
    }

    private static List<HotkeyRow> BuildRows(
        HotkeySettings current,
        IReadOnlyList<KeyboardLayoutDescriptor> layouts)
    {
        var rows = new List<HotkeyRow>();
        for (var index = 0; index < TextSwitchActions.All.Count; index++)
        {
            var action = TextSwitchActions.All[index];
            rows.Add(new HotkeyRow(
                index == 0 ? "Mapped target" : string.Empty,
                action,
                null,
                false,
                null,
                action.GetBinding(current).Clone()));
        }

        for (var index = 0; index < TextCaseActions.All.Count; index++)
        {
            var action = TextCaseActions.All[index];
            rows.Add(new HotkeyRow(
                index == 0 ? "Text case" : string.Empty,
                null,
                action,
                false,
                null,
                action.GetBinding(current).Clone()));
        }

        foreach (var layout in layouts)
        {
            var target = current.TargetLayouts.TryGetValue(layout.Id, out var configured)
                ? configured
                : new TargetLayoutHotkeys();
            rows.Add(new HotkeyRow(
                layout.DisplayName,
                null,
                null,
                true,
                layout.Id,
                target.ActivateLayout.Clone()));
        }

        foreach (var layout in layouts)
        {
            var target = current.TargetLayouts.TryGetValue(layout.Id, out var configured)
                ? configured
                : new TargetLayoutHotkeys();
            for (var index = 0; index < TextSwitchActions.All.Count; index++)
            {
                var action = TextSwitchActions.All[index];
                rows.Add(new HotkeyRow(
                    index == 0 ? layout.DisplayName : string.Empty,
                    action,
                    null,
                    false,
                    layout.Id,
                    action.GetBinding(target).Clone()));
            }
        }

        return rows;
    }

    private sealed class HotkeyRow
    {
        internal HotkeyRow(
            string scope,
            TextSwitchAction? switchAction,
            TextCaseAction? caseAction,
            bool isLayoutActivation,
            string? targetLayoutId,
            HotkeyBinding binding)
        {
            Scope = scope;
            SwitchAction = switchAction;
            CaseAction = caseAction;
            IsLayoutActivation = isLayoutActivation;
            TargetLayoutId = targetLayoutId;
            Binding = binding;
        }

        internal string Scope { get; }

        internal TextSwitchAction? SwitchAction { get; }

        internal TextCaseAction? CaseAction { get; }

        internal bool IsLayoutActivation { get; }

        internal string DisplayName =>
            SwitchAction?.DisplayName ??
            CaseAction?.DisplayName ??
            (IsLayoutActivation ? "Switch input language" : string.Empty);

        internal string? TargetLayoutId { get; }

        internal HotkeyBinding Binding { get; set; }

        internal HotkeyBinding GetDefaultBinding(HotkeySettings defaults)
        {
            if (CaseAction is not null)
            {
                return CaseAction.GetBinding(defaults).Clone();
            }

            return TargetLayoutId is null && SwitchAction is not null
                ? SwitchAction.GetBinding(defaults).Clone()
                : new HotkeyBinding();
        }

        internal string FullName =>
            string.IsNullOrEmpty(Scope) ? DisplayName : $"{Scope}: {DisplayName}";
    }

    private sealed record TargetChoice(string Id, string Name);
}
