using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IdleDash.Core;

/// <summary>Icoontjes voor snelkoppelingen: van apps en bestanden (via Windows) en van websites (van de site zelf).</summary>
public static class ShellIcons
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) IdleDash");
        return client;
    }

    private static string IconFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IdleDash", "icons");

    /// <summary>Het grote icoon (48 pixels) van een app, snelkoppeling, bestand of map.</summary>
    public static ImageSource? ForFile(string path)
    {
        if (Cache.TryGetValue(path, out var cached)) return cached;
        ImageSource? result = null;
        try
        {
            var info = new SHFILEINFO();
            if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_SYSICONINDEX) != IntPtr.Zero)
            {
                var iid = typeof(IImageList).GUID;
                if (SHGetImageList(SHIL_EXTRALARGE, ref iid, out var list) == 0 && list.GetIcon(info.iIcon, ILD_TRANSPARENT, out var icon) == 0 && icon != IntPtr.Zero)
                {
                    try
                    {
                        var bitmap = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        bitmap.Freeze();
                        result = bitmap;
                    }
                    finally
                    {
                        DestroyIcon(icon);
                    }
                }
            }
        }
        catch
        {
            // geen icoon: de widget toont dan een standaardteken
        }
        Cache[path] = result;
        return result;
    }

    /// <summary>
    /// Het icoon van een website, rechtstreeks van die site (niet via Google of een andere dienst).
    /// Eén keer opgehaald en daarna bewaard op je pc.
    /// </summary>
    public static async Task<ImageSource?> ForWebsiteAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return null;
        string key = "web:" + uri.Host;
        if (Cache.TryGetValue(key, out var cached)) return cached;
        Cache[key] = null;   // niet twee keer tegelijk ophalen

        string file = Path.Combine(IconFolder, uri.Host + ".icon");
        byte[]? bytes = null;
        try
        {
            if (File.Exists(file)) bytes = await File.ReadAllBytesAsync(file);
        }
        catch
        {
            // opnieuw ophalen
        }
        if (bytes == null)
        {
            foreach (string candidate in new[] { "/apple-touch-icon.png", "/favicon.ico" })
            {
                try
                {
                    using var response = await Http.GetAsync($"{uri.Scheme}://{uri.Host}{candidate}");
                    if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType?.StartsWith("image") != true) continue;
                    bytes = await response.Content.ReadAsByteArrayAsync();
                    Directory.CreateDirectory(IconFolder);
                    await File.WriteAllBytesAsync(file, bytes);
                    break;
                }
                catch
                {
                    // volgende proberen
                }
            }
        }
        var image = bytes == null ? null : Decode(bytes);
        Cache[key] = image;
        return image;
    }

    private static ImageSource? Decode(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();   // bij .ico: het grootste formaat
            frame.Freeze();
            return frame;
        }
        catch
        {
            return null;
        }
    }

    // ─────────────────────────── Windows ───────────────────────────

    private const uint SHGFI_SYSICONINDEX = 0x4000;
    private const int SHIL_EXTRALARGE = 0x2;
    private const int ILD_TRANSPARENT = 0x1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        // De volgorde moet gelijk zijn aan Windows; alleen GetIcon wordt gebruikt.
        [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, out int pi);
        [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, out int pi);
        [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
        [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
        [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, out int pi);
        [PreserveSig] int Draw(IntPtr pimldp);
        [PreserveSig] int Remove(int i);
        [PreserveSig] int GetIcon(int i, int flags, out IntPtr picon);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll")]
    private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
