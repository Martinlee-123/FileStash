# FileStash · 文件暂存栈

> Windows 平台的**系统级文件拖拽暂存架**，对标 macOS 的 [Yoink](https://eternalstorms.at/yoink/mac/)。
>
> 拖动文件时屏幕侧边弹出浮窗，把文件临时「暂存」进去；需要时再从侧边拖出到任意文件夹或应用。
> 不用再为了「从 A 文件夹拖到 B 文件夹」而反复切窗口、找位置。

**技术栈：C# + WPF + .NET 8** ｜ 平台：Windows 10/11 (x64) ｜ 协议：MIT

---

## 📥 下载与使用

**👉 点击右侧 [Releases](https://github.com/Martinlee-123/FileStash/releases) 下载最新版 `FileStash-win-x64.zip`**（编译好的免安装版，双击即用）。

> ⚠️ 别点页面右上角绿色 **Code → Download ZIP**——那是源码包，不能直接运行。

**使用步骤：**

1. 在 [Releases](https://github.com/Martinlee-123/FileStash/releases) 页下载 `FileStash-win-x64.zip`
2. 解压得到 `FileStash.exe`（单文件，已内置 .NET 运行时，**无需安装**）
3. 双击 `FileStash.exe` 运行

**首次运行提示：**

- 若弹 SmartScreen「未知发布者」（未做代码签名），点「更多信息 → 仍要运行」
- 首次启动稍慢（单文件解压运行时），属正常现象

---

## ✨ 功能

- **侧边浮窗**：常驻窄边触发条，拖拽靠近屏幕右边缘自动弹出，半透明玻璃质感、圆角、置顶
- **拖入暂存**：拖入文件 / 文字 / 图片 → 自动暂存
- **拖出还原**：从列表拖出到任意位置，**拖出即取出**（不会残留副本）
- **多选**：单击切换选中，支持多选拖入 / 拖出 / 删除
- **持久化**：重启不丢（存于 `%AppData%\FileStash\stash.json`），失效路径自动跳过
- **文字 / 图片暂存**：选中文字或图片拖入 → 自动生成临时文件
- **「纯文字」开关**：拖出单个 txt 时可切换为纯文字格式（适配 QQ / 微信直接显示文字）
- **「固定」开关**：面板常驻展开，不自动收回
- **系统托盘 + 全局快捷键**：`Ctrl+Alt+S` 随时呼出 / 收起
- **删除 / 清空 / 找回**：支持单条删除、一键清空（可一键找回）
- **文件图标缩略图**：列表项显示系统 shell 图标
- **双击打开**：双击列表项直接用系统默认程序打开文件
- **空格预览**：选中项按空格弹出 QuickLook 预览（图片原图 / 文件大图标 + 信息）
- **免安装分发**：可发布为单文件 exe，自带 .NET 运行时，双击即用

## 🎬 核心交互

1. 拖起一个文件（文字、图片、文件均可）
2. 拖动靠近**屏幕右边缘**，浮窗自动弹出
3. 把文件「丢」进浮窗 → 暂存
4. 需要时，从浮窗把文件**拖出**到目标位置

## 🚀 编译运行

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

```powershell
cd FileStash
dotnet build -c Debug
.\bin\Debug\net8.0-windows\FileStash.exe
```

**发布免安装单文件版**（自带运行时，用户无需装 .NET）：

```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o ..\发布版
```

## 🛠 技术要点

| 难点 | 方案 |
|---|---|
| 拖拽触发浮窗 | 全局低层鼠标钩子 `SetWindowsHookEx(WH_MOUSE_LL)` + `GetAsyncKeyState` 判断左键拖拽 |
| 数据源区分 | 文件 `CF_HDROP` / 文字 `CF_TEXT` / 图片 `CF_BITMAP`，优先级：文件 > 文字 > 图片 |
| 反向拖出 | 构造 OLE `DataObject` 后 `DoDragDrop`，还原为目标应用可接受的数据对象 |
| 文件图标 | `SHGetFileInfo`（`SHGFI_ICON`，句柄取自 `shfi.hIcon` 字段）提取 16×16 shell 图标 |
| 临时文件生命周期 | 拖出 / 删除后**延迟 15 秒**再删，避免目标应用异步读取到空文件 |

## 📁 项目结构

```
FileStash/
├─ App.xaml / App.xaml.cs       # 应用入口
├─ MainWindow.xaml              # UI：触发条 + 主面板 + 列表 + 样式
├─ MainWindow.xaml.cs           # 全部逻辑（收放/钩子/拖入拖出/持久化/托盘/快捷键）
├─ StashItem.cs                 # 数据模型：Path + Name + IsTemporary
├─ FileIconConverter.cs         # 文件图标转换器（shell 图标 → ImageSource）
├─ TrayIcon.cs                  # 系统托盘图标
├─ HotKeyManager.cs             # 全局快捷键 Ctrl+Alt+S
└─ FileStash.csproj
```

## 📌 已定的交互决策

1. 浮窗形态：**常驻窄边**（非自动隐藏）
2. 拖出后：**拖出即取出**——成功拖出即删除暂存项
3. 浮窗位置：**固定右侧**（主屏工作区右边缘）
4. 同名文件：**允许重复**（不合并去重）
5. 面板有内容时：**不自动缩回**
6. 多选：**单击切换选中**（松手生效）
7. 全局快捷键：`Ctrl+Alt+S`

## ⚠️ 已知边界

- **仅 Windows**（WPF / OLE / Win32 钩子为 Windows 专属 API）
- **多显示器**：当前仅贴主屏右边缘，未处理副屏
- **UAC 受保护目录**：拖出到需提权的目录未处理
- **拖出 effect**：个别目标应用可能把「复制」识别为「移动」
- 免安装 exe 无代码签名，首次运行 SmartScreen 可能提示「未知发布者」

## 📄 License

[MIT](LICENSE) © 2026 Martinlee-123
