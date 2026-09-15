using System.Runtime.InteropServices;

namespace FileStash;

/// <summary>
/// 全局快捷键注册（Ctrl+Alt+S 呼出/隐藏浮窗）。
/// 用 RegisterHotKey 挂到线程消息队列，配合 HwndSource 的 AddHook 捕获 WM_HOTKEY。
/// </summary>
public sealed class HotKeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int MOD_CONTROL = 0x0002;
    private const int MOD_ALT = 0x0001;
    private const int VK_S = 0x53;

    private const int HotKeyId = 0x4653; // 'FS'

    private IntPtr _handle = IntPtr.Zero;
    private System.Windows.Interop.HwndSource? _source;
    private readonly Action _onPressed;
    private bool _registered;

    public HotKeyManager(System.Windows.Window window, Action onPressed)
    {
        _onPressed = onPressed;

        var helper = new System.Windows.Interop.WindowInteropHelper(window);
        _handle = helper.Handle;
        _source = System.Windows.Interop.HwndSource.FromHwnd(_handle);
        _source?.AddHook(WndProc);

        Register();
    }

    private void Register()
    {
        if (_registered || _handle == IntPtr.Zero) return;
        _registered = RegisterHotKey(_handle, HotKeyId, MOD_CONTROL | MOD_ALT, VK_S);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotKeyId)
        {
            _onPressed();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered && _handle != IntPtr.Zero)
        {
            UnregisterHotKey(_handle, HotKeyId);
            _registered = false;
        }
        if (_source != null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
