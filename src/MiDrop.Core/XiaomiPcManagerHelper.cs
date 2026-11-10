using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;

namespace MiDrop.Core;

/// <summary>
/// 小米电脑管家的定位与发文件实现。
///
/// 6.x（实测 6.0.2.30）的右键发文件入口不是 WM_COPYDATA，而是命令行：
///   XiaomiPcManager.exe --contextmenu_dropfile=payload:&lt;base64&gt;
/// 其中 payload 是 UTF-8 的文本，形状为：
///   parent_dir:&lt;第一个选中项的父目录&gt;|&lt;绝对路径1&gt;|&lt;绝对路径2&gt;
///
/// 官方那支 MiDropShellExt.dll（反编译 + 运行时实测）并不自己拼 base64，而是：
///   1. 建共享内存 Local\MiDropFileMappingObject_&lt;pid&gt;_&lt;tick&gt;_&lt;序号&gt;，
///      内容就是上面那段 UTF-16LE 文本 + 结尾 '\0'；
///   2. CreateProcess "&lt;安装根&gt;\Launch.exe" " --contextmenu_dropfile="mapping:&lt;共享内存名&gt;"；
///   3. Launch.exe（小米电脑管家启动器 6.0.2.30）读共享内存、转成 UTF-8 + base64，
///      再唤醒已在运行的 XiaomiPcManager.exe 走上面那条命令行。
/// 两条路本机都实测能弹出「小米互传 · 发送」窗口，所以这里先走共享内存（不受命令行长度限制），
/// 共享内存不可用时回落到直接把 base64 塞进命令行。
/// </summary>
public class XiaomiPcManagerHelper
{
    private static readonly IReadOnlyList<string> PcManagerRegKeys = [
        // 5.x
        "{504d69c0-cb52-48df-b5b5-7161829fabc8}",
        
        // 4.x
        "{1bca9901-05c3-4d01-8ad4-78da2eac9b3f}",
        ];

    /// <summary>
    /// 各版本的主程序文件名，按优先级排列。
    /// hyperConnect.exe 属于「小米互联服务内测版」，MiPcContinuity.exe 属于 4.x/5.x 的部分装法。
    /// </summary>
    private static readonly IReadOnlyList<string> ExecuteFileNames = [
        "hyperConnect.exe",
        "XiaomiPcManager.exe",
        "MiPcContinuity.exe",
        ];

    /// <summary>
    /// 6.x 右键发文件的命令行开关，官方 DLL 与 XiaomiPcManager.exe 都用这一条（实测字符串在
    /// MiDropShellExt.dll 的 0x18000c118 处，值为 <c>" --contextmenu_dropfile="</c>）。
    /// </summary>
    private const string ContextMenuDropFileSwitch = "--contextmenu_dropfile=";

    /// <summary>
    /// 6.x 共享内存名前缀，实测见 MiDropShellExt.dll 的 0x18000c088 附近。
    /// </summary>
    private const string FileMappingPrefix = @"Local\MiDropFileMappingObject_";

    /// <summary>
    /// 共享内存要在 Launch.exe 退出后再留这么久，等 XiaomiPCManager 第二实例把它读走。实测提前释放会导致
    /// 「Context menu file payload unavailable」，那一路还会把第二实例的 WPF 线程搞崩。
    /// </summary>
    private const int FileMappingLifetimeMilliseconds = 3000;

    private static int _fileMappingSequence;

    public static string GetInstallPath()
    {
        // 6.x：App Paths 直接给出主程序所在目录（已含 6.0.2.30 这层版本目录），优先级最高
        var appPathFolder = ReadAppPathFolder();
        if (Directory.Exists(appPathFolder)) return appPathFolder!;

        // 6.x 兜底：卸载项。6.x 的 InstallLocation 为空，靠 UninstallString / DisplayIcon 反推
        var uninstallFolder = ReadUninstallFolder();
        if (Directory.Exists(uninstallFolder)) return uninstallFolder!;

        // 4.x / 5.x / 互联服务内测版老路径，逻辑与原实现一致
        try
        {
            for (int i = 0; i < PcManagerRegKeys.Count; i++)
            {
                using (var subKey = Registry.ClassesRoot.OpenSubKey($"CLSID\\{PcManagerRegKeys[i]}\\InprocServer32"))
                {
                    if (subKey != null && subKey.GetValue(null) is string path && !string.IsNullOrEmpty(path))
                    {
                        var folder = Path.GetDirectoryName(path);
                        if (folder?.Contains("\\native-interconnect\\") is true)
                        {
                            while (!string.IsNullOrEmpty(folder))
                            {
                                if (File.Exists(Path.Combine(folder, "hyperConnect.exe"))) break;
                                folder = Path.GetDirectoryName(folder);
                            }
                        }
                        if (Directory.Exists(folder))
                        {
                            return folder;
                        }
                    }
                }
            }
        }
        catch { }

        return string.Empty;
    }

    /// <summary>
    /// 6.x：App Paths\XiaomiPcManager.exe 直接给出主程序全路径（HKLM 优先，HKCU 兜底）。
    /// </summary>
    private static string? ReadAppPathFolder()
    {
        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            var folder = ReadRegistryFolder(
                root,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\XiaomiPcManager.exe",
                null);
            if (Directory.Exists(folder)) return folder;
        }

        return null;
    }

    /// <summary>
    /// 6.x 兜底：卸载项。6.x 的 InstallLocation 为空，靠 UninstallString / DisplayIcon 反推目录。
    /// </summary>
    private static string? ReadUninstallFolder()
    {
        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var valueName in new[] { "UninstallString", "DisplayIcon" })
            {
                var folder = ReadRegistryFolder(
                    root,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\XiaomiPCManager",
                    valueName);
                if (Directory.Exists(folder)) return folder;
            }

            var installLocation = ReadRegistryFolder(
                root,
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\XiaomiPCManager",
                "InstallLocation",
                isDirectory: true);
            if (Directory.Exists(installLocation)) return installLocation;
        }

        return null;
    }

    private static string? ReadRegistryFolder(RegistryKey root, string subKeyPath, string? valueName, bool isDirectory = false)
    {
        try
        {
            using var subKey = root.OpenSubKey(subKeyPath);
            if (subKey?.GetValue(valueName) is not string raw || string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            return isDirectory ? raw.Trim().Trim('"') : GetFolderFromPathValue(raw);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 把注册表里可能带引号、带命令行参数、带 ",0" 图标索引的路径值还原成目录。
    /// </summary>
    private static string? GetFolderFromPathValue(string raw)
    {
        var path = raw.Trim();

        if (path.StartsWith('"'))
        {
            var end = path.IndexOf('"', 1);
            if (end > 1) path = path[1..end];
        }

        // 以最后一个 ".exe" 为界，同时吃掉 DisplayIcon 的 ",0" 和 UninstallString 的 " /uninstall"；
        // 不能用第一个空格切，路径本身可能含空格（C:\Program Files\...）
        var exeIndex = path.LastIndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeIndex > 0) path = path[..(exeIndex + 4)];

        path = path.Trim();

        if (string.IsNullOrWhiteSpace(path)) return null;

        return Path.GetDirectoryName(path);
    }

    public static string GetExecuteFile()
    {
        var installPath = GetInstallPath();
        if (!string.IsNullOrEmpty(installPath))
        {
            for (int i = 0; i < ExecuteFileNames.Count; i++)
            {
                var filePath = Path.Combine(installPath, ExecuteFileNames[i]);
                if (File.Exists(filePath)) return filePath;
            }
        }
        return string.Empty;
    }

    public static string GetIconString()
    {
        var installPath = GetInstallPath();
        if (!string.IsNullOrEmpty(installPath))
        {
            // 5.x：GetInstallPath() 返回的就是 MiSmartShare 目录，Assets 在它下面。
            // 6.x：GetInstallPath() 返回版本目录（…\6.0.2.30），小米互传的 Assets 在
            //       …\6.0.2.30\MiSmartShare\Assets\midrop_logo.ico，多了一层。
            // 两处都探一遍，谁先命中用谁，避免 6.x 掉到 exe 图标上（管家图标是个浅色方块，
            // 在深色右键菜单里几乎看不出是「互传」）。
            var icoCandidates = new[]
            {
                Path.Combine(installPath, "Assets", "midrop_logo.ico"),
                Path.Combine(installPath, "MiSmartShare", "Assets", "midrop_logo.ico"),
            };
            for (int i = 0; i < icoCandidates.Length; i++)
            {
                if (File.Exists(icoCandidates[i])) return icoCandidates[i];
            }

            var exeFilePath = GetExecuteFile();
            if (File.Exists(exeFilePath)) return $"{exeFilePath},0";
        }

        return string.Empty;
    }

    public static Task<bool> LaunchAsync(CancellationToken cancellationToken)
    {
        return LaunchAsync(null, cancellationToken);
    }

    public static Task<bool> LaunchAsync(string? arguments, CancellationToken cancellationToken)
    {
        var messageWindow = FindMessageWindow();
        if (messageWindow != 0)
        {
            if (string.IsNullOrEmpty(arguments)) return Task.FromResult(true);
        }

        var executeFile = GetExecuteFile();
        if (!string.IsNullOrEmpty(executeFile))
        {
            return LaunchAsyncCore(executeFile, arguments, cancellationToken);
        }
        return Task.FromResult(false);
    }

    public static async Task<bool> SendCachedFilesAsync(string cacheKey, TimeSpan timeout)
    {
        var xiaomiFile = await FilesHelper.GetXiaomiFileAsync(cacheKey, default).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(xiaomiFile))
        {
            return await SendFilesAsyncCore(xiaomiFile, timeout);
        }
        return false;
    }

    public static Task<bool> SendFilesAsync(string[] files, TimeSpan timeout)
    {
        var xiaomiFile = CreateXiaomiFile(files);
        if (!string.IsNullOrEmpty(xiaomiFile))
        {
            return SendFilesAsyncCore(xiaomiFile, timeout);
        }

        return Task.FromResult(false);
    }

    private static Task<bool> LaunchAsyncCore(string executeFile, string? arguments, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(executeFile, arguments ?? "")
        {
            UseShellExecute = true
        };

        var tcs = new TaskCompletionSource<bool>();

        var registration = cancellationToken.Register(() =>
        {
            tcs.TrySetCanceled(cancellationToken);
        });

        var thread = new Thread(() =>
        {
            try
            {
                var process = Process.Start(executeFile);
                while (!cancellationToken.IsCancellationRequested && !process.HasExited)
                {
                    var messageWindow = FindMessageWindow();
                    if (messageWindow != 0)
                    {
                        tcs.TrySetResult(true);
                        break;
                    }

                    Thread.Sleep(500);
                }
            }
            catch { }
            registration.Dispose();
            tcs.TrySetResult(false);
        });
        thread.IsBackground = true;
        thread.Start();

        return tcs.Task;
    }


    internal static Task<bool> SendFilesAsyncCore(string xiaomiFile, TimeSpan timeout)
    {
        if (string.IsNullOrEmpty(xiaomiFile)) return Task.FromResult(false);
        //if (files == null || files.Length == 0) return Task.FromResult(false);

        // 版本分流只认 exe 身份，不猜版本号：6.x 的 XiaomiPcManager.exe 走命令行，
        // 4.x/5.x 与「互联服务内测版」的 MiPcContinuity.exe / hyperConnect.exe 继续走 WM_COPYDATA。
        // 两条路互斥，避免老版本收到双份请求。
        var executeFile = GetExecuteFile();
        if (IsContextMenuDropFileExecutable(executeFile))
        {
            return SendFilesByContextMenuCommandAsync(xiaomiFile, executeFile, timeout);
        }

        var messageWindow = FindMessageWindow();
        if (messageWindow == 0) return Task.FromResult(false);

        var tcs = new TaskCompletionSource<bool>();

        var thread = new Thread(() =>
        {
            try
            {
                tcs.TrySetResult(SendFilesCore(messageWindow, xiaomiFile, timeout));
            }
            catch { }
            tcs.TrySetResult(false);

            unsafe static bool SendFilesCore(nint messageWindow, string xiaomiFiles, TimeSpan timeout)
            {
                fixed (char* ptr = xiaomiFiles)
                {
                    var s = default(COPYDATASTRUCT);
                    s.dwData = 0;
                    s.cbData = (xiaomiFiles.Length) * 2;
                    s.lpData = (nint)ptr;

                    var timeout2 = unchecked((uint)timeout.TotalMilliseconds);
                    if (timeout2 > 15 * 1000) timeout2 = 15 * 1000;
                    if (timeout2 < 0) timeout2 = 0;

                    var res = SendMessageTimeoutW(messageWindow, 74, (IntPtr)1, (nint)(&s), 0, timeout2, out var lpdwResult);
                    return res != 0;
                }
            }
        });
        thread.IsBackground = true;
        thread.Start();

        return tcs.Task;
    }

    internal static string CreateXiaomiFile(string[] files)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < files.Length; i++)
        {
            var path = files[i];

            if (!string.IsNullOrEmpty(path)
                && Path.IsPathRooted(path))
            {
                if (sb.Length > 0) sb.Append('|');
                sb.Append(path);
            }
        }

        return sb.ToString();
    }


    /// <summary>
    /// 只有 6.x 的 XiaomiPcManager.exe 认 <c>--contextmenu_dropfile</c>；其余（hyperConnect.exe、
    /// MiPcContinuity.exe）都还是 WM_COPYDATA。
    /// </summary>
    private static bool IsContextMenuDropFileExecutable(string executeFile)
    {
        return string.Equals(
            Path.GetFileName(executeFile),
            "XiaomiPcManager.exe",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 拼 6.x 的 payload 文本：<c>parent_dir:&lt;父目录&gt;|&lt;路径1&gt;|&lt;路径2&gt;</c>。
    ///
    /// parent_dir 取**第一个**选中项的父目录，这是官方 DLL 的实际行为（反编译 FUN_180005d90：
    /// 遍历 IShellItemArray，只在父目录字符串还为空时算一次；FUN_1800075b0 再把它拼到最前面）。
    /// 跨目录多选实测（14:02）：parent_dir 仍是第一个文件的目录，日志
    /// <c>Context menu selection parsed: count=2</c> / <c>SetSelectedFiles: fileCount=2</c>，
    /// 两个不同目录的文件都被接收，所以多目录不需要特殊处理。
    /// </summary>
    internal static string BuildContextMenuPayload(string xiaomiFiles)
    {
        if (string.IsNullOrEmpty(xiaomiFiles)) return string.Empty;

        var separator = xiaomiFiles.IndexOf('|');
        var firstPath = separator < 0 ? xiaomiFiles : xiaomiFiles[..separator];

        try
        {
            var parentDir = Path.GetDirectoryName(firstPath);
            if (!string.IsNullOrEmpty(parentDir)) return $"parent_dir:{parentDir}|{xiaomiFiles}";
        }
        catch
        {
            // 路径不合法就退回不带 parent_dir 的形式，让管家自己判断
        }

        return xiaomiFiles;
    }

    private static Task<bool> SendFilesByContextMenuCommandAsync(string xiaomiFiles, string executeFile, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<bool>();
        var thread = new Thread(() =>
        {
            try
            {
                tcs.TrySetResult(SendFilesByContextMenuCommand(xiaomiFiles, executeFile, timeout));
            }
            catch { }
            tcs.TrySetResult(false);
        });
        thread.IsBackground = true;
        thread.Start();

        return tcs.Task;
    }

    private static bool SendFilesByContextMenuCommand(string xiaomiFiles, string executeFile, TimeSpan timeout)
    {
        var payload = BuildContextMenuPayload(xiaomiFiles);
        if (string.IsNullOrEmpty(payload)) return false;

        // 首选：照官方 DLL 的做法走共享内存，命令行里只有 <c>mapping:&lt;名字&gt;</c>，
        // 因此不受 32767 字符的命令行上限影响（实测 14:01 用自定义名字的共享内存也能被 Launch.exe 读到）。
        var launchFile = FindLaunchExecutable(executeFile);
        if (!string.IsNullOrEmpty(launchFile) && TrySendByFileMapping(launchFile, payload, timeout)) return true;

        // 兜底：Launch.exe 不在或共享内存失败时，直接沿用实测过的 base64 命令行。
        // 注意 WM_COPYDATA 在 6.x 上不弹窗（6.x 的 wParam=1 只走到 showFromMenuWindow 回调，
        // 实测 no subscriber），所以 6.x 的兜底只能是这条，不能回落到 COPYDATA。
        return TrySendByPayloadArgument(executeFile, payload);
    }

    /// <summary>
    /// Launch.exe 在安装根目录（6.0.2.30 的上一层）。官方 DLL 是从自己的模块路径向上退三层再加
    /// <c>\Launch.exe</c>，等价于「主程序目录的上一层」；这里按 App Paths 得到的目录直接找，找不到再上一层，
    /// 不依赖 DLL 自己的落盘位置。
    /// </summary>
    private static string? FindLaunchExecutable(string executeFile)
    {
        var installPath = Path.GetDirectoryName(executeFile);
        if (string.IsNullOrEmpty(installPath)) return null;

        foreach (var folder in new[] { installPath, Path.GetDirectoryName(installPath) })
        {
            if (string.IsNullOrEmpty(folder)) continue;

            var candidate = Path.Combine(folder, "Launch.exe");
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    private static bool TrySendByFileMapping(string launchFile, string payload, TimeSpan timeout)
    {
        MemoryMappedFile? mapping = null;
        try
        {
            // 内容 = UTF-16LE 的 payload + 结尾 '\0'，与官方 DLL 完全一致（mapping 大小 = 文本长度 * 2 + 2）
            var body = Encoding.Unicode.GetBytes(payload + '\0');
            var mappingName = string.Format(
                "{0}{1}_{2}_{3}",
                FileMappingPrefix,
                Environment.ProcessId,
                Environment.TickCount64,
                Interlocked.Increment(ref _fileMappingSequence));

            mapping = MemoryMappedFile.CreateNew(mappingName, body.Length);
            using (var view = mapping.CreateViewAccessor())
            {
                view.WriteArray(0, body, 0, body.Length);
                view.Flush();
            }

            var psi = new ProcessStartInfo(launchFile) { UseShellExecute = false };
            // 不要自己加引号：官方那支 DLL 是把整条命令行交给 CreateProcess，
            // 由 CRT 的引号规则在到达 Launch.exe 前去掉引号；这里用 ArgumentList 传参，
            // 内嵌的引号会被转义成 \" 并原样送达，Launch.exe 就认不出 mapping: 前缀了（实测踩过）。
            psi.ArgumentList.Add($"{ContextMenuDropFileSwitch}mapping:{mappingName}");

            using var process = Process.Start(psi);
            if (process is null) return false;

            // Launch.exe 只负责转交参数，真正打开共享内存的是随后起来的 XiaomiPCManager 第二实例
            // （日志：GetMiDropFileMapping attempt 1/2 failed → Context menu file payload unavailable）。
            // 官方 DLL 靠「可继承句柄」把生命周期交给 Launch.exe；这里没法继承，就多留一会儿再释放。
            var wait = (int)Math.Clamp(timeout.TotalMilliseconds, 1000, 5000);
            process.WaitForExit(wait);
            Thread.Sleep(FileMappingLifetimeMilliseconds);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            mapping?.Dispose();
        }
    }

    private static bool TrySendByPayloadArgument(string executeFile, string payload)
    {
        try
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));

            var psi = new ProcessStartInfo(executeFile) { UseShellExecute = false };
            psi.ArgumentList.Add($"{ContextMenuDropFileSwitch}payload:{encoded}");

            using var process = Process.Start(psi);
            return process is not null;
        }
        catch
        {
            return false;
        }
    }

    private unsafe static nint FindMessageWindow()
    {
        const string WindowClassName1 = "XiaomiPCManager";
        const string WindowClassName2 = "MiPcContinuity";

        fixed (char* pClassName = WindowClassName1)
        {
            var hwnd = FindWindowW(pClassName, null);
            if (hwnd > 0) return hwnd;
        }
        fixed (char* pClassName = WindowClassName2)
        {
            var hwnd = FindWindowW(pClassName, null);
            if (hwnd > 0) return hwnd;
        }
        return 0;
    }

    [DllImport("user32.dll")]
    private static unsafe extern nint SendMessageW(nint hWnd, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static unsafe extern int SendMessageTimeoutW(nint hWnd, int Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out nint lpdwResult);

    [DllImport("user32.dll")]
    private static unsafe extern nint FindWindowW(char* lpClassName, char* lpWindowName);

    private struct COPYDATASTRUCT
    {
        public nint dwData;

        public int cbData;

        public nint lpData;
    }

}
