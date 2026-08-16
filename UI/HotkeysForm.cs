using INSwitch.Interop;
using INSwitch.Models;
using INSwitch.Services;

namespace INSwitch.UI;

internal sealed class HotkeysForm : Form
{
    private const string HotkeyColumnName = "Hotkey";
    private const string TargetColumnName = "Target";

    private readonly IReadOnlyList<KeyboardLayoutDescriptor> _layouts;
    private readonly List<HotkeyRow> _rows;
    private readonly DataGridView _universalGrid;
    private readonly DataGridView _layoutGrid;
    private readonly DataGridView _targetGrid;
    private readonly Label _statusLabel;
    private readonly Label _validationLabel;
    private readonly Action<bool>? _setHotkeyCaptureActive;
    private HotkeyRow? _capturingRow;
    private DataGridViewRow? _capturingGridRow;
    private bool _hotkeyCaptureActive;

    internal HotkeySettings Result { get; private set; }

    internal Dictionary<string, string> SwitchTargetsResult { get; private set; }

    internal HotkeysForm(
        HotkeySettings current,
        IReadOnlyDictionary<string, string> currentTargets,
        IReadOnlyList<KeyboardLayoutDescriptor> layouts,
        Action<bool>? setHotkeyCaptureActive = null)
    {
        _layouts = layouts;
        _setHotkeyCaptureActive = setHotkeyCaptureActive;
        Result = current.Clone();
        SwitchTargetsResult = new Dictionary<string, string>(
            currentTargets,
            StringComparer.OrdinalIgnoreCase);
        _rows = BuildRows(current, layouts);

        Text = "Hotkeys — NN Switch";
        ClientSize = new Size(560, CalculateInitialClientHeight(layouts.Count));
        MinimumSize = new Size(500, 600);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        KeyPreview = true;

        var title = new Label
        {
            Text = "Hotkeys",
            Font = new Font("Segoe UI Semibold", 14F),
            ForeColor = DarkTheme.Accent,
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

        _universalGrid = CreateGrid("UniversalHotkeyGrid", includeTarget: false);
        _layoutGrid = CreateGrid("LayoutHotkeyGrid", includeTarget: true);
        _targetGrid = CreateTargetGrid(layouts, currentTargets);
        PopulateGrid(
            _universalGrid,
            _rows.Where(row => !row.IsLayoutSection));
        PopulateGrid(
            _layoutGrid,
            _rows.Where(row => row.IsLayoutSection));
        AddLayoutGroupSpacing(_layoutGrid, layouts.Count);

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

        WireOutsideClickHandlers(content);
        MouseDown += OutsideControlOnMouseDown;
        Deactivate += (_, _) => FinishActiveEditors();
        FormClosed += (_, _) => FinishActiveEditors();

        CancelButton = cancelButton;
        DarkTheme.Apply(this);
        DarkTheme.StyleGrid(_universalGrid);
        DarkTheme.StyleGrid(_layoutGrid);
        DarkTheme.StyleGrid(_targetGrid);
        _layoutGrid.GridColor = DarkTheme.Background;
        DarkTheme.EnableAccentHover(saveButton);
        Shown += (_, _) => ClearAllGridSelections();
    }

    private static int CalculateInitialClientHeight(int layoutCount)
    {
        const int hotkeyHeaderHeight = 30;
        const int hotkeyRowHeight = 28;
        const int targetRowHeight = 30;
        const int groupGapHeight = 8;
        const int labelsAndFormChromeHeight = 220;

        var universalGridHeight = hotkeyHeaderHeight +
            ((TextSwitchActions.All.Count + TextCaseActions.All.Count) * hotkeyRowHeight);
        var targetGridHeight = hotkeyHeaderHeight + (layoutCount * targetRowHeight);
        var layoutGridHeight = hotkeyHeaderHeight +
            ((1 + (layoutCount * 4)) * hotkeyRowHeight) +
            groupGapHeight;
        var desiredHeight = labelsAndFormChromeHeight +
            universalGridHeight +
            targetGridHeight +
            layoutGridHeight;
        var workingHeight = Screen.PrimaryScreen?.WorkingArea.Height ?? 900;
        return Math.Clamp(desiredHeight, 720, Math.Max(720, workingHeight - 70));
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
        var hotkeyCell = _capturingGridRow.Cells[HotkeyColumnName];
        hotkeyCell.Value =
            HotkeyFormatter.Format(_capturingRow.Binding);
        hotkeyCell.Style.ForeColor = DarkTheme.Foreground;
        _capturingGridRow.DataGridView?.InvalidateCell(hotkeyCell);
        ClearFeedback();
        return true;
    }

    private DataGridView CreateGrid(string name, bool includeTarget)
    {
        var grid = new DataGridView
        {
            Name = name,
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

        if (includeTarget)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Scope",
                HeaderText = "Target",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 38F,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Action",
            HeaderText = "Action",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = includeTarget ? 34F : 72F,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = HotkeyColumnName,
            HeaderText = "Hotkey",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 28F,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });

        grid.CellClick += GridOnCellClick;
        grid.CellMouseDown += HotkeyGridOnCellMouseDown;
        grid.CellPainting += HotkeyGridOnCellPainting;
        return grid;
    }

    private DataGridView CreateTargetGrid(
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
        grid.CellClick += TargetGridOnCellClick;
        grid.CellMouseDown += TargetGridOnCellMouseDown;
        grid.Leave += (_, _) => FinishTargetEditing();

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
            Name = TargetColumnName,
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
            ForeColor = DarkTheme.Accent,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        };

        var layoutTitle = new Label
        {
            Name = "LayoutSectionTitle",
            Text = "By installed layout",
            Font = new Font("Segoe UI Semibold", 12F),
            ForeColor = DarkTheme.Accent,
            AutoSize = true,
            Margin = new Padding(0, 14, 0, 6)
        };

        var universalGridHeight = GetGridContentHeight(universalGrid);
        var targetGridHeight = GetGridContentHeight(targetGrid);
        var sections = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 5,
            Dock = DockStyle.Fill,
            Margin = new Padding(0)
        };
        sections.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sections.RowStyles.Add(new RowStyle(SizeType.Absolute, universalGridHeight));
        sections.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sections.RowStyles.Add(new RowStyle(SizeType.Absolute, targetGridHeight));
        sections.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        universalGrid.Margin = new Padding(0);
        targetGrid.Margin = new Padding(0);
        layoutGrid.Margin = new Padding(0, 8, 0, 0);
        sections.Controls.Add(universalTitle, 0, 0);
        sections.Controls.Add(universalGrid, 0, 1);
        sections.Controls.Add(layoutTitle, 0, 2);
        sections.Controls.Add(targetGrid, 0, 3);
        sections.Controls.Add(layoutGrid, 0, 4);
        return sections;
    }

    private static int GetGridContentHeight(DataGridView grid) =>
        grid.ColumnHeadersHeight +
        grid.Rows.Cast<DataGridViewRow>().Sum(row => row.Height + row.DividerHeight) +
        2;

    private static void PopulateGrid(
        DataGridView grid,
        IEnumerable<HotkeyRow> rows)
    {
        foreach (var row in rows)
        {
            var rowIndex = grid.Columns.Contains("Scope")
                ? grid.Rows.Add(
                    row.Scope,
                    row.DisplayName,
                    HotkeyFormatter.Format(row.Binding))
                : grid.Rows.Add(
                    row.DisplayName,
                    HotkeyFormatter.Format(row.Binding));
            grid.Rows[rowIndex].Tag = row;
        }
    }

    private static void AddLayoutGroupSpacing(DataGridView grid, int layoutCount)
    {
        var separatorIndex = 1 + layoutCount;
        if (separatorIndex >= 0 && separatorIndex <= grid.Rows.Count)
        {
            grid.Rows.Insert(separatorIndex, 1);
            var separator = grid.Rows[separatorIndex];
            separator.Height = 8;
            separator.MinimumHeight = 3;
            separator.ReadOnly = true;
            separator.DefaultCellStyle.BackColor = DarkTheme.Background;
            separator.DefaultCellStyle.SelectionBackColor = DarkTheme.Background;
        }
    }

    private void GridOnCellClick(object? sender, DataGridViewCellEventArgs eventArgs)
    {
        if (sender is not DataGridView grid)
        {
            return;
        }

        var hotkeyColumnIndex = GetColumnIndex(grid, HotkeyColumnName);
        if (eventArgs.RowIndex < 0 || eventArgs.ColumnIndex != hotkeyColumnIndex)
        {
            return;
        }

        if (_capturingGridRow is not null)
        {
            _capturingGridRow.DataGridView?.InvalidateCell(
                _capturingGridRow.Cells[HotkeyColumnName]);
        }

        var gridRow = grid.Rows[eventArgs.RowIndex];
        if (gridRow.Tag is not HotkeyRow row)
        {
            return;
        }

        _capturingRow = row;
        _capturingGridRow = gridRow;
        row.Binding = new HotkeyBinding();
        var cell = gridRow.Cells[HotkeyColumnName];
        cell.Value = string.Empty;
        cell.Style.ForeColor = DarkTheme.Foreground;
        SetHotkeyCaptureActive(active: true);
        ClearFeedback();
        grid.InvalidateCell(cell);
        grid.Focus();
    }

    private void HotkeyGridOnCellMouseDown(
        object? sender,
        DataGridViewCellMouseEventArgs eventArgs)
    {
        FinishTargetEditing();
        if (sender is not DataGridView grid ||
            eventArgs.RowIndex < 0 ||
            eventArgs.ColumnIndex != GetColumnIndex(grid, HotkeyColumnName))
        {
            FinishHotkeyCapture();
        }
    }

    private void HotkeyGridOnCellPainting(
        object? sender,
        DataGridViewCellPaintingEventArgs eventArgs)
    {
        if (_capturingGridRow is null ||
            sender is not DataGridView grid ||
            _capturingGridRow.DataGridView != grid ||
            eventArgs.RowIndex != _capturingGridRow.Index ||
            eventArgs.ColumnIndex != GetColumnIndex(grid, HotkeyColumnName))
        {
            return;
        }

        eventArgs.Paint(
            eventArgs.CellBounds,
            eventArgs.PaintParts & ~DataGridViewPaintParts.Border);
        var border = Rectangle.Inflate(eventArgs.CellBounds, -1, -1);
        using var pen = new Pen(DarkTheme.Accent, 2F);
        eventArgs.Graphics?.DrawRectangle(pen, border);
        eventArgs.Handled = true;
    }

    private void TargetGridOnCellClick(object? sender, DataGridViewCellEventArgs eventArgs)
    {
        if (sender is not DataGridView grid || eventArgs.RowIndex < 0)
        {
            return;
        }

        if (eventArgs.ColumnIndex != GetColumnIndex(grid, TargetColumnName))
        {
            BeginInvoke(FinishTargetEditing);
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
    }

    private void TargetGridOnCellMouseDown(
        object? sender,
        DataGridViewCellMouseEventArgs eventArgs)
    {
        FinishHotkeyCapture();
        if (sender is not DataGridView grid ||
            eventArgs.RowIndex < 0 ||
            eventArgs.ColumnIndex != GetColumnIndex(grid, TargetColumnName))
        {
            FinishTargetEditing();
        }
    }

    private void WireOutsideClickHandlers(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is DataGridView)
            {
                continue;
            }

            child.MouseDown += OutsideControlOnMouseDown;
            WireOutsideClickHandlers(child);
        }
    }

    private void OutsideControlOnMouseDown(object? sender, MouseEventArgs eventArgs) =>
        FinishActiveEditors();

    private void FinishActiveEditors()
    {
        FinishTargetEditing();
        FinishHotkeyCapture();
    }

    private void FinishTargetEditing()
    {
        if (_targetGrid.EditingControl is DataGridViewComboBoxEditingControl comboBox)
        {
            comboBox.DroppedDown = false;
        }

        _targetGrid.EndEdit();
        _targetGrid.ClearSelection();
        _targetGrid.CurrentCell = null;
        _targetGrid.Invalidate();
    }

    private void FinishHotkeyCapture()
    {
        if (_capturingGridRow is null)
        {
            return;
        }

        var grid = _capturingGridRow.DataGridView;
        var cell = _capturingGridRow.Cells[HotkeyColumnName];
        _capturingRow = null;
        _capturingGridRow = null;
        SetHotkeyCaptureActive(active: false);
        ClearFeedback();
        if (grid is not null)
        {
            grid.ClearSelection();
            grid.CurrentCell = null;
            grid.InvalidateCell(cell);
        }
    }

    private void SetHotkeyCaptureActive(bool active)
    {
        if (_hotkeyCaptureActive == active)
        {
            return;
        }

        _hotkeyCaptureActive = active;
        _setHotkeyCaptureActive?.Invoke(active);
    }

    private static int GetColumnIndex(DataGridView grid, string columnName) =>
        grid.Columns[columnName]?.Index ?? -1;

    private void ClearAllGridSelections()
    {
        foreach (var grid in new[] { _universalGrid, _targetGrid, _layoutGrid })
        {
            grid.ClearSelection();
            grid.CurrentCell = null;
        }

        ActiveControl = null;
    }

    private void RestoreDefaults()
    {
        FinishActiveEditors();
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
                gridRow.Cells[TargetColumnName].Value = targetId;
            }
        }

        RefreshHotkeyCells();
        ShowStatus("Defaults restored. Case and layout-specific hotkeys are empty.");
    }

    private void SaveButtonOnClick(object? sender, EventArgs eventArgs)
    {
        FinishActiveEditors();
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
            CycleLayout = FindCycleLayout().Binding.Clone(),
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

    private HotkeyRow FindCycleLayout() =>
        _rows.Single(row => row.IsCycleLayout);

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
                result[sourceId] = row.Cells[TargetColumnName].Value as string ?? string.Empty;
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

                gridRow.Cells[HotkeyColumnName].Value =
                    HotkeyFormatter.Format(row.Binding);
                gridRow.Cells[HotkeyColumnName].Style.ForeColor =
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
                false,
                false,
                null,
                action.GetBinding(current).Clone()));
        }

        for (var index = 0; index < TextCaseActions.All.Count; index++)
        {
            var action = TextCaseActions.All[index];
            rows.Add(new HotkeyRow(
                string.Empty,
                null,
                action,
                false,
                false,
                false,
                null,
                action.GetBinding(current).Clone()));
        }

        rows.Add(new HotkeyRow(
            string.Empty,
            null,
            null,
            false,
            true,
            true,
            null,
            current.CycleLayout.Clone()));

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
                false,
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
                    false,
                    true,
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
            bool isCycleLayout,
            bool isLayoutSection,
            string? targetLayoutId,
            HotkeyBinding binding)
        {
            Scope = scope;
            SwitchAction = switchAction;
            CaseAction = caseAction;
            IsLayoutActivation = isLayoutActivation;
            IsCycleLayout = isCycleLayout;
            IsLayoutSection = isLayoutSection;
            TargetLayoutId = targetLayoutId;
            Binding = binding;
        }

        internal string Scope { get; }

        internal TextSwitchAction? SwitchAction { get; }

        internal TextCaseAction? CaseAction { get; }

        internal bool IsLayoutActivation { get; }

        internal bool IsCycleLayout { get; }

        internal bool IsLayoutSection { get; }

        internal string DisplayName =>
            SwitchAction?.DisplayName ??
            (CaseAction is not null ? $"Selected to {CaseAction.DisplayName}" : null) ??
            (IsCycleLayout ? "Cycle input language" : null) ??
            (IsLayoutActivation ? "Switch input language" : string.Empty);

        internal string? TargetLayoutId { get; }

        internal HotkeyBinding Binding { get; set; }

        internal HotkeyBinding GetDefaultBinding(HotkeySettings defaults)
        {
            if (CaseAction is not null)
            {
                return CaseAction.GetBinding(defaults).Clone();
            }

            if (IsCycleLayout)
            {
                return defaults.CycleLayout.Clone();
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
