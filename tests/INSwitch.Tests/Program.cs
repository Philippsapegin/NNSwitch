using System.Text;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
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
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            var layouts = KeyboardLayoutService.GetInstalled();
            Assert(layouts.Count > 0, "Windows reports at least one installed keyboard layout.");
            TestSettingsDefaults(layouts);
            TestSettingsCleanup(layouts);
            TestSettingsPersistence(layouts);
            TestUnifiedHotkeysForm(layouts);
            TestLastTokenSelection();
            TestHotkeyFormatting();
            TestTextCaseConversion();
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
        Assert(
            !settings.Hotkeys.UpperCase.IsConfigured &&
            !settings.Hotkeys.LowerCase.IsConfigured &&
            !settings.Hotkeys.SentenceCase.IsConfigured,
            "Selected-text case hotkeys default to empty.");
        Assert(
            !settings.Hotkeys.CycleLayout.IsConfigured,
            "The cyclic layout hotkey defaults to empty.");

        foreach (var source in layouts)
        {
            Assert(
                settings.SwitchTargets.ContainsKey(source.Id),
                $"A switch target entry exists for {source.DisplayName}.");
            Assert(
                settings.Hotkeys.TargetLayouts.ContainsKey(source.Id),
                $"Language-specific hotkeys exist for {source.DisplayName}.");
            Assert(
                !settings.Hotkeys.TargetLayouts[source.Id].ActivateLayout.IsConfigured &&
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

    private static void TestTextCaseConversion()
    {
        var culture = CultureInfo.GetCultureInfo("ru-RU");
        Assert(
            TextCaseConverter.Convert(
                "Привет, World!",
                TextCaseMode.UpperCase,
                culture) == "ПРИВЕТ, WORLD!",
            "UPPERCASE handles mixed Russian and English text.");
        Assert(
            TextCaseConverter.Convert(
                "Привет, WORLD!",
                TextCaseMode.LowerCase,
                culture) == "привет, world!",
            "lowercase handles mixed Russian and English text.");
        Assert(
            TextCaseConverter.Convert(
                "  пРИВЕТ, МИР! эТО ТЕСТ.\r\nнОВАЯ СТРОКА?",
                TextCaseMode.SentenceCase,
                culture) == "  Привет, мир! Это тест.\r\nНовая строка?",
            "Sentence case capitalizes every selected sentence and line.");
    }

    private static void TestLastTokenSelection()
    {
        AssertLastToken("prefix ghbdtn:", "ghbdtn:");
        AssertLastToken("prefix (ghbdtn:'n)", "(ghbdtn:'n)");
        AssertLastToken("prefix ltkf'n", "ltkf'n");
        AssertLastToken("prefix Ghj[jlbvtw", "Ghj[jlbvtw");
        AssertLastToken("prefix [ghbdtn]", "[ghbdtn]");
        AssertLastToken("prefix ghbdtn:  ", "ghbdtn:  ");
        AssertLastToken("first line\r\nsecond", "second");
        AssertLastTokenAround("prefix (ghbdtn", ")", "ghbdtn");
        AssertLastTokenAround("prefix \"ghbdtn", "\"", "ghbdtn");
        AssertLastTokenAround("prefix 'ghbdtn", "'", "ghbdtn");
        AssertLastTokenAround("prefix ([\"ghbdtn", "\"])", "ghbdtn");
        AssertLastTokenAround("prefix [ghj[jlbvtw", "]", "ghj[jlbvtw");
        Assert(
            LastTokenSelection.FromTextBeforeCaret(" \t\r\n") is null,
            "A whitespace-only prefix has no last token.");
        Assert(
            LastTokenSelection.FromTextAroundCaret("prefix (", ")") is null,
            "An empty delimiter pair has no last word.");
    }

    private static void AssertLastTokenAround(
        string textBeforeCaret,
        string textAfterCaret,
        string expected)
    {
        var selection = LastTokenSelection.FromTextAroundCaret(
            textBeforeCaret,
            textAfterCaret);
        Assert(
            selection?.Text == expected,
            $"Last-token selection preserves a paired wrapper: {expected}");
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

    private static void TestUnifiedHotkeysForm(
        IReadOnlyList<KeyboardLayoutDescriptor> layouts)
    {
        var settings = new AppSettings();
        SettingsNormalizer.Normalize(settings, layouts);
        settings.Hotkeys.CycleLayout = HotkeyBinding.Create(
            HotkeyModifiers.None,
            Keys.F4);
        var source = layouts[0];
        var newTargetId = layouts
            .FirstOrDefault(layout => !layout.Id.Equals(
                source.Id,
                StringComparison.OrdinalIgnoreCase))?.Id ?? string.Empty;

        var captureStates = new List<bool>();
        using var form = new HotkeysForm(
            settings.Hotkeys,
            settings.SwitchTargets,
            layouts,
            captureStates.Add);
        form.Show();
        Application.DoEvents();
        var targetGrid = form.Controls.Find(
                "TargetMappingGrid",
                searchAllChildren: true)
            .OfType<DataGridView>()
            .Single();
        var universalGrid = form.Controls.Find(
                "UniversalHotkeyGrid",
                searchAllChildren: true)
            .OfType<DataGridView>()
            .Single();
        var layoutGrid = form.Controls.Find(
                "LayoutHotkeyGrid",
                searchAllChildren: true)
            .OfType<DataGridView>()
            .Single();
        var layoutTitle = form.Controls.Find(
                "LayoutSectionTitle",
                searchAllChildren: true)
            .OfType<Label>()
            .Single();
        Assert(
            universalGrid.Columns.Count == 2 &&
            universalGrid.Columns[0].Name == "Action" &&
            universalGrid.Columns[1].Name == "Hotkey",
            "The universal Hotkeys table contains only Action and Hotkey columns.");
        Assert(
            universalGrid.Rows.Cast<DataGridViewRow>().Any(row =>
                Equals(row.Cells["Action"].Value, "Selected to UPPERCASE")),
            "Case actions are labelled as selected-text transformations.");
        Assert(
            Equals(layoutGrid.Rows[0].Cells["Action"].Value, "Cycle input language"),
            "The cyclic switch is the first layout-generated hotkey.");
        Assert(
            universalGrid.Top < layoutTitle.Top &&
            layoutTitle.Top < targetGrid.Top &&
            targetGrid.Top < layoutGrid.Top,
            "Correction targets sit under the installed-layout heading and before its hotkeys.");
        Assert(
            form.ClientSize.Width == 560 && form.MinimumSize.Width == 500,
            "The Hotkeys window is one third narrower and can be reduced further.");
        var sourceRow = targetGrid.Rows
            .Cast<DataGridViewRow>()
            .Single(row => row.Tag as string == source.Id);
        sourceRow.Cells["Target"].Value = newTargetId;

        var targetCell = sourceRow.Cells["Target"];
        targetGrid.CurrentCell = targetCell;
        targetGrid.BeginEdit(selectAll: true);
        RaiseCellMouseDown(universalGrid, columnIndex: 0, rowIndex: 0);
        Assert(
            targetGrid.CurrentCell is null &&
            Equals(targetCell.Value, newTargetId),
            "Clicking outside a correction-target dropdown dismisses it without losing its value.");

        RaiseCellClick(
            universalGrid,
            universalGrid.Columns["Hotkey"]?.Index ??
                throw new InvalidOperationException("The Hotkey column is missing."),
            rowIndex: 0);
        Assert(
            captureStates.LastOrDefault() &&
            string.IsNullOrEmpty(universalGrid.Rows[0].Cells["Hotkey"].Value as string),
            "Clicking a hotkey cell activates capture and clears the current shortcut.");

        var saveButton = form.Controls.Find(
                "SaveHotkeysButton",
                searchAllChildren: true)
            .OfType<Button>()
            .Single();
        saveButton.PerformClick();

        Assert(
            form.SwitchTargetsResult[source.Id] == newTargetId,
            "The unified Hotkeys window saves default correction targets.");
        Assert(
            !form.Result.SelectedText.IsConfigured &&
            form.Result.LastWord.Key == settings.Hotkeys.LastWord.Key &&
            form.Result.ActiveField.Key == settings.Hotkeys.ActiveField.Key &&
            form.Result.CycleLayout.Key == Keys.F4,
            "Save keeps the active cell value and all other universal hotkeys in one action.");
        Assert(
            captureStates.SequenceEqual(new[] { true, false }),
            "Saving an active hotkey cell ends capture cleanly.");
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

        var bracketInsideWord = KeyboardLayoutService.ConvertText(
            "Ghj[jlbvtw",
            english,
            russian);
        Assert(
            bracketInsideWord == "Проходимец",
            "A bracket key inside a mistyped word is converted as a letter.");
        Assert(
            KeyboardLayoutService.ResolveSourceForText("ГШ", english, layouts) == russian,
            "Russian text overrides a stale English foreground-window layout.");
        Assert(
            KeyboardLayoutService.ResolveSourceForText("UI", russian, layouts) == english,
            "English text overrides a stale Russian foreground-window layout.");
        Assert(
            KeyboardLayoutService.ConvertText(
                "ГШ",
                KeyboardLayoutService.ResolveSourceForText("ГШ", english, layouts),
                english) == "UI",
            "A Firefox-style stale layout still converts ГШ to UI.");
        Assert(
            KeyboardLayoutService.ResolveSourceForText("123", russian, layouts) == russian,
            "Non-letter text does not override the detected layout.");

    }

    private static void CaptureUi(
        IReadOnlyList<KeyboardLayoutDescriptor> layouts,
        string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        var settings = new AppSettings();
        SettingsNormalizer.Normalize(settings, layouts);
        using var hotkeysForm = new HotkeysForm(
            settings.Hotkeys,
            settings.SwitchTargets,
            layouts);
        SaveFormImage(hotkeysForm, Path.Combine(outputDirectory, "hotkeys.png"));
        hotkeysForm.Show();
        Application.DoEvents();
        var universalGrid = hotkeysForm.Controls.Find(
                "UniversalHotkeyGrid",
                searchAllChildren: true)
            .OfType<DataGridView>()
            .Single();
        RaiseCellClick(
            universalGrid,
            universalGrid.Columns["Hotkey"]?.Index ??
                throw new InvalidOperationException("The Hotkey column is missing."),
            rowIndex: 0);
        Application.DoEvents();
        SaveFormImage(
            hotkeysForm,
            Path.Combine(outputDirectory, "hotkeys-active-cell.png"));

        using var trayMenu = new ContextMenuStrip { ShowCheckMargin = false };
        trayMenu.Items.AddRange(new ToolStripItem[]
        {
            new ToolStripMenuItem("Hotkeys..."),
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
        testSettings.Hotkeys.UpperCase =
            HotkeyBinding.Create(HotkeyModifiers.None, Keys.F10);
        testSettings.Hotkeys.LowerCase =
            HotkeyBinding.Create(HotkeyModifiers.None, Keys.F11);
        testSettings.Hotkeys.SentenceCase =
            HotkeyBinding.Create(HotkeyModifiers.None, Keys.F12);
        testSettings.Hotkeys.CycleLayout =
            HotkeyBinding.Create(HotkeyModifiers.None, Keys.F4);
        testSettings.Hotkeys.TargetLayouts[english.Id].ActivateLayout =
            HotkeyBinding.Create(HotkeyModifiers.None, Keys.F5);
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
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.GetFullPath(executablePath),
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.Environment["NN_SWITCH_TEST_INSTANCE"] =
                Guid.NewGuid().ToString("N");
            appProcess = Process.Start(startInfo) ??
                throw new InvalidOperationException("Could not start NN Switch.");

            PumpMessages(1500);
            appProcess.Refresh();
            Assert(!appProcess.HasExited, "The tray process remains running after startup.");
            Assert(appProcess.MainWindowHandle == IntPtr.Zero, "The tray process has no main window.");
            var englishInputLanguage = InputLanguage.InstalledInputLanguages
                .Cast<InputLanguage>()
                .First(language =>
                    KeyboardLayoutService.GetId(language.Handle)
                        .Equals(english.Id, StringComparison.OrdinalIgnoreCase));
            var russianInputLanguage = InputLanguage.InstalledInputLanguages
                .Cast<InputLanguage>()
                .First(language =>
                    KeyboardLayoutService.GetId(language.Handle)
                        .Equals(russian.Id, StringComparison.OrdinalIgnoreCase));
            InputLanguage.CurrentInputLanguage = englishInputLanguage;

            using var form = new Form
            {
                Text = "NN Switch integration test",
                ClientSize = new Size(480, 160),
                StartPosition = FormStartPosition.CenterScreen,
                ShowInTaskbar = false,
                TopMost = true
            };
            using var textBox = new CopyLineTextBox
            {
                Multiline = true,
                Dock = DockStyle.Fill,
                Text = "Ghbdtn? vbh!",
                Font = new Font("Segoe UI", 13F)
            };
            using var browserLikeField = new BrowserLikeTextControl
            {
                Dock = DockStyle.Fill,
                Visible = false
            };
            form.Controls.Add(browserLikeField);
            form.Controls.Add(textBox);
            form.Show();
            form.Activate();
            textBox.Focus();
            textBox.SelectAll();
            SetForegroundWindow(form.Handle);

            const string clipboardSentinel = "INSwitch integration clipboard";
            Clipboard.SetText(clipboardSentinel);
            EnsureWindowLayout(form, textBox, russianInputLanguage, russian, layouts);
            const string layoutOnlyText = "Layout switch must not edit this text";
            textBox.Text = layoutOnlyText;
            textBox.SelectionStart = textBox.TextLength;
            NativeMethods.SendUnmarkedChord((ushort)Keys.F5);
            var directLayoutChanged = PumpMessagesUntil(
                2000,
                () => KeyboardLayoutService.GetForWindow(form.Handle, layouts)?.Id
                    .Equals(english.Id, StringComparison.OrdinalIgnoreCase) == true);
            PumpMessages(100);
            Assert(
                directLayoutChanged,
                "A layout hotkey switches the active window directly to its target layout.");
            Assert(
                textBox.Text == layoutOnlyText &&
                textBox.SelectionStart == textBox.TextLength &&
                textBox.SelectionLength == 0,
                "A layout hotkey changes neither text nor the caret.");
            Assert(
                ClipboardTextEquals(clipboardSentinel),
                "A layout hotkey never touches the system clipboard.");

            var englishIndex = layouts.ToList().FindIndex(layout =>
                layout.Id.Equals(english.Id, StringComparison.OrdinalIgnoreCase));
            var expectedCycledLayout = layouts[(englishIndex + 1) % layouts.Count];
            NativeMethods.SendUnmarkedChord((ushort)Keys.F4);
            var cyclicLayoutChanged = PumpMessagesUntil(
                2000,
                () => KeyboardLayoutService.GetForWindow(form.Handle, layouts)?.Id
                    .Equals(expectedCycledLayout.Id, StringComparison.OrdinalIgnoreCase) == true);
            PumpMessages(100);
            Assert(
                cyclicLayoutChanged,
                "The cyclic hotkey advances to the next installed keyboard layout.");
            Assert(
                textBox.Text == layoutOnlyText &&
                textBox.SelectionStart == textBox.TextLength &&
                textBox.SelectionLength == 0 &&
                ClipboardTextEquals(clipboardSentinel),
                "The cyclic hotkey changes neither text, caret, nor clipboard.");

            textBox.Text = "Ghbdtn? vbh!";
            InputLanguage.CurrentInputLanguage = englishInputLanguage;
            NativeMethods.PostMessage(
                form.Handle,
                NativeMethods.WmInputLanguageChangeRequest,
                IntPtr.Zero,
                english.Handle);
            EnsureWindowLayout(form, textBox, englishInputLanguage, english, layouts);
            textBox.SelectAll();
            var detectedLayout = KeyboardLayoutService.GetCurrent(layouts);
            Console.WriteLine(
                $"Integration source layout: expected={english.Id}, actual={detectedLayout?.Id ?? "(none)"}; foreground=0x{NativeMethods.GetForegroundWindow():X}");

            Clipboard.SetText(clipboardSentinel);
            NativeMethods.SendUnmarkedChord((ushort)Keys.F6);
            PumpMessages(1600);

            Console.WriteLine($"Integration text after hotkey: [{textBox.Text}]");
            Assert(textBox.Text == "Привет, мир!", "The selected-text command replaces text in the active control.");
            Assert(
                Clipboard.ContainsText() && Clipboard.GetText() == clipboardSentinel,
                "The selected-text command restores the previous clipboard.");

            textBox.Text = "Mixed регистр";
            textBox.SelectAll();
            textBox.CopyCommandCount = 0;
            Clipboard.SetText(clipboardSentinel);
            NativeMethods.SendUnmarkedChord((ushort)Keys.F10);
            PumpMessagesUntil(2000, () => textBox.Text == "MIXED РЕГИСТР");
            PumpMessages(100);
            Assert(
                textBox.Text == "MIXED РЕГИСТР" && textBox.CopyCommandCount == 0,
                "UPPERCASE replaces a native selection without a copy command.");
            Assert(
                ClipboardTextEquals(clipboardSentinel),
                "Native UPPERCASE never touches the system clipboard.");

            textBox.Text = "Mixed РЕГИСТР";
            textBox.SelectAll();
            NativeMethods.SendUnmarkedChord((ushort)Keys.F11);
            PumpMessagesUntil(2000, () => textBox.Text == "mixed регистр");
            PumpMessages(100);
            Assert(
                textBox.Text == "mixed регистр",
                "lowercase replaces a native selection.");

            textBox.Text = "hELLO, WORLD! tHIS IS A TEST.\r\nnEW LINE?";
            textBox.SelectAll();
            NativeMethods.SendUnmarkedChord((ushort)Keys.F12);
            PumpMessagesUntil(
                2000,
                () => textBox.Text == "Hello, world! This is a test.\r\nNew line?");
            PumpMessages(100);
            if (textBox.Text != "Hello, world! This is a test.\r\nNew line?")
            {
                Console.WriteLine(
                    $"Sentence-case failure: actual=[{textBox.Text.Replace("\r", "\\r").Replace("\n", "\\n")}], " +
                    $"selection={textBox.SelectionStart}+{textBox.SelectionLength}, " +
                    $"copyCommands={textBox.CopyCommandCount}");
            }
            Assert(
                textBox.Text == "Hello, world! This is a test.\r\nNew line?",
                "Sentence case handles multiple sentences and lines in a native selection.");

            textBox.Text = "ghbdtn";
            textBox.SelectionStart = textBox.TextLength;
            InputLanguage.CurrentInputLanguage = englishInputLanguage;
            NativeMethods.PostMessage(
                form.Handle,
                NativeMethods.WmInputLanguageChangeRequest,
                IntPtr.Zero,
                english.Handle);
            textBox.Focus();
            SetForegroundWindow(form.Handle);
            EnsureWindowLayout(form, textBox, englishInputLanguage, english, layouts);
            NativeMethods.SendUnmarkedChord(NativeMethods.VkEscape);
            PumpMessages(50);
            Clipboard.SetText(clipboardSentinel);
            textBox.CopyCommandCount = 0;
            textBox.AllowedCopyCommandCount = 0;
            NativeMethods.SendUnmarkedChord((ushort)Keys.F7);
            var nativeVisibleSelectionObserved = false;
            PumpMessages(
                800,
                () => nativeVisibleSelectionObserved |=
                    textBox.RedrawEnabled && textBox.SelectionLength > 0);
            textBox.AllowedCopyCommandCount = int.MaxValue;
            Assert(
                textBox.Text == "привет",
                "A native edit control replaces the last word without clipboard commands.");
            Assert(
                textBox.CopyCommandCount == 0 &&
                textBox.SelectionLength == 0 &&
                !nativeVisibleSelectionObserved,
                "The native edit path uses neither copying nor a visible selection.");
            Assert(
                Clipboard.ContainsText() && Clipboard.GetText() == clipboardSentinel,
                "The native edit path never touches the system clipboard.");

            textBox.Text = "ghbdtn";
            textBox.SelectionStart = textBox.TextLength;
            InputLanguage.CurrentInputLanguage = englishInputLanguage;
            NativeMethods.PostMessage(
                form.Handle,
                NativeMethods.WmInputLanguageChangeRequest,
                IntPtr.Zero,
                english.Handle);
            textBox.Focus();
            SetForegroundWindow(form.Handle);
            EnsureWindowLayout(form, textBox, englishInputLanguage, english, layouts);
            NativeMethods.SendUnmarkedChord(NativeMethods.VkEscape);
            PumpMessages(50);
            Clipboard.SetText(clipboardSentinel);
            textBox.CopyCommandCount = 0;
            textBox.RejectNativeReplacement = true;
            var failedNativeVisibleSelection = false;
            NativeMethods.SendUnmarkedChord((ushort)Keys.F7);
            PumpMessages(
                800,
                () => failedNativeVisibleSelection |=
                    textBox.RedrawEnabled && textBox.SelectionLength > 0);
            textBox.RejectNativeReplacement = false;
            Assert(
                textBox.Text == "ghbdtn" &&
                textBox.SelectionStart == textBox.TextLength &&
                textBox.SelectionLength == 0,
                "A rejected native replacement preserves text and restores the original caret.");
            Assert(
                textBox.RedrawEnabled && !failedNativeVisibleSelection,
                "A rejected native replacement cannot leave redraw disabled or expose a selection.");
            Assert(
                textBox.CopyCommandCount == 0 &&
                ClipboardTextEquals(clipboardSentinel),
                "A rejected native replacement never falls through to clipboard mutation.");

            textBox.Visible = false;
            browserLikeField.Visible = true;
            browserLikeField.BringToFront();

            const string browserCaseText = "Browser Mixed регистр";
            browserLikeField.SetTextAndSelection(
                browserCaseText,
                0,
                browserCaseText.Length);
            browserLikeField.Focus();
            SetForegroundWindow(form.Handle);
            Clipboard.SetText(clipboardSentinel);
            browserLikeField.CopyCommandCount = 0;
            NativeMethods.SendUnmarkedChord((ushort)Keys.F10);
            PumpMessagesUntil(
                3000,
                () => browserLikeField.Text == "BROWSER MIXED РЕГИСТР" &&
                      ClipboardTextEquals(clipboardSentinel));
            Assert(
                browserLikeField.Text == "BROWSER MIXED РЕГИСТР" &&
                browserLikeField.CopyCommandCount > 0,
                "UPPERCASE uses the verified fallback in a browser-like field.");
            Assert(
                ClipboardTextEquals(clipboardSentinel),
                "The browser-like case fallback restores the previous clipboard.");

            foreach (var allowedCopyCommands in new[] { 1, 2 })
            {
                browserLikeField.SetTextAndCaret("ghbdtn", 6);
                InputLanguage.CurrentInputLanguage = englishInputLanguage;
                NativeMethods.PostMessage(
                    form.Handle,
                    NativeMethods.WmInputLanguageChangeRequest,
                    IntPtr.Zero,
                    english.Handle);
                browserLikeField.Focus();
                SetForegroundWindow(form.Handle);
                EnsureWindowLayout(
                    form,
                    browserLikeField,
                    englishInputLanguage,
                    english,
                    layouts);
                NativeMethods.SendUnmarkedChord(NativeMethods.VkEscape);
                PumpMessages(50);
                Clipboard.SetText(clipboardSentinel);
                browserLikeField.CopyCommandCount = 0;
                browserLikeField.AllowedCopyCommandCount = allowedCopyCommands;
                NativeMethods.SendUnmarkedChord((ushort)Keys.F7);
                var expectedCopyCommands = allowedCopyCommands == 1 ? 2 : 4;
                PumpMessagesUntil(
                    3000,
                    () => browserLikeField.CopyCommandCount >= expectedCopyCommands &&
                          browserLikeField.CaretIndex == browserLikeField.Text.Length &&
                          browserLikeField.SelectionLength == 0 &&
                          ClipboardTextEquals(clipboardSentinel));
                browserLikeField.AllowedCopyCommandCount = int.MaxValue;
                Assert(
                    browserLikeField.Text == "ghbdtn",
                    $"A browser-like rejected copy in phase {allowedCopyCommands} never changes text.");
                Assert(
                    browserLikeField.CaretIndex == browserLikeField.Text.Length &&
                    browserLikeField.SelectionLength == 0,
                    $"A browser-like rejected copy in phase {allowedCopyCommands} restores the caret without selection.");
                Assert(
                    Clipboard.ContainsText() && Clipboard.GetText() == clipboardSentinel,
                    $"A browser-like rejected copy in phase {allowedCopyCommands} restores the clipboard.");
            }

            browserLikeField.SetTextAndCaret("ghbdtn", 6);
            InputLanguage.CurrentInputLanguage = englishInputLanguage;
            NativeMethods.PostMessage(
                form.Handle,
                NativeMethods.WmInputLanguageChangeRequest,
                IntPtr.Zero,
                english.Handle);
            browserLikeField.Focus();
            SetForegroundWindow(form.Handle);
            EnsureWindowLayout(
                form,
                browserLikeField,
                englishInputLanguage,
                english,
                layouts);
            NativeMethods.SendUnmarkedChord(NativeMethods.VkEscape);
            PumpMessages(50);
            Clipboard.SetText(clipboardSentinel);
            browserLikeField.CopyCommandCount = 0;
            browserLikeField.Trace.Clear();
            NativeMethods.SendUnmarkedChord((ushort)Keys.F7);
            PumpMessagesUntil(
                3000,
                () => browserLikeField.Text == "привет" &&
                      browserLikeField.SelectionLength == 0 &&
                      ClipboardTextEquals(clipboardSentinel));
            if (browserLikeField.Text != "привет")
            {
                Console.WriteLine(
                    $"Browser fallback failure: actual=[{browserLikeField.Text}], " +
                    $"caret={browserLikeField.CaretIndex}, " +
                    $"selection={browserLikeField.SelectionLength}, " +
                    $"copyCommands={browserLikeField.CopyCommandCount}, " +
                    $"trace=[{string.Join("; ", browserLikeField.Trace)}], " +
                    $"clipboard=[{(Clipboard.ContainsText() ? Clipboard.GetText() : "(non-text)")}]");
            }

            Assert(
                browserLikeField.Text == "привет",
                "A browser-like field still converts existing text through the verified fallback.");
            Assert(
                browserLikeField.CopyCommandCount > 0 &&
                browserLikeField.SelectionLength == 0,
                "The browser fallback completes without leaving a selection.");
            Assert(
                Clipboard.ContainsText() && Clipboard.GetText() == clipboardSentinel,
                "The browser fallback restores the previous clipboard.");

            browserLikeField.SetTextAndCaret("ghbdtn", 6);
            InputLanguage.CurrentInputLanguage = englishInputLanguage;
            NativeMethods.PostMessage(
                form.Handle,
                NativeMethods.WmInputLanguageChangeRequest,
                IntPtr.Zero,
                english.Handle);
            browserLikeField.Focus();
            SetForegroundWindow(form.Handle);
            EnsureWindowLayout(
                form,
                browserLikeField,
                englishInputLanguage,
                english,
                layouts);
            NativeMethods.SendUnmarkedChord(NativeMethods.VkEscape);
            PumpMessages(50);
            Clipboard.SetText(clipboardSentinel);
            browserLikeField.CopyCommandCount = 0;
            NativeMethods.SendUnmarkedChord((ushort)Keys.F7);
            NativeMethods.SendUnmarkedChord((ushort)Keys.F7);
            PumpMessagesUntil(
                5000,
                () => browserLikeField.CopyCommandCount >= 6 &&
                      browserLikeField.Text == "ghbdtn" &&
                      browserLikeField.SelectionLength == 0 &&
                      ClipboardTextEquals(clipboardSentinel));
            Assert(
                browserLikeField.CopyCommandCount >= 6 &&
                browserLikeField.Text == "ghbdtn",
                "A second hotkey pressed during an operation is queued instead of discarded.");
            Assert(
                browserLikeField.SelectionLength == 0 &&
                ClipboardTextEquals(clipboardSentinel),
                "Queued browser operations finish with no selection and the original clipboard.");

            browserLikeField.Visible = false;
            textBox.Visible = true;
            textBox.BringToFront();

            textBox.Text = "Ghbdtn? vbh!";
            textBox.CopyCommandCount = 0;
            textBox.SelectionStart = textBox.TextLength;
            InputLanguage.CurrentInputLanguage = englishInputLanguage;
            NativeMethods.PostMessage(
                form.Handle,
                NativeMethods.WmInputLanguageChangeRequest,
                IntPtr.Zero,
                english.Handle);
            textBox.Focus();
            SetForegroundWindow(form.Handle);
            EnsureWindowLayout(form, textBox, englishInputLanguage, english, layouts);
            NativeMethods.SendUnmarkedChord((ushort)Keys.F7);
            PumpMessages(1600);
            if (textBox.Text != "Ghbdtn? мир!")
            {
                Console.WriteLine(
                    $"Last-word failure: actual=[{textBox.Text}], " +
                    $"selection={textBox.SelectionStart}+{textBox.SelectionLength}, " +
                    $"copyCommands={textBox.CopyCommandCount}, " +
                    $"clipboard=[{(Clipboard.ContainsText() ? Clipboard.GetText() : "(non-text)")}]");
            }
            Assert(
                textBox.Text == "Ghbdtn? мир!",
                "The last-word command replaces only the word before the caret.");

            textBox.Text = "g";
            textBox.CopyCommandCount = 0;
            textBox.SelectionStart = textBox.TextLength;
            InputLanguage.CurrentInputLanguage = englishInputLanguage;
            NativeMethods.PostMessage(
                form.Handle,
                NativeMethods.WmInputLanguageChangeRequest,
                IntPtr.Zero,
                english.Handle);
            textBox.Focus();
            SetForegroundWindow(form.Handle);
            EnsureWindowLayout(form, textBox, englishInputLanguage, english, layouts);
            NativeMethods.SendUnmarkedChord((ushort)Keys.F7);
            PumpMessages(1600);
            if (textBox.Text != KeyboardLayoutService.ConvertText("g", english, russian))
            {
                Console.WriteLine(
                    $"One-character failure: actual=[{textBox.Text}], " +
                    $"selection={textBox.SelectionStart}+{textBox.SelectionLength}, " +
                    $"copyCommands={textBox.CopyCommandCount}");
            }

            Assert(
                textBox.Text == KeyboardLayoutService.ConvertText("g", english, russian),
                "Last-word switching replaces a one-character token.");

            textBox.Text = "ГШ";
            textBox.SelectionStart = textBox.TextLength;
            InputLanguage.CurrentInputLanguage = englishInputLanguage;
            NativeMethods.PostMessage(
                form.Handle,
                NativeMethods.WmInputLanguageChangeRequest,
                IntPtr.Zero,
                english.Handle);
            textBox.Focus();
            SetForegroundWindow(form.Handle);
            EnsureWindowLayout(form, textBox, englishInputLanguage, english, layouts);
            NativeMethods.SendUnmarkedChord((ushort)Keys.F7);
            PumpMessages(1600);
            Assert(
                textBox.Text == "UI",
                "Last-word switching converts ГШ to UI when the browser window reports a stale English layout.");

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
            EnsureWindowLayout(form, textBox, englishInputLanguage, english, layouts);
            NativeMethods.SendUnmarkedChord((ushort)Keys.F7);
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
            EnsureWindowLayout(form, textBox, englishInputLanguage, english, layouts);
            NativeMethods.SendUnmarkedChord((ushort)Keys.F7);
            PumpMessages(1600);
            var convertedPunctuatedToken =
                KeyboardLayoutService.ConvertText(punctuatedToken, english, russian);
            if (textBox.Text != $"Prefix {convertedPunctuatedToken}")
            {
                Console.WriteLine(
                    $"Punctuated-token failure: actual=[{textBox.Text}], " +
                    $"selection={textBox.SelectionStart}+{textBox.SelectionLength}, " +
                    $"clipboard=[{(Clipboard.ContainsText() ? Clipboard.GetText() : "(non-text)")}]");
            }
            Assert(
                textBox.Text == $"Prefix {convertedPunctuatedToken}",
                "Last-word switching treats apostrophes, colons, and brackets as part of the token.");
            textBox.Text = "(ghbdtn)";
            textBox.SelectionStart = textBox.TextLength - 1;
            InputLanguage.CurrentInputLanguage = englishInputLanguage;
            NativeMethods.PostMessage(
                form.Handle,
                NativeMethods.WmInputLanguageChangeRequest,
                IntPtr.Zero,
                english.Handle);
            textBox.Focus();
            SetForegroundWindow(form.Handle);
            EnsureWindowLayout(form, textBox, englishInputLanguage, english, layouts);
            NativeMethods.SendUnmarkedChord((ushort)Keys.F7);
            PumpMessages(1600);
            if (textBox.Text != "(привет)")
            {
                Console.WriteLine(
                    $"Bracket-pair failure: actual=[{textBox.Text}], " +
                    $"selection={textBox.SelectionStart}+{textBox.SelectionLength}, " +
                    $"clipboard=[{(Clipboard.ContainsText() ? Clipboard.GetText() : "(non-text)")}]");
            }
            Assert(
                textBox.Text == "(привет)",
                "Last-word switching preserves a pre-typed bracket pair around the word.");

            textBox.Text = "\"ghbdtn\"";
            textBox.SelectionStart = textBox.TextLength - 1;
            InputLanguage.CurrentInputLanguage = englishInputLanguage;
            NativeMethods.PostMessage(
                form.Handle,
                NativeMethods.WmInputLanguageChangeRequest,
                IntPtr.Zero,
                english.Handle);
            textBox.Focus();
            SetForegroundWindow(form.Handle);
            EnsureWindowLayout(form, textBox, englishInputLanguage, english, layouts);
            NativeMethods.SendUnmarkedChord((ushort)Keys.F7);
            PumpMessages(1600);
            if (textBox.Text != "\"привет\"")
            {
                Console.WriteLine(
                    $"Quote-pair failure: actual=[{textBox.Text}], " +
                    $"selection={textBox.SelectionStart}+{textBox.SelectionLength}");
            }
            Assert(
                textBox.Text == "\"привет\"",
                "Last-word switching preserves a pre-typed quote pair around the word.");

            textBox.Text = "Ghj[jlbvtw";
            textBox.SelectionStart = textBox.TextLength;
            InputLanguage.CurrentInputLanguage = englishInputLanguage;
            NativeMethods.PostMessage(
                form.Handle,
                NativeMethods.WmInputLanguageChangeRequest,
                IntPtr.Zero,
                english.Handle);
            textBox.Focus();
            SetForegroundWindow(form.Handle);
            EnsureWindowLayout(form, textBox, englishInputLanguage, english, layouts);
            NativeMethods.SendUnmarkedChord((ushort)Keys.F7);
            PumpMessages(1600);
            Assert(
                textBox.Text == "Проходимец",
                "Last-word switching converts a bracket key inside a mistyped word.");

            const string reportedCorruptionCase =
                "jq? xfcnbxyj yt gthtdtltyyjq cnhjrb/yjq b|yjq cnhjrb/Ghf";
            var reportedTokenStart = reportedCorruptionCase.LastIndexOf(' ') + 1;
            var reportedPrefix = reportedCorruptionCase[..reportedTokenStart];
            var reportedToken = reportedCorruptionCase[reportedTokenStart..];
            var convertedReportedToken = KeyboardLayoutService.ConvertText(
                reportedToken,
                english,
                russian);
            textBox.Text = reportedCorruptionCase;
            textBox.SelectionStart = textBox.TextLength;
            InputLanguage.CurrentInputLanguage = englishInputLanguage;
            NativeMethods.PostMessage(
                form.Handle,
                NativeMethods.WmInputLanguageChangeRequest,
                IntPtr.Zero,
                english.Handle);
            textBox.Focus();
            SetForegroundWindow(form.Handle);
            EnsureWindowLayout(form, textBox, englishInputLanguage, english, layouts);
            NativeMethods.SendUnmarkedChord((ushort)Keys.F7);
            PumpMessages(1600);
            if (textBox.Text != $"{reportedPrefix}{convertedReportedToken}")
            {
                Console.WriteLine(
                    $"Reported-case failure: actual=[{textBox.Text}], " +
                    $"selection={textBox.SelectionStart}+{textBox.SelectionLength}, " +
                    $"clipboard=[{(Clipboard.ContainsText() ? Clipboard.GetText() : "(non-text)")}]");
            }
            Assert(
                textBox.Text == $"{reportedPrefix}{convertedReportedToken}",
                "Copy-line behavior cannot replace text before the final token.");

            textBox.Text = "ghbdtn";
            textBox.SelectionStart = textBox.TextLength;
            InputLanguage.CurrentInputLanguage = englishInputLanguage;
            NativeMethods.PostMessage(
                form.Handle,
                NativeMethods.WmInputLanguageChangeRequest,
                IntPtr.Zero,
                english.Handle);
            textBox.Focus();
            SetForegroundWindow(form.Handle);
            Clipboard.Clear();
            EnsureWindowLayout(form, textBox, englishInputLanguage, english, layouts);
            NativeMethods.SendUnmarkedChord((ushort)Keys.F7);
            PumpMessages(1600);
            Assert(textBox.Text == "привет", "Last-word switching works with an empty clipboard.");
            Assert(
                !Clipboard.ContainsText(),
                "An originally empty clipboard is empty again immediately after switching.");

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
            EnsureWindowLayout(form, textBox, englishInputLanguage, english, layouts);
            var convertedLongToken =
                KeyboardLayoutService.ConvertText(longToken, english, russian);
            var longTokenStopwatch = Stopwatch.StartNew();
            NativeMethods.SendUnmarkedChord((ushort)Keys.F7);
            while (longTokenStopwatch.ElapsedMilliseconds < 5000 &&
                   textBox.Text != $"Prefix {convertedLongToken}")
            {
                Application.DoEvents();
                Thread.Sleep(1);
            }

            Console.WriteLine(
                $"Long-token switching latency: {longTokenStopwatch.ElapsedMilliseconds} ms");
            if (textBox.Text != $"Prefix {convertedLongToken}")
            {
                Console.WriteLine(
                    $"Long-token failure: actual=[{textBox.Text}], " +
                    $"selection={textBox.SelectionStart}+{textBox.SelectionLength}, " +
                    $"clipboard=[{(Clipboard.ContainsText() ? Clipboard.GetText() : "(non-text)")}]");
            }
            Assert(
                textBox.Text == $"Prefix {convertedLongToken}",
                "Last-word switching reads a token beyond the initial probe.");

            Assert(
                longTokenStopwatch.ElapsedMilliseconds < 1500,
                "Long-token switching completes without a multi-second delay.");

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
            EnsureWindowLayout(form, textBox, englishInputLanguage, english, layouts);
            NativeMethods.SendUnmarkedChord((ushort)Keys.F9);
            PumpMessages(1600);
            Assert(
                textBox.Text == "Привет, мир!",
                "The active-field command replaces the entire text field.");

            textBox.Text = "Ghbdtn\r\nvbh";
            textBox.SelectionStart = 2;
            InputLanguage.CurrentInputLanguage = englishInputLanguage;
            NativeMethods.PostMessage(
                form.Handle,
                NativeMethods.WmInputLanguageChangeRequest,
                IntPtr.Zero,
                english.Handle);
            textBox.Focus();
            SetForegroundWindow(form.Handle);
            EnsureWindowLayout(form, textBox, englishInputLanguage, english, layouts);
            NativeMethods.SendUnmarkedChord((ushort)Keys.F9);
            PumpMessages(1600);
            Assert(
                textBox.Text == "Привет\r\nмир",
                "Direct Unicode replacement preserves line breaks in a multiline field.");

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
            EnsureWindowLayout(form, textBox, englishInputLanguage, english, layouts);
                NativeMethods.SendUnmarkedChord((ushort)Keys.F8);
                PumpMessages(1600);
                Assert(
                    textBox.Text == "Привет, мир!",
                    "A language-specific hotkey converts selected text directly to its target layout.");
            }

            textBox.Text = string.Empty;
            textBox.SelectionStart = 0;
            InputLanguage.CurrentInputLanguage = englishInputLanguage;
            NativeMethods.PostMessage(
                form.Handle,
                NativeMethods.WmInputLanguageChangeRequest,
                IntPtr.Zero,
                english.Handle);
            textBox.Focus();
            SetForegroundWindow(form.Handle);
            EnsureWindowLayout(form, textBox, englishInputLanguage, english, layouts);
            NativeMethods.SendUnmarkedChord(NativeMethods.VkEscape);
            foreach (var key in new[]
                     {
                         Keys.G,
                         Keys.H,
                         Keys.B,
                         Keys.D,
                         Keys.T,
                         Keys.N
                     })
            {
                NativeMethods.SendUnmarkedChord((ushort)key);
            }

            PumpMessages(100);
            NativeMethods.SendUnmarkedChord(NativeMethods.VkLeft);
            NativeMethods.SendUnmarkedChord(NativeMethods.VkRight);
            PumpMessages(100);
            Clipboard.SetText(clipboardSentinel);
            textBox.CopyCommandCount = 0;
            NativeMethods.SendUnmarkedChord((ushort)Keys.F7);
            PumpMessages(1600);
            Assert(
                textBox.Text == KeyboardLayoutService.ConvertText("ghbdtn", english, russian),
                "Navigation invalidates private history and the verified fallback still converts the word.");
            Assert(
                textBox.CopyCommandCount == 0,
                "Invalidated private history uses the native edit path instead of stale deletion counts.");

            textBox.Text = string.Empty;
            textBox.SelectionStart = 0;
            InputLanguage.CurrentInputLanguage = englishInputLanguage;
            NativeMethods.PostMessage(
                form.Handle,
                NativeMethods.WmInputLanguageChangeRequest,
                IntPtr.Zero,
                english.Handle);
            textBox.Focus();
            SetForegroundWindow(form.Handle);
            EnsureWindowLayout(form, textBox, englishInputLanguage, english, layouts);
            NativeMethods.SendUnmarkedChord(NativeMethods.VkEscape);
            PumpMessages(50);
            Clipboard.SetText(clipboardSentinel);
            textBox.CopyCommandCount = 0;
            foreach (var key in new[]
                     {
                         Keys.G,
                         Keys.H,
                         Keys.B,
                         Keys.D,
                         Keys.T,
                         Keys.N
                     })
            {
                NativeMethods.SendUnmarkedChord((ushort)key);
            }

            PumpMessages(100);
            Assert(textBox.Text == "ghbdtn", "Physical keyboard input reaches the tracked field.");
            NativeMethods.SendUnmarkedChord((ushort)Keys.F7);
            PumpMessages(800);
            Assert(
                textBox.Text == KeyboardLayoutService.ConvertText("ghbdtn", english, russian),
                "Tracked last-word switching replaces text without reading the field.");
            Assert(
                textBox.CopyCommandCount == 0,
                "Tracked last-word switching never sends Ctrl+C.");
            Assert(
                textBox.SelectionLength == 0,
                "Tracked last-word switching never creates a visible selection.");
            Assert(
                Clipboard.ContainsText() && Clipboard.GetText() == clipboardSentinel,
                "Tracked last-word switching never touches the system clipboard.");

            NativeMethods.SendUnmarkedChord((ushort)Keys.F7);
            PumpMessages(800);
            Assert(
                textBox.Text == "ghbdtn",
                "Repeated tracked switching reverses the word without reading the field.");
            Assert(
                textBox.CopyCommandCount == 0 && textBox.SelectionLength == 0,
                "Repeated tracked switching still uses neither copying nor selection.");

            textBox.Text = string.Empty;
            textBox.SelectionStart = 0;
            InputLanguage.CurrentInputLanguage = englishInputLanguage;
            NativeMethods.PostMessage(
                form.Handle,
                NativeMethods.WmInputLanguageChangeRequest,
                IntPtr.Zero,
                english.Handle);
            textBox.Focus();
            SetForegroundWindow(form.Handle);
            EnsureWindowLayout(form, textBox, englishInputLanguage, english, layouts);
            NativeMethods.SendUnmarkedChord(NativeMethods.VkEscape);
            PumpMessages(50);
            NativeMethods.SendUnmarkedChord((ushort)Keys.OemOpenBrackets);
            PumpMessages(50);
            textBox.Text = "[]";
            textBox.SelectionStart = 1;
            foreach (var key in new[]
                     {
                         Keys.G,
                         Keys.H,
                         Keys.B,
                         Keys.D,
                         Keys.T,
                         Keys.N
                     })
            {
                NativeMethods.SendUnmarkedChord((ushort)key);
            }

            PumpMessages(100);
            Clipboard.SetText(clipboardSentinel);
            textBox.CopyCommandCount = 0;
            NativeMethods.SendUnmarkedChord((ushort)Keys.F7);
            PumpMessages(1600);
            if (textBox.Text != "[привет]")
            {
                Console.WriteLine(
                    $"Tracked delimiter-pair failure: actual=[{textBox.Text}], " +
                    $"selection={textBox.SelectionStart}+{textBox.SelectionLength}, " +
                    $"copyCommands={textBox.CopyCommandCount}, " +
                    $"clipboard=[{(Clipboard.ContainsText() ? Clipboard.GetText() : "(non-text)")}]");
            }

            Assert(
                textBox.Text == "[привет]",
                "An editor-created delimiter pair is preserved around a physically typed word.");
            Assert(
                textBox.CopyCommandCount == 0,
                "An ambiguous leading delimiter is verified directly from the native field.");

            textBox.Text = string.Empty;
            textBox.SelectionStart = 0;
            InputLanguage.CurrentInputLanguage = russianInputLanguage;
            NativeMethods.PostMessage(
                form.Handle,
                NativeMethods.WmInputLanguageChangeRequest,
                IntPtr.Zero,
                russian.Handle);
            textBox.Focus();
            SetForegroundWindow(form.Handle);
            EnsureWindowLayout(form, textBox, russianInputLanguage, russian, layouts);
            NativeMethods.SendUnmarkedChord(NativeMethods.VkEscape);
            PumpMessages(50);
            Clipboard.SetText(clipboardSentinel);
            textBox.CopyCommandCount = 0;
            NativeMethods.SendUnmarkedChord(NativeMethods.VkShift, (ushort)Keys.U);
            NativeMethods.SendUnmarkedChord(NativeMethods.VkShift, (ushort)Keys.I);
            PumpMessages(100);
            Assert(textBox.Text == "ГШ", "Russian physical keys produce the reported ГШ case.");
            NativeMethods.SendUnmarkedChord((ushort)Keys.F7);
            PumpMessages(800);
            Assert(
                textBox.Text == "UI",
                "Tracked Firefox-style switching converts ГШ to UI without clipboard probing.");
            Assert(
                textBox.CopyCommandCount == 0 && textBox.SelectionLength == 0,
                "The ГШ to UI tracked path uses neither copying nor selection.");
            Assert(
                Clipboard.ContainsText() && Clipboard.GetText() == clipboardSentinel,
                "The ГШ to UI tracked path preserves the system clipboard.");

            textBox.Text = "Must stay unchanged";
            textBox.SelectionStart = textBox.TextLength;
            Clipboard.SetText(clipboardSentinel);
            NativeMethods.SendUnmarkedChord((ushort)Keys.F10);
            PumpMessages(500);
            Assert(
                textBox.Text == "Must stay unchanged" &&
                textBox.SelectionLength == 0 &&
                ClipboardTextEquals(clipboardSentinel),
                "A case command without a selection changes neither text nor clipboard.");

            const string rejectedCaseText = "Rejected Mixed Case";
            textBox.Text = rejectedCaseText;
            textBox.SelectAll();
            textBox.CopyCommandCount = 0;
            textBox.RejectNativeReplacement = true;
            Clipboard.SetText(clipboardSentinel);
            NativeMethods.SendUnmarkedChord((ushort)Keys.F10);
            PumpMessages(500);
            textBox.RejectNativeReplacement = false;
            Assert(
                textBox.Text == rejectedCaseText &&
                textBox.SelectionStart == 0 &&
                textBox.SelectionLength == rejectedCaseText.Length,
                "A rejected native case replacement preserves text and the original selection.");
            Assert(
                textBox.RedrawEnabled &&
                textBox.CopyCommandCount == 0 &&
                ClipboardTextEquals(clipboardSentinel),
                "A rejected native case replacement restores redraw without touching the clipboard.");

            textBox.Visible = false;
            browserLikeField.Visible = true;
            browserLikeField.BringToFront();
            const string browserNoSelectionText = "BROWSER MIXED РЕГИСТР";
            browserLikeField.SetTextAndCaret(
                browserNoSelectionText,
                browserNoSelectionText.Length);
            browserLikeField.Focus();
            SetForegroundWindow(form.Handle);
            Clipboard.SetText(clipboardSentinel);
            browserLikeField.CopyCommandCount = 0;
            NativeMethods.SendUnmarkedChord((ushort)Keys.F11);
            PumpMessagesUntil(
                2000,
                () => browserLikeField.CopyCommandCount > 0 &&
                      ClipboardTextEquals(clipboardSentinel));
            PumpMessages(100);
            Assert(
                browserLikeField.Text == browserNoSelectionText &&
                browserLikeField.SelectionLength == 0,
                "A browser-like case command without a selection leaves the text unchanged.");
            Assert(
                ClipboardTextEquals(clipboardSentinel),
                "A failed browser-like case command restores the previous clipboard.");

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
        var receivedCount = 0;
        var typedInputTracker = new TypedInputTracker(() => layouts);
        using var manager = new HotkeyManager(
            command =>
            {
                receivedCommand = command;
                receivedCount++;
            },
            typedInputTracker);
        var errors = manager.RegisterAll(HotkeySettings.Defaults, layouts);
        Assert(errors.Count == 0, "Default global hotkeys can be registered.");

        var sent = NativeMethods.SendUnmarkedChord(
            NativeMethods.VkControl,
            NativeMethods.VkMenu,
            (ushort)Keys.S);
        Assert(sent, "Windows accepts the synthetic shortcut input.");
        PumpMessages(150);
        Assert(
            receivedCommand is TextSwitchHotkeyCommand
            {
                Mode: TextSwitchMode.SelectedText,
                TargetLayoutId: null
            } && receivedCount == 1,
            "The Windows message loop receives the selected-text hotkey exactly once.");

        var pauseSettings = HotkeySettings.Defaults;
        pauseSettings.SelectedText = HotkeyBinding.Create(HotkeyModifiers.None, Keys.Pause);
        var pauseErrors = manager.RegisterAll(pauseSettings, layouts);
        Assert(
            pauseErrors.Count == 0,
            "Windows accepts Pause as a standalone registered global hotkey.");

        var caseSettings = HotkeySettings.Defaults;
        caseSettings.UpperCase = HotkeyBinding.Create(
            HotkeyModifiers.None,
            Keys.F10);
        var caseErrors = manager.RegisterAll(caseSettings, layouts);
        Assert(caseErrors.Count == 0, "A selected-text case hotkey can be registered.");

        receivedCommand = null;
        receivedCount = 0;
        NativeMethods.SendUnmarkedChord((ushort)Keys.F10);
        PumpMessages(150);
        Assert(
            receivedCommand is TextCaseHotkeyCommand
            {
                Mode: TextCaseMode.UpperCase
            } && receivedCount == 1,
            "The Windows message loop receives the UPPERCASE command exactly once.");

        var cycleAppSettings = new AppSettings();
        SettingsNormalizer.Normalize(cycleAppSettings, layouts);
        cycleAppSettings.Hotkeys.CycleLayout = HotkeyBinding.Create(
            HotkeyModifiers.None,
            Keys.F4);
        var cycleErrors = manager.RegisterAll(cycleAppSettings.Hotkeys, layouts);
        Assert(cycleErrors.Count == 0, "A cyclic layout hotkey can be registered.");

        receivedCommand = null;
        receivedCount = 0;
        using (var hotkeysForm = new HotkeysForm(
                   cycleAppSettings.Hotkeys,
                   cycleAppSettings.SwitchTargets,
                   layouts,
                   manager.SetCommandHandlingSuspended))
        {
            hotkeysForm.Show();
            Application.DoEvents();
            NativeMethods.SendUnmarkedChord((ushort)Keys.F4);
            PumpMessages(150);
            hotkeysForm.Close();
        }
        Assert(
            receivedCommand is CycleKeyboardLayoutHotkeyCommand && receivedCount == 1,
            "Configured hotkeys remain active while the Hotkeys window is open.");

        manager.SetCommandHandlingSuspended(suspended: true);
        receivedCommand = null;
        receivedCount = 0;
        NativeMethods.SendUnmarkedChord((ushort)Keys.F4);
        PumpMessages(150);
        Assert(
            receivedCommand is null && receivedCount == 0,
            "Hotkey capture temporarily passes configured shortcuts through.");

        manager.SetCommandHandlingSuspended(suspended: false);
        NativeMethods.SendUnmarkedChord((ushort)Keys.F4);
        PumpMessages(150);
        Assert(
            receivedCommand is CycleKeyboardLayoutHotkeyCommand && receivedCount == 1,
            "Configured shortcuts resume without rebuilding the hotkey manager.");

        var targetLayout = layouts.FirstOrDefault();
        if (targetLayout is not null)
        {
            var targetSettings = HotkeySettings.Defaults;
            targetSettings.TargetLayouts[targetLayout.Id] = new TargetLayoutHotkeys
            {
                ActivateLayout = HotkeyBinding.Create(HotkeyModifiers.None, Keys.F5),
                SelectedText = HotkeyBinding.Create(HotkeyModifiers.None, Keys.F8)
            };
            var targetErrors = manager.RegisterAll(targetSettings, layouts);
            Assert(
                targetErrors.Count == 0,
                "A language-specific hotkey can be registered.");

            receivedCommand = null;
            receivedCount = 0;
            NativeMethods.SendUnmarkedChord((ushort)Keys.F5);
            PumpMessages(150);
            Assert(
                receivedCommand is KeyboardLayoutHotkeyCommand layoutCommand &&
                layoutCommand.TargetLayoutId == targetLayout.Id &&
                receivedCount == 1,
                "A direct layout hotkey carries its explicit target exactly once.");

            receivedCommand = null;
            receivedCount = 0;
            NativeMethods.SendUnmarkedChord((ushort)Keys.F8);
            PumpMessages(150);
            Assert(
                receivedCommand is TextSwitchHotkeyCommand
                {
                    Mode: TextSwitchMode.SelectedText
                } switchCommand &&
                switchCommand.TargetLayoutId == targetLayout.Id &&
                receivedCount == 1,
                "A language-specific hotkey carries its explicit target layout.");
        }

        const int blockerId = 9127;
        var blockerRegistered = NativeMethods.RegisterHotKey(
            IntPtr.Zero,
            blockerId,
            HotkeyModifiers.Alt | HotkeyModifiers.NoRepeat,
            (uint)Keys.X);
        try
        {
            var exclusiveSettings = HotkeySettings.Defaults;
            exclusiveSettings.SelectedText = HotkeyBinding.Create(
                HotkeyModifiers.Alt,
                Keys.X);
            var exclusiveErrors = manager.RegisterAll(exclusiveSettings, layouts);
            Assert(
                exclusiveErrors.Count == 0,
                "A conflicting global shortcut is captured by the exclusive hook.");

            receivedCommand = null;
            receivedCount = 0;
            NativeMethods.SendUnmarkedChord(NativeMethods.VkMenu, (ushort)Keys.X);
            PumpMessages(150);
            Assert(
                receivedCommand is TextSwitchHotkeyCommand
                {
                    Mode: TextSwitchMode.SelectedText
                } && receivedCount == 1,
                "The exclusive hook delivers Alt+X to NN Switch.");
        }
        finally
        {
            if (blockerRegistered)
            {
                NativeMethods.UnregisterHotKey(IntPtr.Zero, blockerId);
            }
        }
    }

    private static void RaiseCellClick(
        DataGridView grid,
        int columnIndex,
        int rowIndex)
    {
        var method = typeof(DataGridView).GetMethod(
            "OnCellClick",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(DataGridViewCellEventArgs) },
            modifiers: null) ??
            throw new InvalidOperationException("Could not find DataGridView.OnCellClick.");
        method.Invoke(grid, new object[]
        {
            new DataGridViewCellEventArgs(columnIndex, rowIndex)
        });
    }

    private static void RaiseCellMouseDown(
        DataGridView grid,
        int columnIndex,
        int rowIndex)
    {
        var method = typeof(DataGridView).GetMethod(
            "OnCellMouseDown",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(DataGridViewCellMouseEventArgs) },
            modifiers: null) ??
            throw new InvalidOperationException("Could not find DataGridView.OnCellMouseDown.");
        method.Invoke(grid, new object[]
        {
            new DataGridViewCellMouseEventArgs(
                columnIndex,
                rowIndex,
                localX: 1,
                localY: 1,
                new MouseEventArgs(MouseButtons.Left, 1, 1, 1, 0))
        });
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

    private static bool PumpMessagesUntil(
        int timeoutMilliseconds,
        Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
        {
            Application.DoEvents();
            if (condition())
            {
                return true;
            }

            Thread.Sleep(1);
        }

        Application.DoEvents();
        return condition();
    }

    private static bool ClipboardTextEquals(string expected)
    {
        try
        {
            return Clipboard.ContainsText() && Clipboard.GetText() == expected;
        }
        catch (ExternalException)
        {
            return false;
        }
    }

    private static void EnsureWindowLayout(
        Form form,
        Control focusedControl,
        InputLanguage inputLanguage,
        KeyboardLayoutDescriptor expectedLayout,
        IReadOnlyList<KeyboardLayoutDescriptor> layouts)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            InputLanguage.CurrentInputLanguage = inputLanguage;
            NativeMethods.PostMessage(
                form.Handle,
                NativeMethods.WmInputLanguageChangeRequest,
                IntPtr.Zero,
                expectedLayout.Handle);
            focusedControl.Focus();
            SetForegroundWindow(form.Handle);
            PumpMessages(25);

            var actual = KeyboardLayoutService.GetForWindow(form.Handle, layouts);
            if (actual?.Id.Equals(
                    expectedLayout.Id,
                    StringComparison.OrdinalIgnoreCase) == true)
            {
                PumpMessages(75);
                focusedControl.Focus();
                SetForegroundWindow(form.Handle);
                actual = KeyboardLayoutService.GetForWindow(form.Handle, layouts);
                if (actual?.Id.Equals(
                        expectedLayout.Id,
                        StringComparison.OrdinalIgnoreCase) == true)
                {
                    return;
                }
            }
        }

        throw new InvalidOperationException(
            $"Could not activate {expectedLayout.DisplayName} for the integration window.");
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

    private sealed class CopyLineTextBox : TextBox
    {
        internal int CopyCommandCount;
        internal int AllowedCopyCommandCount = int.MaxValue;
        internal bool RedrawEnabled = true;
        internal bool RejectNativeReplacement;

        protected override void OnTextChanged(EventArgs eventArgs)
        {
            base.OnTextChanged(eventArgs);
            SelectionLength = 0;
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.C))
            {
                CopyCommandCount++;
                if (CopyCommandCount > AllowedCopyCommandCount)
                {
                    return true;
                }

                if (SelectionLength == 0)
                {
                    Clipboard.SetText(Text);
                    return true;
                }
            }

            return base.ProcessCmdKey(ref message, keyData);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == NativeMethods.WmSetredraw)
            {
                RedrawEnabled = message.WParam != IntPtr.Zero;
            }

            if (RejectNativeReplacement &&
                message.Msg == NativeMethods.EmReplacesel)
            {
                return;
            }

            base.WndProc(ref message);
        }
    }

    private sealed class BrowserLikeTextControl : Control
    {
        private int _selectionAnchor;
        private int _selectionActive;

        internal BrowserLikeTextControl()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
        }

        internal int AllowedCopyCommandCount = int.MaxValue;
        internal int CopyCommandCount;
        internal readonly List<string> Trace = new();

        internal int CaretIndex => _selectionActive;

        internal int SelectionLength =>
            Math.Abs(_selectionActive - _selectionAnchor);

        internal void SetTextAndCaret(string text, int caretIndex)
        {
            Text = text;
            _selectionAnchor = Math.Clamp(caretIndex, 0, Text.Length);
            _selectionActive = _selectionAnchor;
        }

        internal void SetTextAndSelection(string text, int start, int length)
        {
            Text = text;
            _selectionAnchor = Math.Clamp(start, 0, Text.Length);
            _selectionActive = Math.Clamp(
                _selectionAnchor + length,
                _selectionAnchor,
                Text.Length);
        }

        protected override bool IsInputKey(Keys keyData) =>
            (keyData & Keys.KeyCode) is Keys.Left or Keys.Right ||
            base.IsInputKey(keyData);

        protected override void OnKeyPress(KeyPressEventArgs eventArgs)
        {
            base.OnKeyPress(eventArgs);
            if (char.IsControl(eventArgs.KeyChar))
            {
                return;
            }

            var selectionStart = Math.Min(_selectionAnchor, _selectionActive);
            Trace.Add(
                $"char:{eventArgs.KeyChar}:{_selectionAnchor}->{_selectionActive}");
            Text = Text.Remove(selectionStart, SelectionLength)
                .Insert(selectionStart, eventArgs.KeyChar.ToString());
            _selectionAnchor = selectionStart + 1;
            _selectionActive = _selectionAnchor;
            eventArgs.Handled = true;
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.C))
            {
                CopyCommandCount++;
                Trace.Add(
                    $"copy#{CopyCommandCount}:{_selectionAnchor}->{_selectionActive}");
                if (CopyCommandCount > AllowedCopyCommandCount)
                {
                    return true;
                }

                var selectionStart = Math.Min(_selectionAnchor, _selectionActive);
                if (SelectionLength > 0)
                {
                    Clipboard.SetText(Text.Substring(selectionStart, SelectionLength));
                }

                return true;
            }

            var keyCode = keyData & Keys.KeyCode;
            if (keyCode is not Keys.Left and not Keys.Right)
            {
                return base.ProcessCmdKey(ref message, keyData);
            }

            var direction = keyCode == Keys.Left ? -1 : 1;
            if ((keyData & Keys.Shift) != 0)
            {
                _selectionActive = Math.Clamp(
                    _selectionActive + direction,
                    0,
                    Text.Length);
                return true;
            }

            if (SelectionLength > 0)
            {
                _selectionActive = direction < 0
                    ? Math.Min(_selectionAnchor, _selectionActive)
                    : Math.Max(_selectionAnchor, _selectionActive);
            }
            else
            {
                _selectionActive = Math.Clamp(
                    _selectionActive + direction,
                    0,
                    Text.Length);
            }

            _selectionAnchor = _selectionActive;
            return true;
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

}
