using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace FileStash;

/// <summary>
/// QuickLook 风格的预览窗：空格键弹出，显示图片原图或文件大图标 + 基本信息。
/// 按 Esc / 空格 / 点击关闭。
/// </summary>
public partial class PreviewWindow : Window
{
    /// <summary>是否为图片扩展名。</summary>
    private static readonly string[] ImageExts =
        { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".tif", ".tiff" };

    private static bool IsImage(string path)
    {
        var ext = Path.GetExtension(path);
        return !string.IsNullOrEmpty(ext) &&
               Array.Exists(ImageExts, e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
    }

    public PreviewWindow(StashItem item)
    {
        InitializeComponent();

        TitleText.Text = item.Name;
        PathText.Text = item.Path;

        if (!File.Exists(item.Path))
        {
            PreviewIconPanel.Visibility = Visibility.Visible;
            PreviewNameText.Text = item.Name;
            PreviewMetaText.Text = "⚠ 文件不存在";
            return;
        }

        bool shownImage = false;
        if (IsImage(item.Path))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(item.Path);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.EndInit();
                bmp.Freeze();

                PreviewImage.Source = bmp;
                PreviewImage.Visibility = Visibility.Visible;
                shownImage = true;

                var fi = new FileInfo(item.Path);
                PreviewMetaText.Text = $"{bmp.PixelWidth} × {bmp.PixelHeight} px · {FormatSize(fi.Length)}";
            }
            catch
            {
                shownImage = false;
            }
        }

        if (!shownImage)
        {
            // 非图片（或图片读取失败）：显示系统大图标 + 信息
            PreviewIconPanel.Visibility = Visibility.Visible;
            PreviewNameText.Text = item.Name;

            try
            {
                var fi = new FileInfo(item.Path);
                var typeName = string.IsNullOrEmpty(fi.Extension) ? "文件" : fi.Extension.TrimStart('.').ToUpperInvariant() + " 文件";
                PreviewMetaText.Text = $"{typeName} · {FormatSize(fi.Length)} · 修改于 {fi.LastWriteTime:yyyy-MM-dd HH:mm}";
            }
            catch
            {
                PreviewMetaText.Text = "";
            }

            try
            {
                var icon = new FileIconConverter().Convert(item.Path, typeof(object), null, System.Globalization.CultureInfo.InvariantCulture) as System.Windows.Media.ImageSource;
                if (icon != null) PreviewIcon.Source = icon;
            }
            catch { /* 图标失败不阻断 */ }
        }

        // 定位：屏幕工作区右侧（避开暂存架），垂直居中
        Loaded += (_, _) =>
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - Width - 300; // 留出暂存架区域
            if (Left < wa.Left) Left = wa.Left + 20;
            Top = wa.Top + (wa.Height - Height) / 2;
        };
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.0} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.0} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):0.00} GB";
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape || e.Key == Key.Space)
        {
            Close();
            e.Handled = true;
        }
    }

    private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // 点击任意处关闭
        Close();
    }
}
