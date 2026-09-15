using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace FileStash;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    // 收起时露出屏幕的触发条宽度
    private const double TriggerBarWidth = 10;
    // 浮窗完全展开时的宽度
    private const double ExpandedWidth = 320;
    // 拖动时鼠标距离屏幕右边缘多少像素内，触发面板弹出
    private const double EdgeThreshold = 30;

    /// <summary>持久化文件路径：%AppData%\FileStash\stash.json</summary>
    private static readonly string StashFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FileStash", "stash.json");

    /// <summary>回收快照路径：%AppData%\FileStash\stash.recycle.json（清空时保存，供找回）</summary>
    private static readonly string RecycleFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FileStash", "stash.recycle.json");

    /// <summary>临时文件目录：%AppData%\FileStash\tmp（拖入的文字/图片生成的临时文件）</summary>
    private static readonly string TempDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FileStash", "tmp");

    private bool _isExpanded;

    // 托盘与全局快捷键（M5）
    private TrayIcon? _trayIcon;
    private HotKeyManager? _hotKey;
    // 浮窗整体是否被隐藏（快捷键/托盘切换）
    private bool _panelHidden;

    // 拖拽拖出相关状态
    private System.Windows.Point _dragStartPoint;
    private bool _dragOutSuppressCollapse;
    // 按下时刻的选中快照与命中项（Multiple 模式点击会 toggle 选中，拖拽必须用按下前的状态）
    private List<StashItem> _dragSnapshot = new();
    private StashItem? _dragPivotItem;
    // 是否已进入拖拽（用于区分「点击切换选中」与「拖出」）
    private bool _dragStarted;

    // 右键菜单当前指向的暂存项
    private StashItem? _contextMenuItem;

    // 固定开关：开启后面板常驻展开，鼠标离开也不自动收回
    private bool _isPinned;

    // 纯文字模式：开启后拖出 txt 时直接以文字形式（默认 false，拖出为 txt 文件）
    private bool _textMode;

    // 待延迟删除的临时文件（拖出/删除后不立即删，给目标应用留时间读取，避免 QQ 等报「空文件」）
    private readonly List<string> _pendingDelete = new();
    private System.Windows.Threading.DispatcherTimer? _cleanupTimer;

    // 拖入完成时间戳：拖入后的短时间内不自动收回（等 Drop 处理完）
    private DateTime _lastDropTime = DateTime.MinValue;

    /// <summary>暂存的文件列表（绑定到 ListBox）</summary>
    public ObservableCollection<StashItem> StashItems { get; } = new();

    public MainWindow()
    {
        InitializeComponent();

        // 绑定数据上下文，供 ListBox 的 ItemsSource 使用
        DataContext = this;

        // 不抢焦点、不激活（置顶悬浮窗的关键）
        ShowActivated = false;

        // 首次加载后定位到右边缘（收起态）
        Loaded += (_, _) =>
        {
            PositionToRightEdge();
            SetCollapsed();
            LoadStash();
            CleanupOrphanTempFiles();
            InstallMouseHook();
            InitTrayAndHotKey();
        };

        // 关闭时保存并卸载钩子
        Closed += (_, _) =>
        {
            SaveStash();
            UninstallMouseHook();
            _trayIcon?.Dispose();
            _hotKey?.Dispose();
        };

        // 响应系统工作区变化（分辨率/任务栏位置改变）
        SystemEvents.DisplaySettingsChanged += (_, _) =>
        {
            PositionToRightEdge();
            if (!_isExpanded) SetCollapsed();
        };

        // 右键菜单关闭后，若鼠标已离开面板，则收回
        StashList.ContextMenu!.Closed += (_, _) =>
        {
            var pos = System.Windows.Input.Mouse.GetPosition(this);
            if (pos.X < 0 || pos.Y < 0 || pos.X > ActualWidth || pos.Y > ActualHeight)
            {
                Collapse();
            }
        };

        // 列表变化时：更新空提示 + 持久化；一旦有文件就强制展开（状态驱动，避免时序竞态）
        StashItems.CollectionChanged += (_, _) =>
        {
            UpdateEmptyHint();
            SaveStash();

            if (StashItems.Count > 0)
            {
                Dispatcher.BeginInvoke(() => Expand());
            }
        };
    }

    #region 浮窗收放

    private void PositionToRightEdge()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - TriggerBarWidth;
        Top = wa.Top + (wa.Height - Height) / 2; // 垂直居中
    }

    private void Expand()
    {
        if (_isExpanded) return;
        _isExpanded = true;

        var wa = SystemParameters.WorkArea;
        double targetLeft = wa.Right - ExpandedWidth;

        AnimateLeft(targetLeft);
        TriggerArrow.Text = "»";
    }

    private void Collapse()
    {
        if (!_isExpanded) return;
        // 固定开启时不收回
        if (_isPinned) return;
        // 有暂存文件时保持展开，不自动收回（方便连续操作）；清空后（空列表）才恢复自动收回
        if (StashItems.Count > 0) return;
        // 刚拖入文件后短时间内不收回（Drop 可能尚未处理完，等状态稳定）
        if ((DateTime.Now - _lastDropTime).TotalMilliseconds < 800) return;

        _isExpanded = false;

        var wa = SystemParameters.WorkArea;
        double targetLeft = wa.Right - TriggerBarWidth;

        AnimateLeft(targetLeft);
        TriggerArrow.Text = "«";
    }

    private void SetCollapsed()
    {
        _isExpanded = false;
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - TriggerBarWidth;
        TriggerArrow.Text = "«";
    }

    private void AnimateLeft(double targetLeft)
    {
        var anim = new DoubleAnimation(Left, targetLeft, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        anim.Completed += (_, _) => Left = targetLeft;
        BeginAnimation(Window.LeftProperty, anim);
    }

    private void TriggerBar_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        Expand();
    }

    /// <summary>固定开关：切换面板常驻展开。</summary>
    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        _isPinned = PinButton.IsChecked == true;
        PinButton.Content = _isPinned ? "已固定" : "固定";

        if (_isPinned)
        {
            Expand();
        }
        else
        {
            // 取消固定后，若鼠标已不在面板内则收回
            var pos = System.Windows.Input.Mouse.GetPosition(this);
            if (pos.X < 0 || pos.Y < 0 || pos.X > ActualWidth || pos.Y > ActualHeight)
            {
                Collapse();
            }
        }
    }

    /// <summary>
    /// 鼠标离开整个浮窗（含触发条和主面板）时收回。
    /// 但拖出文件进行中不收回。
    /// </summary>
    private void RootGrid_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragOutSuppressCollapse) return;
        // 右键菜单打开时（ContextMenu 是独立弹出层，会触发 MouseLeave），不收回
        if (StashList.ContextMenu is { IsOpen: true }) return;
        // 有暂存文件时保持展开，不收回（不看鼠标位置，由列表状态驱动）
        if (StashItems.Count > 0) return;
        // 固定开启时不收回
        if (_isPinned) return;

        Collapse();
    }

    /// <summary>纯文字开关：切换拖出 txt 时是当文字还是当文件。</summary>
    private void TextModeButton_Click(object sender, RoutedEventArgs e)
    {
        _textMode = TextModeButton.IsChecked == true;
        TextModeButton.Content = _textMode ? "文字中" : "纯文字";
    }

    #endregion

    #region 全局鼠标钩子（拖拽自动弹出）

    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONUP = 0x0202;
    private const int VK_LBUTTON = 0x01;

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelMouseProc? _proc;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private void InstallMouseHook()
    {
        if (_hookId != IntPtr.Zero) return;
        _proc = HookCallback;
        _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
    }

    private void UninstallMouseHook()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

            if (msg == WM_MOUSEMOVE)
            {
                bool leftDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
                if (leftDown)
                {
                    var wa = SystemParameters.WorkArea;
                    if (info.pt.X >= wa.Right - EdgeThreshold)
                    {
                        Dispatcher.BeginInvoke(Expand);
                    }
                }
            }
            else if (msg == WM_LBUTTONUP)
            {
                // 松开左键时，若鼠标不在窗口内，收回面板
                Dispatcher.BeginInvoke(() =>
                {
                    var rect = new Rect(Left, Top, ActualWidth, ActualHeight);
                    var pos = new System.Windows.Point(info.pt.X, info.pt.Y);
                    if (!rect.Contains(pos))
                    {
                        Collapse();
                    }
                });
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    #endregion

    #region 接收拖入文件/文字/图片

    /// <summary>判断拖入的数据是否可暂存（文件、文字、图片位图）。</summary>
    private static bool IsStashable(System.Windows.IDataObject data)
    {
        return data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            || data.GetDataPresent(System.Windows.DataFormats.UnicodeText)
            || data.GetDataPresent(System.Windows.DataFormats.Text)
            || data.GetDataPresent(System.Windows.DataFormats.Bitmap);
    }

    private void MainPanel_DragEnter(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = IsStashable(e.Data)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void MainPanel_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = IsStashable(e.Data)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void MainPanel_Drop(object sender, System.Windows.DragEventArgs e)
    {
        // 记录拖入时间：之后短时间内不自动收回，确保拖入后面板保持展开
        _lastDropTime = DateTime.Now;

        // 防止把「从本面板拖出」的数据再次丢回面板（会导致复制出相同文件）
        if (_dragOutSuppressCollapse)
        {
            e.Handled = true;
            return;
        }

        // 优先级：文件 > 文字 > 图片位图
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] paths)
            {
                foreach (var path in paths)
                {
                    // 只暂存文件；目录先跳过（MVP 只做文件）
                    if (File.Exists(path))
                    {
                        StashItems.Add(new StashItem
                        {
                            Path = path,
                            Name = System.IO.Path.GetFileName(path)
                        });
                    }
                }
            }
        }
        else if (e.Data.GetDataPresent(System.Windows.DataFormats.UnicodeText))
        {
            AddTextToStash(e.Data.GetData(System.Windows.DataFormats.UnicodeText) as string);
        }
        else if (e.Data.GetDataPresent(System.Windows.DataFormats.Text))
        {
            AddTextToStash(e.Data.GetData(System.Windows.DataFormats.Text) as string);
        }
        else if (e.Data.GetDataPresent(System.Windows.DataFormats.Bitmap))
        {
            AddBitmapToStash(e.Data.GetData(System.Windows.DataFormats.Bitmap) as System.Windows.Media.Imaging.BitmapSource);
        }
        e.Handled = true;
    }

    /// <summary>把拖入的一段文字保存为临时 .txt 并加入暂存栈。</summary>
    private void AddTextToStash(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        try
        {
            Directory.CreateDirectory(TempDir);
            // 用前若干字符做文件名前缀，便于识别
            var preview = text.Trim().Replace('\r', ' ').Replace('\n', ' ');
            if (preview.Length > 24) preview = preview[..24];
            var file = Path.Combine(TempDir, $"{preview}_{DateTime.Now:HHmmss}_{Guid.NewGuid().ToString("N")[..4]}.txt");
            File.WriteAllText(file, text);

            StashItems.Add(new StashItem
            {
                Path = file,
                Name = System.IO.Path.GetFileName(file),
                IsTemporary = true
            });
        }
        catch
        {
            // 保存临时文字失败不阻断
        }
    }

    /// <summary>把拖入的图片位图保存为临时 .png 并加入暂存栈。</summary>
    private void AddBitmapToStash(System.Windows.Media.Imaging.BitmapSource? bitmap)
    {
        if (bitmap == null) return;

        try
        {
            Directory.CreateDirectory(TempDir);
            var file = Path.Combine(TempDir, $"截图_{DateTime.Now:HHmmss}_{Guid.NewGuid().ToString("N")[..4]}.png");

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using (var fs = new FileStream(file, FileMode.Create))
            {
                encoder.Save(fs);
            }

            StashItems.Add(new StashItem
            {
                Path = file,
                Name = System.IO.Path.GetFileName(file),
                IsTemporary = true
            });
        }
        catch
        {
            // 保存临时图片失败不阻断
        }
    }

    private void UpdateEmptyHint()
    {
        bool hasItems = StashItems.Count > 0;
        EmptyHint.Text = hasItems
            ? $"已暂存 {StashItems.Count} 个文件"
            : "拖拽文件 / 文字 / 图片到这里暂存";
        ClearButton.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        RestoreButton.Visibility = File.Exists(RecycleFilePath) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>右键打开菜单前，定位到鼠标下的列表项；无目标项则隐藏「删除此项」。</summary>
    private void StashList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var pos = System.Windows.Input.Mouse.GetPosition(StashList);
        var hit = StashList.InputHitTest(pos) as System.Windows.DependencyObject;
        var item = FindVisualParent<ListBoxItem>(hit);
        _contextMenuItem = item?.DataContext as StashItem;

        // 没有指向具体项时，只保留「清空全部」，隐藏「删除此项」
        DeleteItemMenuItem.Visibility = _contextMenuItem != null ? Visibility.Visible : Visibility.Collapsed;
        ClearAllMenuItem.Visibility = StashItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>删除右键指向的暂存项。若该项处于多选中，则删除所有选中项。</summary>
    private void DeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (_contextMenuItem == null) return;

        // 若右键指向的项处于多选中（>1），删除全部选中项；否则只删这一项
        var selected = StashList.SelectedItems.Cast<StashItem>().ToList();
        if (selected.Count > 1 && selected.Contains(_contextMenuItem))
        {
            RemoveItems(selected);
        }
        else
        {
            RemoveItems(new[] { _contextMenuItem });
        }
        _contextMenuItem = null;
    }

    /// <summary>从暂存栈移除若干项；若是临时生成的文件（文字/图片），延迟删除磁盘上的临时文件（不立即删）。</summary>
    private void RemoveItems(IEnumerable<StashItem> items)
    {
        foreach (var it in items)
        {
            if (it.IsTemporary && File.Exists(it.Path))
            {
                ScheduleTempDelete(it.Path);
            }
            StashItems.Remove(it);
        }
    }

    /// <summary>安排临时文件延迟删除：先入队，定时器到点后统一删。</summary>
    private void ScheduleTempDelete(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        _pendingDelete.Add(path);
        EnsureCleanupTimer();
    }

    private void EnsureCleanupTimer()
    {
        if (_cleanupTimer != null) return;
        _cleanupTimer = new System.Windows.Threading.DispatcherTimer
        {
            // 给目标应用（QQ/微信/资源管理器）充足时间读取临时文件
            Interval = TimeSpan.FromSeconds(15)
        };
        _cleanupTimer.Tick += (_, _) => FlushPendingDelete();
        _cleanupTimer.Start();
    }

    private void FlushPendingDelete()
    {
        foreach (var path in _pendingDelete)
        {
            TryDeleteFile(path);
        }
        _pendingDelete.Clear();
        _cleanupTimer?.Stop();
        _cleanupTimer = null;
    }

    /// <summary>启动时清理 tmp 目录里不在暂存列表中的孤儿临时文件（上次残留）。</summary>
    private void CleanupOrphanTempFiles()
    {
        try
        {
            if (!Directory.Exists(TempDir)) return;
            var valid = new HashSet<string>(StashItems.Select(s => s.Path));
            foreach (var f in Directory.GetFiles(TempDir))
            {
                if (!valid.Contains(f))
                {
                    TryDeleteFile(f);
                }
            }
        }
        catch
        {
            // 清理失败不阻断
        }
    }

    /// <summary>清空全部暂存项（不删除磁盘上的原始文件）。清空前快照到回收文件，可一键找回。</summary>
    private void ClearAll_Click(object sender, RoutedEventArgs e) => ClearAllCore();

    private void ClearAllCore()
    {
        if (StashItems.Count == 0) return;

        // 先把当前列表快照保存到回收文件，再清空
        SaveRecycleSnapshot(StashItems.ToList());

        // 清空时删除临时生成的本地文件（文字/图片）；真实文件只从列表移除、不动磁盘
        foreach (var it in StashItems.ToList())
        {
            if (it.IsTemporary && File.Exists(it.Path))
            {
                ScheduleTempDelete(it.Path);
            }
        }

        StashItems.Clear();
        _contextMenuItem = null;
    }

    /// <summary>找回：读取回收快照，把上次清空的文件重新加回列表。</summary>
    private void Restore_Click(object sender, RoutedEventArgs e) => RestoreCore();

    private void RestoreCore()
    {
        var items = LoadRecycleSnapshot();
        if (items.Count == 0) return;

        // 逐个加回（同名允许重复；失效路径自动跳过）
        foreach (var item in items)
        {
            if (File.Exists(item.Path))
            {
                StashItems.Add(item);
            }
        }

        // 找回后清掉回收快照，避免重复找回
        TryDeleteFile(RecycleFilePath);
        UpdateEmptyHint();
    }

    private void SaveRecycleSnapshot(List<StashItem> items)
    {
        try
        {
            var dir = Path.GetDirectoryName(RecycleFilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(RecycleFilePath, json);
        }
        catch
        {
            // 快照失败不阻断清空
        }
    }

    private List<StashItem> LoadRecycleSnapshot()
    {
        try
        {
            if (!File.Exists(RecycleFilePath)) return new List<StashItem>();
            var json = File.ReadAllText(RecycleFilePath);
            return JsonSerializer.Deserialize<List<StashItem>>(json) ?? new List<StashItem>();
        }
        catch
        {
            return new List<StashItem>();
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); } catch { /* 忽略 */ }
    }

    #endregion

    #region 拖出还原

    private void ListItem_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var item = sender as ListBoxItem;
        _dragPivotItem = item?.DataContext as StashItem;
        _dragStartPoint = e.GetPosition(null);
        _dragSnapshot = StashList.SelectedItems.Cast<StashItem>().ToList();
        _dragStarted = false;

        // 阻止 ListBox 默认的「按下即切换选中」（Multiple 模式在 MouseDown 时 toggle），
        // 改为在 MouseUp（点击完成）时手动切换，避免按下/拖动瞬间选中状态抖动丢失。
        e.Handled = true;
        item?.CaptureMouse();
    }

    private void ListItem_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
            return;

        // 拖拽的锚定项（按下时命中的项）
        if (_dragPivotItem is not StashItem stash)
            return;

        // 拖拽阈值：移动超过一定距离才启动拖出，避免误触
        var current = e.GetPosition(null);
        if (Math.Abs(current.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _dragStarted = true;

        // 文件必须仍存在
        if (!File.Exists(stash.Path)) return;

        // 确定要拖出的文件集合：按下时若锚定项已在多选中，拖出整个选中集合；否则只拖这一项
        var itemsToDrag = _dragSnapshot.Count > 1 && _dragSnapshot.Contains(stash)
            ? _dragSnapshot
            : new List<StashItem> { stash };

        // 只拖出仍存在的文件
        var validPaths = itemsToDrag.Where(x => File.Exists(x.Path)).Select(x => x.Path).ToArray();
        if (validPaths.Length == 0) return;

        // 构造数据对象：纯文字模式下单个 .txt 走纯文字拖出，其余走文件拖放
        var dataObj = BuildDataObject(itemsToDrag, validPaths);

        _dragOutSuppressCollapse = true;
        try
        {
            var result = System.Windows.DragDrop.DoDragDrop(StashList, dataObj, System.Windows.DragDropEffects.Copy);

            // 拖出即取出：目标接受了拖放（返回非 None）就删除暂存项，避免反复复制同一文件
            if (result != System.Windows.DragDropEffects.None)
            {
                RemoveItems(itemsToDrag);
            }
        }
        finally
        {
            _dragOutSuppressCollapse = false;
            _dragPivotItem = null;
            _dragStarted = false;
            (sender as ListBoxItem)?.ReleaseMouseCapture();
        }
    }

    private void ListItem_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        (sender as ListBoxItem)?.ReleaseMouseCapture();

        // 点击（未进入拖拽）时切换选中状态
        if (!_dragStarted && _dragPivotItem is StashItem pivot)
        {
            ToggleSelection(pivot);
        }
        _dragPivotItem = null;
        _dragStarted = false;
    }

    /// <summary>
    /// 构造拖出用的 DataObject：
    /// 默认（纯文字模式关闭）：始终提供 CF_HDROP 文件拖放（可拖到桌面/资源管理器，QQ 显示为 txt 文件）。
    /// 纯文字模式开启：单个 .txt 只提供文字格式（QQ/微信直接粘贴为文字）。
    /// </summary>
    private System.Windows.DataObject BuildDataObject(List<StashItem> items, string[] validPaths)
    {
        var dataObj = new System.Windows.DataObject();

        // 纯文字模式 + 单个 .txt：纯文字拖出，不提供文件格式
        if (_textMode && items.Count == 1 &&
            string.Equals(System.IO.Path.GetExtension(items[0].Path), ".txt", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var text = File.ReadAllText(items[0].Path);
                if (!string.IsNullOrEmpty(text))
                {
                    dataObj.SetData(System.Windows.DataFormats.UnicodeText, text);
                    dataObj.SetData(System.Windows.DataFormats.Text, text);
                    return dataObj;
                }
            }
            catch
            {
                // 读不到内容就退回文件拖出
            }
        }

        // 默认：正常文件拖放
        dataObj.SetData(System.Windows.DataFormats.FileDrop, validPaths);
        return dataObj;
    }

    /// <summary>切换单个项的选中状态（Multiple 模式的手动实现）。</summary>
    private void ToggleSelection(StashItem item)
    {
        if (StashList.SelectedItems.Contains(item))
            StashList.SelectedItems.Remove(item);
        else
            StashList.SelectedItems.Add(item);
    }

    /// <summary>向上遍历可视树，找到指定类型的祖先元素。</summary>
    private static T? FindVisualParent<T>(System.Windows.DependencyObject? child) where T : System.Windows.DependencyObject
    {
        while (child != null)
        {
            if (child is T typed) return typed;
            child = System.Windows.Media.VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    #endregion

    #region 持久化

    private void SaveStash()
    {
        try
        {
            var dir = Path.GetDirectoryName(StashFilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(StashItems.ToList(), new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(StashFilePath, json);
        }
        catch
        {
            // 持久化失败不阻断主流程
        }
    }

    private void LoadStash()
    {
        try
        {
            if (!File.Exists(StashFilePath)) return;

            var json = File.ReadAllText(StashFilePath);
            var items = JsonSerializer.Deserialize<List<StashItem>>(json);
            if (items == null) return;

            foreach (var item in items)
            {
                // 校验路径是否仍有效，失效的跳过（文件已被移动/删除）
                if (File.Exists(item.Path))
                {
                    StashItems.Add(item);
                }
            }
        }
        catch
        {
            // 读取失败不阻断启动
        }
    }

    #endregion

    #region 系统托盘 + 全局快捷键（M5）

    private void InitTrayAndHotKey()
    {
        _trayIcon = new TrayIcon(TogglePanel, ClearAllCore, RestoreCore, ExitApplication);
        _hotKey = new HotKeyManager(this, TogglePanel);
    }

    /// <summary>切换浮窗整体显示/隐藏（托盘双击或 Ctrl+Alt+S）。</summary>
    private void TogglePanel()
    {
        if (_panelHidden)
        {
            _panelHidden = false;
            Show();
            PositionToRightEdge();
            SetCollapsed();
            InstallMouseHook();
        }
        else
        {
            _panelHidden = true;
            UninstallMouseHook();
            Hide();
        }
    }

    /// <summary>从托盘菜单退出应用。</summary>
    private void ExitApplication()
    {
        SaveStash();
        UninstallMouseHook();
        System.Windows.Application.Current.Shutdown();
    }

    #endregion
}
