using INSwitch.Interop;

namespace INSwitch.UI;

internal static class DarkTheme
{
    internal static readonly Color Background = ColorTranslator.FromHtml("#202227");
    internal static readonly Color Button = ColorTranslator.FromHtml("#2B2E34");
    internal static readonly Color Row = ColorTranslator.FromHtml("#191B1F");
    internal static readonly Color AlternateRow = ColorTranslator.FromHtml("#1C1E22");
    internal static readonly Color Foreground = Color.FromArgb(244, 245, 247);
    internal static readonly Color Muted = Color.FromArgb(168, 172, 181);
    internal static readonly Color Accent = ColorTranslator.FromHtml("#A4FF4A");
    internal static readonly Color Danger = Color.FromArgb(238, 112, 112);

    internal static void Apply(Form form)
    {
        form.BackColor = Background;
        form.ForeColor = Foreground;
        form.Font = new Font("Segoe UI", 9F);
        form.HandleCreated += (_, _) => EnableDarkTitleBar(form);
        ApplyToControls(form.Controls);
    }

    internal static void Apply(ContextMenuStrip menu)
    {
        menu.BackColor = Background;
        menu.ForeColor = Foreground;
        menu.Font = new Font("Segoe UI", 9F);
        menu.Renderer = new DarkToolStripRenderer();
        menu.ShowImageMargin = false;
        ApplyToMenuItems(menu.Items);
    }

    internal static void StyleGrid(DataGridView grid)
    {
        grid.BackgroundColor = Background;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.None;
        grid.GridColor = Row;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Background,
            ForeColor = Foreground,
            SelectionBackColor = Background,
            SelectionForeColor = Foreground,
            Padding = new Padding(8, 3, 8, 3),
            Font = new Font("Segoe UI Semibold", 9F)
        };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Row,
            ForeColor = Foreground,
            SelectionBackColor = Button,
            SelectionForeColor = Foreground,
            Padding = new Padding(8, 2, 8, 2)
        };
        grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = AlternateRow,
            ForeColor = Foreground,
            SelectionBackColor = Button,
            SelectionForeColor = Foreground,
            Padding = new Padding(8, 2, 8, 2)
        };
        grid.HandleCreated += (_, _) =>
            NativeMethods.SetWindowTheme(grid.Handle, "DarkMode_Explorer", null);
    }

    internal static void EnableAccentHover(Button button)
    {
        button.BackColor = Button;
        button.ForeColor = Foreground;
        button.FlatAppearance.MouseOverBackColor = Accent;
        button.MouseEnter += (_, _) =>
        {
            button.BackColor = Accent;
            button.ForeColor = Background;
        };
        button.MouseLeave += (_, _) =>
        {
            button.BackColor = Button;
            button.ForeColor = Foreground;
        };
    }

    internal static Button CreateButton(
        string text,
        EventHandler onClick,
        Size minimumSize)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = minimumSize,
            Margin = new Padding(7, 0, 0, 0)
        };
        button.Click += onClick;
        return button;
    }

    private static void ApplyToControls(Control.ControlCollection controls)
    {
        foreach (Control control in controls)
        {
            if (control.ForeColor != Muted &&
                control.ForeColor != Danger &&
                control.ForeColor != Accent)
            {
                control.ForeColor = Foreground;
            }

            switch (control)
            {
                case Button button:
                    button.BackColor = Button;
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderSize = 0;
                    button.FlatAppearance.MouseOverBackColor = Button;
                    button.FlatAppearance.MouseDownBackColor = Button;
                    button.UseVisualStyleBackColor = false;
                    break;

                case TextBox textBox:
                    textBox.BackColor = Background;
                    textBox.BorderStyle = BorderStyle.None;
                    break;

                case ComboBox comboBox:
                    comboBox.BackColor = Background;
                    comboBox.FlatStyle = FlatStyle.Flat;
                    break;

                case Panel or TableLayoutPanel or FlowLayoutPanel:
                    control.BackColor = Background;
                    break;
            }

            if (control.HasChildren)
            {
                ApplyToControls(control.Controls);
            }
        }
    }

    private static void ApplyToMenuItems(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            item.BackColor = Background;
            item.ForeColor = Foreground;

            if (item is ToolStripMenuItem menuItem)
            {
                menuItem.DropDown.BackColor = Background;
                menuItem.DropDown.ForeColor = Foreground;
                ApplyToMenuItems(menuItem.DropDownItems);
            }
        }
    }

    private static void EnableDarkTitleBar(Form form)
    {
        try
        {
            var enabled = 1;
            const int immersiveDarkMode = 20;
            const int immersiveDarkModeLegacy = 19;
            if (NativeMethods.DwmSetWindowAttribute(
                    form.Handle,
                    immersiveDarkMode,
                    ref enabled,
                    sizeof(int)) != 0)
            {
                NativeMethods.DwmSetWindowAttribute(
                    form.Handle,
                    immersiveDarkModeLegacy,
                    ref enabled,
                    sizeof(int));
            }
        }
        catch
        {
            // Older Windows versions simply keep the system title bar.
        }
    }

    private sealed class DarkToolStripRenderer : ToolStripProfessionalRenderer
    {
        internal DarkToolStripRenderer()
            : base(new DarkColorTable())
        {
        }

    }

    private sealed class DarkColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Background;
        public override Color ImageMarginGradientBegin => Background;
        public override Color ImageMarginGradientMiddle => Background;
        public override Color ImageMarginGradientEnd => Background;
        public override Color MenuBorder => Background;
        public override Color MenuItemBorder => Button;
        public override Color MenuItemSelected => Button;
        public override Color MenuItemSelectedGradientBegin => Button;
        public override Color MenuItemSelectedGradientEnd => Button;
        public override Color MenuItemPressedGradientBegin => Button;
        public override Color MenuItemPressedGradientMiddle => Button;
        public override Color MenuItemPressedGradientEnd => Button;
        public override Color SeparatorDark => Button;
        public override Color SeparatorLight => Button;
    }
}
