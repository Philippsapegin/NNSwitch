namespace INSwitch.UI;

internal static class TrayIconFactory
{
    internal static Icon Create()
    {
        try
        {
            var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon is not null)
            {
                return icon;
            }
        }
        catch
        {
            // Fall back to a standard icon only if the executable resource is unavailable.
        }

        return (Icon)SystemIcons.Application.Clone();
    }
}
