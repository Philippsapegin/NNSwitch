using System.Text;
using System.Diagnostics;
using System.Runtime.InteropServices;
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
            TestSettingsCleanup(layouts);
            TestSettingsPersistence(layouts);
            TestLastTokenSelection();
            TestHotkeyFormatting();
            TestRussianEnglishConversion(layouts);

            if (args.Contains("--integration", StringComparer.OrdinalIgnoreCase))
            {
                var executablePath = GetOptionValue(args, "--integration") ??
                    throw new InvalidOperationException("--integration requires the app executable path.");
                TestSelectedTextIntegration(layouts, executablePath);
                TestHotkeyDelivery(layouts);
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
        SettingsNormalizer.Normalize(settings, layouts);

        Assert(
            settings.SchemaVersion == AppSettings.CurrentSchemaVersion,
            "Settings use the current schema version.");

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

    private static void TestLastTokenSelection()
    {
        AssertLastToken("prefix ghbdtn:", "ghbdtn:");
        AssertLastToken("prefix (ghbdtn:'n)", "(ghbdtn:'n)");
        AssertLastToken("prefix ltkf'n", "ltkf'n");
        AssertLastToken("prefix ghbdtn:  ", "ghbdtn:  ");
        AssertLastToken("first line\r\nsecond", "second");
        Assert(
            LastTokenSelection.FromTextBeforeCaret(" \t\r\n") is null,
            "A whitespace-only prefix has no last token.");
    }

    private static void AssertLastToken(string textBeforeCaret, string expected)
    {
        var selection = LastTokenSelection.FromTextBeforeCaret(textBeforeCaret);
        Assert(
            selection?.Text == expected,
            $"Last-token selection keeps punctuation until whitespace: {expected}");
    }

    private static void TestSettingsPersistence(
        IReadOnlyList<KeyboardLayoutDescriptor> layouts)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "NN-Switch-tests",
            Guid.NewGuid().ToString("N"));
        var currentDirectory = Path.Combine(root, "current");
        var legacyDirectory = Path.Combine(root, "legacy");

        try
        {
            Directory.CreateDirectory(legacyDirectory);
            File.WriteAllText(
                Path.Combine(legacyDirectory, "settings.json"),
                """{"UnknownLegacyProperty":true}""");

            var store = new SettingsStore(
                currentDirectory,
                legacyDirectory,
                _ => { });
            var migrated = store.Load(layouts);
            var currentPath = Path.Combine(currentDirectory, "settings.json");
            var migratedJson = File.ReadAllText(currentPath);

            Assert(
                migrated.SchemaVersion == AppSettings.CurrentSchemaVersion,
                "Legacy settings are upgraded to the current schema.");
            Assert(
                !migratedJson.Contains("UnknownLegacyProperty", StringComparison.Ordinal),
                "Obsolete settings data is removed during migration.");
            Assert(
                File.Exists(Path.Combine(legacyDirectory, "settings.json")),
                "Migration keeps the legacy file as a rollback fallback.");

            File.WriteAllText(currentPath, "{broken-json");
            store.Load(layouts);

            Assert(
                Directory.GetFiles(currentDirectory, "settings.json.corrupt-*").Length == 1,
                "Invalid settings are backed up before defaults are written.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void TestSettingsCleanup(IReadOnlyList<KeyboardLayoutDescriptor> layouts)
    {
        const string staleId = "DEADDEAD";
        var settings = new AppSettings();
        SettingsNormalizer.Normalize(settings, layouts);
        settings.SwitchTargets[staleId] = layouts[0].Id;
        settings.Hotkeys.TargetLayouts[staleId] = new TargetLayoutHotkeys();
        settings.SwitchTargets[layouts[0].Id] = staleId;

        var changed = SettingsNormalizer.Normalize(settings, layouts);

        Assert(changed, "Normalization reports repaired settings.");
        Assert(
            !settings.SwitchTargets.ContainsKey(staleId),
            "A removed source layout is deleted from switch targets.");
        Assert(
            !settings.Hotkeys.TargetLayouts.ContainsKey(staleId),
            "A removed layout is deleted from direct hotkeys.");
        Assert(
            string.IsNullOrEmpty(settings.SwitchTargets[layouts[0].Id]) ||
            layouts.Any(layout => layout.Id.Equals(
                settings.SwitchTargets[layouts[0].Id],
                StringComparison.OrdinalIgnoreCase)),
            "A missing target layout is replaced with a valid target or no target.");
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
        SettingsNormalizer.Normalize(settings, layouts);
        using var targetsForm = new SwitchTargetsForm(layouts, settings.SwitchTargets);
        SaveFormImage(targetsForm, Path.Combine(outputDirectory, "switch-targets.png"));

        using var trayMenu = new ContextMenuStrip { ShowCheckMargin = false };
        trayMenu.Items.AddRange(new ToolStripItem[]
        {
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
            Hotkeys = HotkeySettings.Defaults
        };
        SettingsNormalizer.Normalize(testSettings, layouts);
        testSettings.Hotkeys.SelectedText =
            HotkeyBinding.Create(HotkeyModifiers.None, Keys.F6);
        testSettings.Hotkeys.LastWord =
            HotkeyBinding.Create(HotkeyModifiers.None, Keys.F7);
        testSettings.Hotkeys.ActiveField =
            HotkeyBinding.Create(HotkeyModifiers.None, Keys.F9);
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
            SetForegroundWindow(form.Handle);
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
            NativeMethods.SendChord((ushort)Keys.F6);
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
            SetForegroundWindow(form.Handle);
            PumpMessages(100);
            NativeMethods.SendChord((ushort)Keys.F7);
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
            SetForegroundWindow(form.Handle);
            PumpMessages(100);
            NativeMethods.SendChord((ushort)Keys.F7);
            PumpMessages(1600);
            Assert(
                textBox.Text == "Ghbdtn? мир! ",
                "Last-word switching preserves a trailing word separator.");

            const string punctuatedToken = "(ghbdtn:'n)";
            textBox.Text = $"Prefix {punctuatedToken}";
            textBox.SelectionStart = textBox.TextLength;
            InputLanguage.CurrentInputLanguage = englishInputLanguage;
            NativeMethods.PostMessage(
                form.Handle,
                NativeMethods.WmInputLanguageChangeRequest,
                IntPtr.Zero,
                english.Handle);
            textBox.Focus();
            SetForegroundWindow(form.Handle);
            PumpMessages(100);
            NativeMethods.SendChord((ushort)Keys.F7);
            var maximumPunctuatedSelectionLength = 0;
            PumpMessages(
                1600,
                () => maximumPunctuatedSelectionLength = Math.Max(
                    maximumPunctuatedSelectionLength,
                    textBox.SelectionLength));
            var convertedPunctuatedToken =
                KeyboardLayoutService.ConvertText(punctuatedToken, english, russian);
            Assert(
                textBox.Text == $"Prefix {convertedPunctuatedToken}",
                "Last-word switching treats apostrophes, colons, and brackets as part of the token.");
            Assert(
                maximumPunctuatedSelectionLength <= punctuatedToken.Length,
                "Last-word switching never selects text before the token boundary.");

            var longToken = $"({new string('g', 70)}:'n)";
            textBox.Text = $"Prefix {longToken}";
            textBox.SelectionStart = textBox.TextLength;
            InputLanguage.CurrentInputLanguage = englishInputLanguage;
            NativeMethods.PostMessage(
                form.Handle,
                NativeMethods.WmInputLanguageChangeRequest,
                IntPtr.Zero,
                english.Handle);
            textBox.Focus();
            SetForegroundWindow(form.Handle);
            PumpMessages(100);
            NativeMethods.SendChord((ushort)Keys.F7);
            var maximumLongSelectionLength = 0;
            PumpMessages(
                1600,
                () => maximumLongSelectionLength = Math.Max(
                    maximumLongSelectionLength,
                    textBox.SelectionLength));
            var convertedLongToken =
                KeyboardLayoutService.ConvertText(longToken, english, russian);
            Assert(
                textBox.Text == $"Prefix {convertedLongToken}",
                "Last-word switching reads a token beyond the initial probe.");
            Assert(
                maximumLongSelectionLength <= longToken.Length,
                "Long-token switching never selects preceding text.");

            textBox.Text = "Ghbdtn? vbh!";
            textBox.SelectionStart = 3;
            InputLanguage.CurrentInputLanguage = englishInputLanguage;
            NativeMethods.PostMessage(
                form.Handle,
                NativeMethods.WmInputLanguageChangeRequest,
                IntPtr.Zero,
                english.Handle);
            textBox.Focus();
            SetForegroundWindow(form.Handle);
            PumpMessages(100);
            NativeMethods.SendChord((ushort)Keys.F9);
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
                SetForegroundWindow(form.Handle);
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

    private static void PumpMessages(int milliseconds, Action? observe = null)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < milliseconds)
        {
            Application.DoEvents();
            observe?.Invoke();
            Thread.Sleep(observe is null ? 10 : 1);
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);
}
