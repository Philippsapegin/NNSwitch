using System.Text.Json.Serialization;

namespace INSwitch.Models;

[Flags]
internal enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,
    NoRepeat = 0x4000
}

internal sealed class HotkeyBinding
{
    public HotkeyModifiers Modifiers { get; set; }

    public Keys Key { get; set; }

    [JsonIgnore]
    public bool IsConfigured => Key != Keys.None;

    public HotkeyBinding Clone() => new()
    {
        Modifiers = Modifiers,
        Key = Key
    };

    public static HotkeyBinding Create(HotkeyModifiers modifiers, Keys key) => new()
    {
        Modifiers = modifiers,
        Key = key
    };
}

internal sealed class HotkeySettings
{
    public HotkeyBinding SelectedText { get; set; } =
        HotkeyBinding.Create(HotkeyModifiers.Control | HotkeyModifiers.Alt, Keys.S);

    public HotkeyBinding LastWord { get; set; } =
        HotkeyBinding.Create(HotkeyModifiers.Control | HotkeyModifiers.Alt, Keys.W);

    public HotkeyBinding ActiveField { get; set; } =
        HotkeyBinding.Create(HotkeyModifiers.Control | HotkeyModifiers.Alt, Keys.A);

    public HotkeyBinding UpperCase { get; set; } = new();

    public HotkeyBinding LowerCase { get; set; } = new();

    public HotkeyBinding SentenceCase { get; set; } = new();

    public HotkeyBinding CycleLayout { get; set; } = new();

    public Dictionary<string, TargetLayoutHotkeys> TargetLayouts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public HotkeySettings Clone() => new()
    {
        SelectedText = SelectedText.Clone(),
        LastWord = LastWord.Clone(),
        ActiveField = ActiveField.Clone(),
        UpperCase = UpperCase.Clone(),
        LowerCase = LowerCase.Clone(),
        SentenceCase = SentenceCase.Clone(),
        CycleLayout = CycleLayout.Clone(),
        TargetLayouts = TargetLayouts.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone(),
            StringComparer.OrdinalIgnoreCase)
    };

    public static HotkeySettings Defaults => new();
}

internal sealed class TargetLayoutHotkeys
{
    public HotkeyBinding ActivateLayout { get; set; } = new();

    public HotkeyBinding SelectedText { get; set; } = new();

    public HotkeyBinding LastWord { get; set; } = new();

    public HotkeyBinding ActiveField { get; set; } = new();

    public TargetLayoutHotkeys Clone() => new()
    {
        ActivateLayout = ActivateLayout.Clone(),
        SelectedText = SelectedText.Clone(),
        LastWord = LastWord.Clone(),
        ActiveField = ActiveField.Clone()
    };
}

internal sealed class AppSettings
{
    internal const int CurrentSchemaVersion = 4;

    public int SchemaVersion { get; set; }

    public HotkeySettings Hotkeys { get; set; } = HotkeySettings.Defaults;

    public Dictionary<string, string> SwitchTargets { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}
