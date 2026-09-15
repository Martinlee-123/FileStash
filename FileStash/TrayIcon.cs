using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FileStash;

/// <summary>
/// 系统托盘图标管理：常驻托盘、右键菜单（显示/隐藏浮窗、清空、找回、退出）。
/// 程序唯一优雅退出入口也在这里（主窗口无边框、无关闭按钮）。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private bool _disposed;

    public TrayIcon(Action togglePanel, Action clearAll, Action restore, Action toggleEdge, Action exit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("显示/隐藏浮窗", null, (_, _) => togglePanel());
        menu.Items.Add("切换停靠边（左/右）", null, (_, _) => toggleEdge());
        menu.Items.Add("清空全部", null, (_, _) => clearAll());
        menu.Items.Add("找回", null, (_, _) => restore());
        menu.Items.Add(new ToolStripSeparator());
        var hotkeyHint = new ToolStripMenuItem("快捷键：Ctrl+Alt+S") { Enabled = false };
        menu.Items.Add(hotkeyHint);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => exit());

        _notifyIcon = new NotifyIcon
        {
            Icon = CreateTrayIcon(),
            Text = "文件暂存栈（Ctrl+Alt+S 呼出）",
            ContextMenuStrip = menu,
            Visible = true
        };

        // 双击托盘图标 = 呼出/隐藏浮窗
        _notifyIcon.DoubleClick += (_, _) => togglePanel();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }

    /// <summary>运行时绘制一个 16x16 托盘图标：浅灰圆角底 + 三条深色横线（堆叠文件）。</summary>
    private static Icon CreateTrayIcon()
    {
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // 浅灰圆角背景
            using (var path = RoundedRect(new Rectangle(1, 1, 14, 14), 3))
            using (var bg = new SolidBrush(Color.FromArgb(230, 232, 232)))
            {
                g.FillPath(bg, path);
            }

            // 深色堆叠横线
            using var pen = new Pen(Color.FromArgb(58, 58, 66), 1.5f);
            g.DrawLine(pen, 4, 5, 12, 5);
            g.DrawLine(pen, 4, 8, 12, 8);
            g.DrawLine(pen, 4, 11, 12, 11);
        }

        var hIcon = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(hIcon);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
