using System.ComponentModel;
using INSwitch.Models;
using INSwitch.Services;
using INSwitch.UI;

namespace INSwitch;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SettingsStore _settingsStore = new();
    private readonly ContextMenuStrip _trayMenu;
    private readonly ToolStripMenuItem _autoswitchItem;
    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _appIcon;
    private readonly HotkeyManager _hotkeyManager;
    private readonly AutoSwitchMonitor _autoSwitchMonitor;
    private readonly TextSwitchService _textSwitchService;
    private IReadOnlyList<KeyboardLayoutDescriptor> _layouts;
    private readonly AppSettings _settings;
    private bool _exiting;

    internal TrayApplicationContext()
    {
        _layouts = KeyboardLayoutService.GetInstalled();
        _settings = _settingsStore.Load(_layouts);
        _settingsStore.Save(_settings);
        _appIcon = TrayIconFactory.Create();

        _autoswitchItem = new ToolStripMenuItem("Autoswitch")
        {
            CheckOnClick = false
        };
        _autoswitchItem.Click += (_, _) => ToggleAutoswitch();

        var hotkeysItem = new ToolStripMenuItem("Hotkeys...");
        hotkeysItem.Click += (_, _) => ShowHotkeysForm();

        var switchTargetsItem = new ToolStripMenuItem("Switch to...");
        switchTargetsItem.Click += (_, _) => ShowSwitchTargetsForm();

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApplication();

        _trayMenu = new ContextMenuStrip
        {
            ShowCheckMargin = true
        };
        _trayMenu.Items.AddRange(new ToolStripItem[]
        {
            _autoswitchItem,
            hotkeysItem,
            switchTargetsItem,
            new ToolStripSeparator(),
            exitItem
        });
        _trayMenu.Opening += TrayMenuOnOpening;
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
        _autoSwitchMonitor = new AutoSwitchMonitor(
            () => _settings,
            () => _layouts,
            () => _textSwitchService.SwitchAsync(TextSwitchMode.LastWord, showFailure: false));

        UpdateMenuState();
        ApplyFunctionalState();
    }

    private void ToggleAutoswitch()
    {
        _settings.AutoSwitch = !_settings.AutoSwitch;
        _settingsStore.Save(_settings);
        UpdateMenuState();
        ApplyFunctionalState();
    }

    private void ShowHotkeysForm()
    {
        RefreshLayouts();
        _hotkeyManager.UnregisterAll();
        _autoSwitchMonitor.Stop();

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
            _settingsStore.Save(_settings);
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
        _settingsStore.Save(_settings);
    }

    private void RefreshLayouts()
    {
        _layouts = KeyboardLayoutService.GetInstalled();
        SettingsStore.Normalize(_settings, _layouts);
        _settingsStore.Save(_settings);
    }

    private void ApplyFunctionalState()
    {
        _hotkeyManager.UnregisterAll();
        _autoSwitchMonitor.Stop();

        var registrationErrors = _hotkeyManager.RegisterAll(_settings.Hotkeys, _layouts);
        if (registrationErrors.Count > 0)
        {
            ShowNotification(
                "Some hotkeys are unavailable",
                string.Join(Environment.NewLine, registrationErrors));
        }

        if (_settings.AutoSwitch && !_autoSwitchMonitor.Start())
        {
            ShowNotification(
                "Autoswitch could not start",
                new Win32Exception().Message);
        }
    }

    private async void HandleHotkey(HotkeyCommand command)
    {
        await _textSwitchService.SwitchAsync(
            command.Mode,
            targetLayoutId: command.TargetLayoutId);
    }

    private void TrayMenuOnOpening(object? sender, CancelEventArgs eventArgs)
    {
        UpdateMenuState();
    }

    private void NotifyIconOnMouseClick(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button == MouseButtons.Left)
        {
            UpdateMenuState();
            _trayMenu.Show(Cursor.Position);
        }
    }

    private void UpdateMenuState()
    {
        _autoswitchItem.Checked = _settings.AutoSwitch;
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
        _autoSwitchMonitor.Dispose();
        _hotkeyManager.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _trayMenu.Dispose();
        _appIcon.Dispose();
        base.ExitThreadCore();
    }
}
