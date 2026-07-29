using System.Runtime.InteropServices;
using INSwitch.Interop;

namespace INSwitch.Services;

internal static class CaretTextReader
{
    private const int InitialProbeSize = 64;
    private const int MaximumProbeSize = 1 << 20;
    private const int TextPatternId = 10014;
    private static readonly Guid CUIAutomationClassId =
        new("FF48DBA4-60EF-4201-AA87-54103EEF594E");

    private static IUIAutomation? _automation;

    internal static LastTokenSelection? TryGetLastTokenBeforeCaret(IntPtr foregroundWindow)
    {
        try
        {
            var caret = TryGetCaretRange(foregroundWindow);
            if (caret is null)
            {
                return null;
            }

            for (var probeSize = InitialProbeSize;
                 probeSize <= MaximumProbeSize;
                 probeSize = Math.Min(probeSize * 2, MaximumProbeSize))
            {
                var textBeforeCaret = caret.Clone();
                var moved = textBeforeCaret.MoveEndpointByUnit(
                    TextPatternRangeEndpoint.Start,
                    TextUnit.Character,
                    -probeSize);
                var text = textBeforeCaret.GetText(-1);
                var lastToken = LastTokenSelection.FromTextBeforeCaret(text);
                var reachedFieldStart = Math.Abs(moved) < probeSize;
                var foundWhitespaceBoundary =
                    lastToken is not null &&
                    lastToken.CaretMoveCount < LastTokenSelection.CountCaretMoves(text);

                if (lastToken is not null && (foundWhitespaceBoundary || reachedFieldStart))
                {
                    return lastToken;
                }

                if (reachedFieldStart || probeSize == MaximumProbeSize)
                {
                    return null;
                }
            }
        }
        catch (Exception)
        {
            // TextPattern is optional and some controls stop exposing it while focus changes.
        }

        return null;
    }

    private static IUIAutomationTextRange? TryGetCaretRange(IntPtr foregroundWindow)
    {
        if (foregroundWindow == IntPtr.Zero ||
            NativeMethods.GetForegroundWindow() != foregroundWindow)
        {
            return null;
        }

        _automation ??= (IUIAutomation)Activator.CreateInstance(
            Type.GetTypeFromCLSID(CUIAutomationClassId)!)!;
        var focusedWindow = NativeMethods.GetFocusedControl(foregroundWindow);
        if (focusedWindow != IntPtr.Zero &&
            TryGetCaretRange(_automation.ElementFromHandle(focusedWindow), out var caret))
        {
            return caret;
        }

        return TryGetCaretRange(_automation.GetFocusedElement(), out caret)
            ? caret
            : null;
    }

    private static bool TryGetCaretRange(
        IUIAutomationElement? element,
        out IUIAutomationTextRange? caret)
    {
        caret = null;
        try
        {
            if (element is null)
            {
                return false;
            }

            var textPattern = element.GetCurrentPattern(TextPatternId)
                as IUIAutomationTextPattern;
            var selection = textPattern?.GetSelection();
            if (selection is null || selection.Length == 0)
            {
                return false;
            }

            caret = selection.GetElement(0).Clone();
            caret.MoveEndpointByRange(
                TextPatternRangeEndpoint.Start,
                caret,
                TextPatternRangeEndpoint.End);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private enum TextPatternRangeEndpoint
    {
        Start,
        End
    }

    private enum TextUnit
    {
        Character,
        Format,
        Word,
        Line,
        Paragraph,
        Page,
        Document
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct AutomationPoint
    {
        internal readonly int X;
        internal readonly int Y;
    }

    [ComImport]
    [Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        [return: MarshalAs(UnmanagedType.Bool)]
        bool CompareElements(IUIAutomationElement first, IUIAutomationElement second);

        [return: MarshalAs(UnmanagedType.Bool)]
        bool CompareRuntimeIds(
            [MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)] int[] first,
            [MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)] int[] second);

        IUIAutomationElement GetRootElement();

        IUIAutomationElement ElementFromHandle(IntPtr windowHandle);

        IUIAutomationElement ElementFromPoint(AutomationPoint point);

        IUIAutomationElement GetFocusedElement();
    }

    [ComImport]
    [Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElement
    {
        void SetFocus();

        [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)]
        int[] GetRuntimeId();

        IntPtr FindFirst(int scope, IntPtr condition);

        IntPtr FindAll(int scope, IntPtr condition);

        IntPtr FindFirstBuildCache(int scope, IntPtr condition, IntPtr cacheRequest);

        IntPtr FindAllBuildCache(int scope, IntPtr condition, IntPtr cacheRequest);

        IntPtr BuildUpdatedCache(IntPtr cacheRequest);

        [return: MarshalAs(UnmanagedType.Struct)]
        object GetCurrentPropertyValue(int propertyId);

        [return: MarshalAs(UnmanagedType.Struct)]
        object GetCurrentPropertyValueEx(
            int propertyId,
            [MarshalAs(UnmanagedType.Bool)] bool ignoreDefaultValue);

        [return: MarshalAs(UnmanagedType.Struct)]
        object GetCachedPropertyValue(int propertyId);

        [return: MarshalAs(UnmanagedType.Struct)]
        object GetCachedPropertyValueEx(
            int propertyId,
            [MarshalAs(UnmanagedType.Bool)] bool ignoreDefaultValue);

        [return: MarshalAs(UnmanagedType.IUnknown)]
        object GetCurrentPatternAs(int patternId, ref Guid interfaceId);

        [return: MarshalAs(UnmanagedType.IUnknown)]
        object GetCachedPatternAs(int patternId, ref Guid interfaceId);

        [return: MarshalAs(UnmanagedType.IUnknown)]
        object? GetCurrentPattern(int patternId);
    }

    [ComImport]
    [Guid("32EBA289-3583-42C9-9C59-3B6D9A1E9B6A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextPattern
    {
        IUIAutomationTextRange RangeFromPoint(AutomationPoint point);

        IUIAutomationTextRange RangeFromChild(IUIAutomationElement child);

        IUIAutomationTextRangeArray GetSelection();

        IUIAutomationTextRangeArray GetVisibleRanges();

        IUIAutomationTextRange GetDocumentRange();

        int GetSupportedTextSelection();
    }

    [ComImport]
    [Guid("CE4AE76A-E717-4C98-81EA-47371D028EB6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextRangeArray
    {
        int Length { get; }

        IUIAutomationTextRange GetElement(int index);
    }

    [ComImport]
    [Guid("A543CC6A-F4AE-494B-8239-C814481187A8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTextRange
    {
        IUIAutomationTextRange Clone();

        [return: MarshalAs(UnmanagedType.Bool)]
        bool Compare(IUIAutomationTextRange range);

        int CompareEndpoints(
            TextPatternRangeEndpoint sourceEndpoint,
            IUIAutomationTextRange range,
            TextPatternRangeEndpoint targetEndpoint);

        void ExpandToEnclosingUnit(TextUnit textUnit);

        IUIAutomationTextRange FindAttribute(
            int attributeId,
            [MarshalAs(UnmanagedType.Struct)] object value,
            [MarshalAs(UnmanagedType.Bool)] bool backward);

        IUIAutomationTextRange FindText(
            [MarshalAs(UnmanagedType.BStr)] string text,
            [MarshalAs(UnmanagedType.Bool)] bool backward,
            [MarshalAs(UnmanagedType.Bool)] bool ignoreCase);

        [return: MarshalAs(UnmanagedType.Struct)]
        object GetAttributeValue(int attributeId);

        [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_R8)]
        double[] GetBoundingRectangles();

        IUIAutomationElement GetEnclosingElement();

        [return: MarshalAs(UnmanagedType.BStr)]
        string GetText(int maximumLength);

        int Move(TextUnit unit, int count);

        int MoveEndpointByUnit(
            TextPatternRangeEndpoint endpoint,
            TextUnit unit,
            int count);

        void MoveEndpointByRange(
            TextPatternRangeEndpoint sourceEndpoint,
            IUIAutomationTextRange range,
            TextPatternRangeEndpoint targetEndpoint);
    }
}
