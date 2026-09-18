using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FileStash;

/// <summary>
/// 根据文件路径提取系统文件类型图标（16x16），用于列表缩略图。带内存缓存。
/// </summary>
public class FileIconConverter : IValueConverter
{
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_SMALLICON = 0x000000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static readonly Dictionary<string, ImageSource> Cache = new();

    /// <summary>缓存上限：超出后整体清空，避免长时间运行后无限制增长（内存泄漏）。</summary>
    private const int MaxCacheItems = 512;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path))
            return null;

        // ⚠️ File.Exists / SHGetFileInfo 会在 UI 线程上对所有列表项同步执行。
        //    若指向慢速/已断开的网络位置，两者都可能阻塞数秒——这是「卡死」的一个典型来源。
        //    因此全部用 try 包住，并限制缓存大小。
        try
        {
            if (!File.Exists(path)) return null;

            var key = path.ToLowerInvariant();
            if (Cache.TryGetValue(key, out var cached))
                return cached;

            var icon = ExtractIcon(path);
            if (icon != null)
            {
                if (Cache.Count >= MaxCacheItems) Cache.Clear();
                Cache[key] = icon;
            }
            return icon;
        }
        catch
        {
            // 取图标失败不应影响列表渲染，也不应让程序崩溃
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static ImageSource? ExtractIcon(string path)
    {
        try
        {
            var shfi = new SHFILEINFO();
            IntPtr result = SHGetFileInfo(path, 0, ref shfi, (uint)Marshal.SizeOf(shfi), SHGFI_ICON | SHGFI_SMALLICON);
            // 注意：SHGFI_ICON 模式下，函数返回值仅是成功标志；真正的图标句柄在 shfi.hIcon
            if (result == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
                return null;

            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(shfi.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally
            {
                DestroyIcon(shfi.hIcon);
            }
        }
        catch
        {
            return null;
        }
    }
}
