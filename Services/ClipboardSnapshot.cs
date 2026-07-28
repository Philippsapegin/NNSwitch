using System.Collections.Specialized;
using System.Runtime.InteropServices;

namespace INSwitch.Services;

internal sealed class ClipboardSnapshot : IDisposable
{
    private const int RestoreAttempts = 10;
    private const int RestoreRetryDelayMs = 15;

    private readonly DataObject? _data;
    private readonly List<IDisposable> _ownedData;
    private bool _disposed;

    private ClipboardSnapshot(DataObject? data, List<IDisposable> ownedData)
    {
        _data = data;
        _ownedData = ownedData;
    }

    internal static bool TryCapture(out ClipboardSnapshot snapshot)
    {
        var dataObject = new DataObject();
        var copiedFormats = 0;
        var ownedData = new List<IDisposable>();

        try
        {
            var current = Clipboard.GetDataObject();
            if (current is null)
            {
                snapshot = new ClipboardSnapshot(null, ownedData);
                return true;
            }

            foreach (var format in current.GetFormats(autoConvert: false))
            {
                try
                {
                    var data = CloneClipboardData(
                        current.GetData(format, autoConvert: false),
                        ownedData);
                    if (data is null)
                    {
                        continue;
                    }

                    dataObject.SetData(format, autoConvert: false, data);
                    copiedFormats++;
                }
                catch
                {
                    // Unsupported clipboard formats are skipped.
                }
            }
        }
        catch (ExternalException)
        {
            foreach (var item in ownedData)
            {
                item.Dispose();
            }

            snapshot = null!;
            return false;
        }

        snapshot = new ClipboardSnapshot(copiedFormats > 0 ? dataObject : null, ownedData);
        return true;
    }

    internal async Task RestoreAsync()
    {
        if (_data is null)
        {
            return;
        }

        for (var attempt = 0; attempt < RestoreAttempts; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(_data, copy: true);
                return;
            }
            catch (ExternalException) when (attempt + 1 < RestoreAttempts)
            {
                await Task.Delay(RestoreRetryDelayMs);
            }
            catch (ExternalException)
            {
                return;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var item in _ownedData)
        {
            item.Dispose();
        }
    }

    private static object? CloneClipboardData(
        object? data,
        ICollection<IDisposable> ownedData) =>
        data switch
        {
            null => null,
            Bitmap bitmap => CloneBitmap(bitmap, ownedData),
            MemoryStream stream => CloneStream(stream, ownedData),
            byte[] bytes => bytes.ToArray(),
            string[] strings => strings.ToArray(),
            StringCollection files => CloneFileList(files),
            _ => data
        };

    private static Bitmap CloneBitmap(Bitmap bitmap, ICollection<IDisposable> ownedData)
    {
        var clone = new Bitmap(bitmap);
        ownedData.Add(clone);
        return clone;
    }

    private static MemoryStream CloneStream(
        MemoryStream stream,
        ICollection<IDisposable> ownedData)
    {
        var clone = new MemoryStream(stream.ToArray());
        ownedData.Add(clone);
        return clone;
    }

    private static StringCollection CloneFileList(StringCollection files)
    {
        var clone = new StringCollection();
        clone.AddRange(files.Cast<string>().ToArray());
        return clone;
    }
}
