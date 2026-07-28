using INSwitch.Models;
using INSwitch.Services;
using INSwitch.UI;

namespace INSwitch;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SettingsStore _settingsStore = new();
    private readonly ContextMenuStrip _trayMenu;
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _appIcon;
    private readonly HotkeyManager _hotkeyManager;
    private readonly TextSwitchService _textSwitchService;
    private IReadOnlyList<KeyboardLayoutDescriptor> _layouts;
    private readonly AppSettings _settings;
    private bool _exiting;

    internal TrayApplicationContext()
    {
        _layouts = KeyboardLayoutService.GetInstalled();
        _settings = _settingsStore.Load(_layouts);
        _appIcon = TrayIconFactory.Create();

        var hotkeysItem = new ToolStripMenuItem("Hotkeys...");
        hotkeysItem.Click += (_, _) => ShowHotkeysForm();

        var switchTargetsItem = new ToolStripMenuItem("Switch to...");
        switchTargetsItem.Click += (_, _) => ShowSwitchTargetsForm();

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApplication();

        _trayMenu = new ContextMenuStrip
        {
            ShowCheckMargin = false
        };
        _trayMenu.Items.AddRange(new ToolStripItem[]
        {
            hotkeysItem,
            switchTargetsItem,
            new ToolStripSeparator(),
            exitItem
        });
        DarkTheme.Apply(_trayMenu);

        _notifyIcon = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "NN Switch",
            ContextMenuStrip = _trayMenu,
            Visible = true
        };
        _notifyIcon.MouseClick += NotifyIconOnMouseClick;

        _textSwitchService = new TextSwitchService(
            () => _settings,
            () => _layouts,
            ShowNotification);

        _hotkeyManager = new HotkeyManager(HandleHotkey);

        ApplyFunctionalState();
    }

    private void ShowHotkeysForm()
    {
        RefreshLayouts();
        _hotkeyManager.UnregisterAll();

        try
        {
            using var form = new HotkeysForm(_settings.Hotkeys, _layouts)
            {
                Icon = _appIcon
            };
            if (form.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            _settings.Hotkeys = form.Result;
            SaveSettings();
        }
        finally
        {
            ApplyFunctionalState();
        }
    }

    private void ShowSwitchTargetsForm()
    {
        RefreshLayouts();
        using var form = new SwitchTargetsForm(_layouts, _settings.SwitchTargets)
        {
            Icon = _appIcon
        };
        if (form.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        _settings.SwitchTargets = form.Result;
        SaveSettings();
    }

    private void RefreshLayouts()
    {
        _layouts = KeyboardLayoutService.GetInstalled();
        if (SettingsNormalizer.Normalize(_settings, _layouts))
        {
            SaveSettings();
        }
    }

    private void ApplyFunctionalState()
    {
        var registrationErrors = _hotkeyManager.RegisterAll(_settings.Hotkeys, _layouts);
        if (registrationErrors.Count > 0)
        {
            ShowNotification(
                "Some hotkeys are unavailable",
                string.Join(Environment.NewLine, registrationErrors));
        }
    }

    private async void HandleHotkey(HotkeyCommand command)
    {
        await _textSwitchService.SwitchAsync(
            command.Mode,
            targetLayoutId: command.TargetLayoutId);
    }

    private void NotifyIconOnMouseClick(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button == MouseButtons.Left)
        {
            _trayMenu.Show(Cursor.Position);
        }
    }

    private void SaveSettings()
    {
        if (!_settingsStore.Save(_settings))
        {
            ShowNotification(
                "Settings were not saved",
                "NN Switch could not write settings.json. See error.log for details.");
        }
    }

    private void ShowNotification(string title, string message)
    {
        if (!_exiting && _notifyIcon.Visible)
        {
            _notifyIcon.ShowBalloonTip(3500, title, message, ToolTipIcon.Warning);
        }
    }

    private void ExitApplication()
    {
        _exiting = true;
        _notifyIcon.Visible = false;
        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        _exiting = true;
        _hotkeyManager.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _trayMenu.Dispose();
        _appIcon.Dispose();
        base.ExitThreadCore();
    }
}
