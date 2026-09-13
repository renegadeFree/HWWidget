using System;
using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;
using SD = System.Drawing;

namespace HWWidget;

/// <summary>The embedded application icon (also used for the tray and the windows).</summary>
static class AppIcon
{
    static byte[]? _bytes;

    public static byte[] Bytes
    {
        get
        {
            if (_bytes != null) return _bytes;
            try
            {
                using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico");
                if (s == null) return _bytes = Array.Empty<byte>();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                return _bytes = ms.ToArray();
            }
            catch { return _bytes = Array.Empty<byte>(); }
        }
    }

    public static SD.Icon? Drawing()
    {
        var bytes = Bytes;
        if (bytes.Length == 0) return null;
        try { return new SD.Icon(new MemoryStream(bytes), 32, 32); } catch { return null; }
    }

    public static BitmapFrame? Wpf()
    {
        var bytes = Bytes;
        if (bytes.Length == 0) return null;
        try { return BitmapFrame.Create(new MemoryStream(bytes), BitmapCreateOptions.None, BitmapCacheOption.OnLoad); }
        catch { return null; }
    }
}
