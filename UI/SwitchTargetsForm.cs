using INSwitch.Models;
using INSwitch.Services;

namespace INSwitch.UI;

internal sealed class SwitchTargetsForm : Form
{
    private readonly IReadOnlyList<KeyboardLayoutDescriptor> _layouts;
    private readonly DataGridView _grid;

    internal Dictionary<string, string> Result { get; private set; }

    internal SwitchTargetsForm(
        IReadOnlyList<KeyboardLayoutDescriptor> layouts,
        IReadOnlyDictionary<string, string> currentTargets)
    {
        _layouts = layouts;
        Result = new Dictionary<string, string>(currentTargets, StringComparer.OrdinalIgnoreCase);

        Text = "Switch to — NN Switch";
        ClientSize = new Size(760, 430);
        MinimumSize = new Size(650, 380);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;

        var title = new Label
        {
            Text = "Switch targets",
            Font = new Font("Segoe UI Semibold", 15F),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        };

        var description = new Label
        {
            Text = "For each current keyboard layout, choose the layout that corrected text should use.",
            ForeColor = DarkTheme.Muted,
            AutoSize = true,
            MaximumSize = new Size(690, 0),
            Margin = new Padding(0, 0, 0, 16)
        };

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = false,
            RowHeadersVisible = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            EditMode = DataGridViewEditMode.EditOnEnter,
            RowTemplate = { Height = 34 }
        };
        _grid.DataError += (_, _) => { };
        _grid.CellClick += (_, eventArgs) =>
        {
            if (eventArgs.RowIndex < 0 || eventArgs.ColumnIndex != 1)
            {
                return;
            }

            _grid.BeginEdit(selectAll: true);
            if (_grid.EditingControl is DataGridViewComboBoxEditingControl comboBox)
            {
                comboBox.BackColor = DarkTheme.Background;
                comboBox.ForeColor = DarkTheme.Foreground;
                comboBox.FlatStyle = FlatStyle.Flat;
                comboBox.DroppedDown = true;
            }
        };

        _grid.Columns.Add(new DataGridViewTextBoxColumn
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
        choices.AddRange(layouts.Select(layout => new TargetChoice(layout.Id, layout.DisplayName)));

        _grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "Target",
            HeaderText = "Switch text to",
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
                           choices.Any(choice => choice.Id.Equals(configuredTarget, StringComparison.OrdinalIgnoreCase))
                ? configuredTarget
                : string.Empty;

            var rowIndex = _grid.Rows.Add(layout.DisplayName, targetId);
            _grid.Rows[rowIndex].Tag = layout.Id;
        }

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
            WrapContents = false,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 14, 0, 0)
        };
        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(cancelButton);

        var content = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 4,
            Dock = DockStyle.Fill,
            Padding = new Padding(26, 22, 26, 22)
        };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.Controls.Add(title, 0, 0);
        content.Controls.Add(description, 0, 1);
        content.Controls.Add(_grid, 0, 2);
        content.Controls.Add(buttons, 0, 3);
        Controls.Add(content);

        CancelButton = cancelButton;
        DarkTheme.Apply(this);
        DarkTheme.StyleGrid(_grid);
        DarkTheme.EnableAccentHover(saveButton);
    }

    private static Button CreateButton(string text, EventHandler onClick)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(105, 34),
            Margin = new Padding(8, 0, 0, 0)
        };
        button.Click += onClick;
        return button;
    }

    private void SaveButtonOnClick(object? sender, EventArgs eventArgs)
    {
        _grid.EndEdit();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Tag is not string sourceId)
            {
                continue;
            }

            result[sourceId] = row.Cells["Target"].Value as string ?? string.Empty;
        }

        Result = result;
        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed record TargetChoice(string Id, string Name);
}
