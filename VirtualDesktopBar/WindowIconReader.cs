using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VirtualDesktopBar;

internal static class WindowIconReader
{
    internal sealed record Result(BitmapSource Image, string Fingerprint);

    internal static Result? Read(IntPtr hwnd)
    {
        if (!IsWindow(hwnd)) return null;
        // A web app can share its browser's window class but have its own shell icon.
        var image = ReadResource(ReadRelaunchIcon(hwnd));
        foreach (int kind in new[] { 0, 1, 2 }) // SMALL, BIG, SMALL2
        {
            if (image != null) break;
            if (SendMessageTimeout(hwnd, 0x7F, new IntPtr(kind), IntPtr.Zero, 0x2, 100, out var icon) != IntPtr.Zero)
                image = FromHandle(icon);
        }
        foreach (int index in new[] { -34, -14 })
        {
            if (image != null) break;
            var icon = IntPtr.Size == 8 ? GetClassLongPtr(hwnd, index) : new IntPtr(unchecked((long)GetClassLong(hwnd, index)));
            image = FromHandle(icon);
        }
        return image == null ? null : new Result(image, Fingerprint(image));
    }

    internal static string Fingerprint(BitmapSource image)
    {
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        converted.CopyPixels(pixels, image.PixelWidth * 4, 0);
        return $"{image.PixelWidth}x{image.PixelHeight}:{Convert.ToHexString(SHA256.HashData(pixels))}";
    }

    private static BitmapSource? FromHandle(IntPtr icon)
    {
        if (icon == IntPtr.Zero) return null;
        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch (ArgumentException) { return null; }
        catch (COMException) { return null; }
    }

    internal static (string Path, int Index)? ParseResource(string? resource)
    {
        if (string.IsNullOrWhiteSpace(resource)) return null;
        string path = resource.Trim();
        int index = 0;
        int comma = path.LastIndexOf(',');
        if (comma >= 0 && int.TryParse(path[(comma + 1)..].Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int parsed))
        {
            index = parsed;
            path = path[..comma];
        }
        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        return string.IsNullOrWhiteSpace(path) ? null : (path, index);
    }

    internal static BitmapSource? ReadResource(string? resource)
    {
        var parsed = ParseResource(resource);
        if (parsed == null) return null;
        IntPtr large = IntPtr.Zero, small = IntPtr.Zero;
        try
        {
            ExtractIconEx(parsed.Value.Path, parsed.Value.Index, out large, out small, 1);
            return FromHandle(small) ?? FromHandle(large);
        }
        finally
        {
            // ExtractIconEx returns owned handles; WM_GETICON and class handles are borrowed.
            if (small != IntPtr.Zero) DestroyIcon(small);
            if (large != IntPtr.Zero && large != small) DestroyIcon(large);
        }
    }

    private static string? ReadRelaunchIcon(IntPtr hwnd)
    {
        var iid = typeof(IPropertyStore).GUID;
        IPropertyStore? store = null;
        try
        {
            if (SHGetPropertyStoreForWindow(hwnd, ref iid, out store) < 0 || store == null) return null;
            var key = new PropertyKey { Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), Id = 3 };
            var value = new PropVariant();
            try
            {
                return store.GetValue(ref key, out value) >= 0 && value.Type == 31
                    ? Marshal.PtrToStringUni(value.Pointer) : null;
            }
            finally { PropVariantClear(ref value); }
        }
        catch (COMException) { return null; }
        finally { if (store != null) Marshal.ReleaseComObject(store); }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct PropertyKey { public Guid Format; public uint Id; }
    // The native union contains a counted pointer (16 bytes on x64), not just a pointer.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
    [DllImport("shell32.dll")]
    internal static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid iid, out IPropertyStore? store);
    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint count);
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    private static extern IntPtr GetClassLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "GetClassLongW")]
    private static extern uint GetClassLong(IntPtr hwnd, int index);
}
