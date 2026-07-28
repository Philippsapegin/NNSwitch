using System.Text;
using System.Diagnostics;
using INSwitch.Interop;
using INSwitch.Models;
using INSwitch.Services;
using INSwitch.UI;

namespace INSwitch.Tests;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        try
        {
            var layouts = KeyboardLayoutService.GetInstalled();
            Assert(layouts.Count > 0, "Windows reports at least one installed keyboard layout.");
            TestSettingsDefaults(layouts);
            TestHotkeyFormatting();
            TestRussianEnglishConversion(layouts);

            if (args.Contains("--integration", StringComparer.OrdinalIgnoreCase))
            {
                var executablePath = GetOptionValue(args, "--integration") ??
                    throw new InvalidOperationException("--integration requires the app executable path.");
                TestHotkeyDelivery(layouts);
                TestSelectedTextIntegration(layouts, executablePath);
            }

            if (args.Contains("--screenshots", StringComparer.OrdinalIgnoreCase))
            {
                var outputDirectory = GetOptionValue(args, "--screenshots") ??
                    Path.Combine(AppContext.BaseDirectory, "ui");
                CaptureUi(layouts, outputDirectory);
            }

            Console.WriteLine("All INSwitch tests passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void TestSettingsDefaults(IReadOnlyList<KeyboardLayoutDescriptor> layouts)
    {
        var settings = new AppSettings();
        SettingsStore.Normalize(settings, layouts);

        foreach (var source in layouts)
        {
            Assert(
                settings.SwitchTargets.ContainsKey(source.Id),
                $"A switch target entry exists for {source.DisplayName}.");
            Assert(
                settings.Hotkeys.TargetLayouts.ContainsKey(source.Id),
                $"Language-specific hotkeys exist for {source.DisplayName}.");
            Assert(
                !settings.Hotkeys.TargetLayouts[source.Id].SelectedText.IsConfigured &&
                !settings.Hotkeys.TargetLayouts[source.Id].LastWord.IsConfigured &&
                !settings.Hotkeys.TargetLayouts[source.Id].ActiveField.IsConfigured,
                $"Language-specific hotkeys default to empty for {source.DisplayName}.");
        }

        var english = layouts.FirstOrDefault(layout => layout.TwoLetterLanguage == "en");
        var russian = layouts.FirstOrDefault(layout => layout.TwoLetterLanguage == "ru");
        if (english is not null && russian is not null)
        {
            Assert(
                settings.SwitchTargets[english.Id] == russian.Id,
                "English defaults to Russian when both are installed.");
            Assert(
                settings.SwitchTargets[russian.Id] == english.Id,
                "Russian defaults to English when both are installed.");
        }
    }

    private static void TestHotkeyFormatting()
    {
        var binding = HotkeyBinding.Create(
            HotkeyModifiers.Control | HotkeyModifiers.Alt,
            Keys.S);
        Assert(
            HotkeyFormatter.Format(binding) == "Ctrl + Alt + S",
            "Hotkey labels use the expected English format.");

        var pauseBinding = HotkeyBinding.Create(HotkeyModifiers.None, Keys.Pause);
        Assert(pauseBinding.IsConfigured, "Pause is valid as a standalone global hotkey.");
        Assert(
            HotkeyFormatter.Format(pauseBinding) == "Pause",
            "Standalone Pause uses the expected hotkey label.");

        var unmodifiedLetter = HotkeyBinding.Create(HotkeyModifiers.None, Keys.S);
        Assert(
            unmodifiedLetter.IsConfigured &&
            HotkeyFormatter.Format(unmodifiedLetter) == "S",
            "An ordinary letter can be used without a modifier.");
    }

    private static void TestRussianEnglishConversion(IReadOnlyList<KeyboardLayoutDescriptor> layouts)
    {
        var english = layouts.FirstOrDefault(layout => layout.TwoLetterLanguage == "en");
        var russian = layouts.FirstOrDefault(layout => layout.TwoLetterLanguage == "ru");
        if (english is null || russian is null)
        {
            Console.WriteLine("SKIP: Russian/English conversion test (both layouts are not installed).");
            return;
        }

        var russianText = KeyboardLayoutService.ConvertText("Ghbdtn? vbh!", english, russian);
        Assert(russianText == "Привет, мир!", "English key positions convert to Russian text.");

        var englishText = KeyboardLayoutService.ConvertText("руддщ цщкдв", russian, english);
        Assert(englishText == "hello world", "Russian key positions convert to English text.");

        Assert(
            LanguageHeuristics.ShouldSwitch("ghbdtn", "привет", english, russian),
            "Autoswitch recognizes a mistyped Russian greeting.");
        Assert(
            LanguageHeuristics.ShouldSwitch("руддщ", "hello", russian, english),
            "Autoswitch recognizes a mistyped English greeting.");
        Assert(
            !LanguageHeuristics.ShouldSwitch("hello", "руддщ", english, russian),
            "Autoswitch keeps a valid English word.");
    }

    private static void CaptureUi(
        IReadOnlyList<KeyboardLayoutDescriptor> layouts,
        string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var hotkeySettings = HotkeySettings.Defaults;
        foreach (var layout in layouts)
        {
            hotkeySettings.TargetLayouts[layout.Id] = new TargetLayoutHotkeys();
        }

        using var hotkeysForm = new HotkeysForm(hotkeySettings, layouts);
        SaveFormImage(hotkeysForm, Path.Combine(outputDirectory, "hotkeys.png"));

        var settings = new AppSettings();
        SettingsStore.Normalize(settings, layouts);
        using var targetsForm = new SwitchTargetsForm(layouts, settings.SwitchTargets);
        SaveFormImage(targetsForm, Path.Combine(outputDirectory, "switch-targets.png"));

        using var trayMenu = new ContextMenuStrip { ShowCheckMargin = true };
        trayMenu.Items.AddRange(new ToolStripItem[]
        {
            new ToolStripMenuItem("Autoswitch") { Checked = true },
            new ToolStripMenuItem("Hotkeys..."),
            new ToolStripMenuItem("Switch to..."),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Exit")
        });
        DarkTheme.Apply(trayMenu);
        trayMenu.Show(new Point(80, 80));
        Application.DoEvents();
        using var menuBitmap = new Bitmap(trayMenu.Width, trayMenu.Height);
        trayMenu.DrawToBitmap(menuBitmap, new Rectangle(Point.Empty, trayMenu.Size));
        menuBitmap.Save(Path.Combine(outputDirectory, "tray-menu.png"));
        trayMenu.Hide();
    }

    private static void TestSelectedTextIntegration(
        IReadOnlyList<KeyboardLayoutDescriptor> layouts,
        string executablePath)
    {
        var english = layouts.FirstOrDefault(layout => layout.TwoLetterLanguage == "en");
        var russian = layouts.FirstOrDefault(layout => layout.TwoLetterLanguage == "ru");
        if (english is null || russian is null)
        {
            Console.WriteLine("SKIP: selected-text integration test (both layouts are not installed).");
            return;
        }

        var settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NN Switch",
            "settings.json");
        var settingsExisted = File.Exists(settingsPath);
        var originalSettings = settingsExisted ? File.ReadAllBytes(settingsPath) : null;
        var testSettings = new AppSettings
        {
            AutoSwitch = true,
            Hotkeys = HotkeySettings.Defaults
        };
        SettingsStore.Normalize(testSettings, layouts);
        var russianTarget = layouts.FirstOrDefault(layout => layout.TwoLetterLanguage == "ru");
        if (russianTarget is not null)
        {
            testSettings.Hotkeys.TargetLayouts[russianTarget.Id].SelectedText =
                HotkeyBinding.Create(HotkeyModifiers.None, Keys.F8);
        }
        new SettingsStore().Save(testSettings);

        Process? appProcess = null;
        var originalInputLanguage = InputLanguage.CurrentInputLanguage;
        try
        {
            appProcess = Process.Start(new ProcessStartInfo
            {
                FileName = Path.GetFullPath(executablePath),
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            }) ?? throw new InvalidOperationException("Could not start NN Switch.");

            PumpMessages(750);
            appProcess.Refresh();
            Assert(!appProcess.HasExited, "The tray process remains running after startup.");
            Assert(appProcess.MainWindowHandle == IntPtr.Zero, "The tray process has no main window.");
            TestScreenshotShortcutPassThrough();

            var englishInputLanguage = InputLanguage.InstalledInputLanguages
                .Cast<InputLanguage>()
                .First(language =>
                    KeyboardLayoutService.GetId(language.Handle)
                        .Equals(english.Id, StringComparison.OrdinalIgnoreCase));
            InputLanguage.CurrentInputLanguage = englishInputLanguage;

            using var form = new Form
            {
                Text = "NN Switch integration test",
                ClientSize = new Size(480, 160),
                StartPosition = FormStartPosition.CenterScreen,
                ShowInTaskbar = false,
                TopMost = true
            };
            using var textBox = new TextBox
            {
                Multiline = true,
                Dock = DockStyle.Fill,
                Text = "Ghbdtn? vbh!",
                Font = new Font("Segoe UI", 13F)
            };
            form.Controls.Add(textBox);
            form.Show();
            form.Activate();
            textBox.Focus();
            textBox.SelectAll();
            NativeMethods.SetForegroundWindow(form.Handle);
            InputLanguage.CurrentInputLanguage = englishInputLanguage;
            NativeMethods.PostMessage(
                form.Handle,
                NativeMethods.WmInputLanguageChangeRequest,
                IntPtr.Zero,
                english.Handle);
            PumpMessages(100);
            var detectedLayout = KeyboardLayoutService.GetCurrent(layouts);
            Console.WriteLine(
                $"Integration source layout: expected={english.Id}, actual={detectedLayout?.Id ?? "(none)"}; foreground=0x{NativeMethods.GetForegroundWindow():X}");

            const string clipboardSentinel = "INSwitch integration clipboard";
            Clipboard.SetText(clipboardSentinel);
            NativeMethods.SendChord(
                NativeMethods.VkControl,
                NativeMethods.VkMenu,
                (ushort)Keys.S);
            PumpMessages(1600);

            Console.WriteLine($"Integration text after hotkey: [{textBox.Text}]");
            Assert(textBox.Text == "Привет, мир!", "The registered selected-text hotkey replaces text in another process.");
            Assert(
                Clipboard.ContainsText() && Clipboard.GetText() == clipboardSentinel,
                "The selected-text command restores the previous clipboard.");

            textBox.Text = "Ghbdtn? vbh!";
            textBox.SelectionStart = textBox.TextLength;
            InputLanguage.CurrentInputLanguage = englishInputLanguage;
            NativeMethods.PostMessage(
                form.Handle,
                NativeMethods.WmInputLanguageChangeRequest,
                IntPtr.Zero,
                english.Handle);
            textBox.Focus();
            NativeMethods.SetForegroundWindow(form.Handle);
            PumpMessages(100);
            NativeMethods.SendChord(
                NativeMethods.VkControl,
                NativeMethods.VkMenu,
                (ushort)Keys.W);
            PumpMessages(1600);
            Assert(
                textBox.Text == "Ghbdtn? мир!",
                "The registered last-word hotkey replaces only the word before the caret.");

            textBox.Text = "Ghbdtn? vbh! ";
            textBox.SelectionStart = textBox.TextLength;
            InputLanguage.CurrentInputLanguage = englishInputLanguage;
            NativeMethods.PostMessage(
                form.Handle,
                NativeMethods.WmInputLanguageChangeRequest,
                IntPtr.Zero,
                english.Handle);
            textBox.Focus();
            NativeMethods.SetForegroundWindow(form.Handle);
            PumpMessages(100);
            NativeMethods.SendChord(
                NativeMethods.VkControl,
                NativeMethods.VkMenu,
                (ushort)Keys.W);
            PumpMessages(1600);
            Assert(
                textBox.Text == "Ghbdtn? мир! ",
                "Last-word switching preserves a trailing word separator used by Autoswitch.");

            textBox.Text = "Ghbdtn? vbh!";
            textBox.SelectionStart = 3;
            InputLanguage.CurrentInputLanguage = englishInputLanguage;
            NativeMethods.PostMessage(
                form.Handle,
                NativeMethods.WmInputLanguageChangeRequest,
                IntPtr.Zero,
                english.Handle);
            textBox.Focus();
            NativeMethods.SetForegroundWindow(form.Handle);
            PumpMessages(100);
            NativeMethods.SendChord(
                NativeMethods.VkControl,
                NativeMethods.VkMenu,
                (ushort)Keys.A);
            PumpMessages(1600);
            Assert(
                textBox.Text == "Привет, мир!",
                "The registered active-field hotkey replaces the entire text field.");

            if (russianTarget is not null)
            {
                textBox.Text = "Ghbdtn? vbh!";
                textBox.SelectAll();
                InputLanguage.CurrentInputLanguage = englishInputLanguage;
                NativeMethods.PostMessage(
                    form.Handle,
                    NativeMethods.WmInputLanguageChangeRequest,
                    IntPtr.Zero,
                    english.Handle);
                textBox.Focus();
                NativeMethods.SetForegroundWindow(form.Handle);
                PumpMessages(100);
                NativeMethods.SendChord((ushort)Keys.F8);
                PumpMessages(1600);
                Assert(
                    textBox.Text == "Привет, мир!",
                    "A language-specific hotkey converts selected text directly to its target layout.");
            }

            form.Close();
        }
        finally
        {
            try
            {
                InputLanguage.CurrentInputLanguage = originalInputLanguage;
                if (appProcess is { HasExited: false })
                {
                    appProcess.Kill();
                    appProcess.WaitForExit(5000);
                }

                appProcess?.Dispose();
            }
            finally
            {
                if (settingsExisted && originalSettings is not null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
                    File.WriteAllBytes(settingsPath, originalSettings);
                }
                else
                {
                    File.Delete(settingsPath);
                }
            }
        }
    }

    private static void TestHotkeyDelivery(IReadOnlyList<KeyboardLayoutDescriptor> layouts)
    {
        HotkeyCommand? receivedCommand = null;
        using var manager = new HotkeyManager(command => receivedCommand = command);
        var errors = manager.RegisterAll(HotkeySettings.Defaults, layouts);
        Assert(errors.Count == 0, "Default global hotkeys can be registered.");

        var sent = NativeMethods.SendChord(
            NativeMethods.VkControl,
            NativeMethods.VkMenu,
            (ushort)Keys.S);
        Assert(sent, "Windows accepts the synthetic shortcut input.");
        PumpMessages(150);
        Assert(
            receivedCommand?.Mode == TextSwitchMode.SelectedText &&
            receivedCommand.TargetLayoutId is null,
            "The Windows message loop receives the selected-text hotkey.");

        var pauseSettings = HotkeySettings.Defaults;
        pauseSettings.SelectedText = HotkeyBinding.Create(HotkeyModifiers.None, Keys.Pause);
        var pauseErrors = manager.RegisterAll(pauseSettings, layouts);
        Assert(
            pauseErrors.Count == 0,
            "Windows accepts Pause as a standalone registered global hotkey.");

        var targetLayout = layouts.FirstOrDefault();
        if (targetLayout is not null)
        {
            var targetSettings = HotkeySettings.Defaults;
            targetSettings.TargetLayouts[targetLayout.Id] = new TargetLayoutHotkeys
            {
                SelectedText = HotkeyBinding.Create(HotkeyModifiers.None, Keys.F8)
            };
            var targetErrors = manager.RegisterAll(targetSettings, layouts);
            Assert(
                targetErrors.Count == 0,
                "A language-specific hotkey can be registered.");

            receivedCommand = null;
            NativeMethods.SendChord((ushort)Keys.F8);
            PumpMessages(150);
            Assert(
                receivedCommand?.Mode == TextSwitchMode.SelectedText &&
                receivedCommand.TargetLayoutId == targetLayout.Id,
                "A language-specific hotkey carries its explicit target layout.");
        }
    }

    private static void TestScreenshotShortcutPassThrough()
    {
        NativeMethods.SendChord(
            NativeMethods.VkLwin,
            NativeMethods.VkShift,
            (ushort)Keys.S);
        PumpMessages(1200);

        var captureHosts = Process.GetProcessesByName("ScreenClippingHost")
            .Concat(Process.GetProcessesByName("SnippingTool"))
            .ToList();
        try
        {
            Assert(
                captureHosts.Count > 0,
                "Win+Shift+S reaches the Windows screen capture host while Autoswitch is active.");
        }
        finally
        {
            NativeMethods.SendChord(NativeMethods.VkEscape);
            PumpMessages(200);
            foreach (var process in captureHosts)
            {
                process.Dispose();
            }
        }
    }

    private static void PumpMessages(int milliseconds)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < milliseconds)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private static string? GetOptionValue(string[] args, string option)
    {
        for (var index = 0; index + 1 < args.Length; index++)
        {
            if (args[index].Equals(option, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static void SaveFormImage(Form form, string path)
    {
        form.Show();
        Application.DoEvents();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        bitmap.Save(path);
        form.Hide();
        Console.WriteLine($"Captured {path}");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"FAILED: {message}");
        }

        Console.WriteLine($"PASS: {message}");
    }
}
