using Microsoft.Win32;
using MiDrop.Core;
using ShellExtensions;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace ShellExt
{
    public static class DllMain
    {
        private static readonly Guid PackagedClsid1 = new Guid("976D43D8-907F-46AF-B47F-07084C71A2F0");
        private static readonly Guid PackagedClsid2 = new Guid("3EE2A0FD-EA1B-4630-9972-02F51EC786FF");
        private static readonly Guid PackagedClsid3 = new Guid("3FA37C0D-64B9-4E51-A9DF-411647E9DE79");

        // 6.x 裸 COM 注册（regsvr32）用的经典 ExplorerCommand CLSID。
        // 实测注册表：HKLM\SOFTWARE\Classes\*\shell\MiDropExtMenu 与
        // Directory\shell\MiDropExtMenu 的 ExplorerCommandHandler 都指向这个值。
        private static readonly Guid DropFileCommandClsid = new Guid("504d69c0-cb52-48df-b5b5-7161829fabc8");
        private const string DropFileCommandClsidString = "{504d69c0-cb52-48df-b5b5-7161829fabc8}";

        // 只挂这两个动词，别的不碰。
        private static readonly string[] DropFileCommandVerbs = new[] { @"*\shell", @"Directory\shell" };

        private const string DropFileCommandKeyName = "MiDropExtMenu";

        private const int S_OK = 0;
        private const int E_NOTIMPL = unchecked((int)0x80004001);
        private const int E_FAIL = unchecked((int)0x80004005);
        private const int E_ACCESSDENIED = unchecked((int)0x80070005);

#pragma warning disable CA2255
        [ModuleInitializer]
#pragma warning restore CA2255
        public static void _DllMain()
        {
            try
            {
                var folder = XiaomiPcManagerHelper.GetInstallPath();
                if (Directory.Exists(folder))
                {
                    ShellExtensions.ShellExtensionsClassFactory.RegisterInProcess(
                        PackagedClsid1,
                        () => new ContextMenu(
                            "XiaomiShare",
                            XiaomiPcManagerHelper.GetIconString(),
                            "使用小米互传发送"));

                    // 同一套实现再挂到注册表里实际使用的经典 CLSID 上，
                    // 打包 CLSID 一个都不删。
                    ShellExtensions.ShellExtensionsClassFactory.RegisterInProcess(
                        DropFileCommandClsid,
                        () => new ContextMenu(
                            "XiaomiShare",
                            XiaomiPcManagerHelper.GetIconString(),
                            "使用小米互传发送"));
                }
            }
            catch { }

            try
            {
                var folder = HonorPCManagerHelper.GetInstallPath();
                if (Directory.Exists(folder))
                {
                    ShellExtensions.ShellExtensionsClassFactory.RegisterInProcess(
                        PackagedClsid2,
                        () => new ContextMenu(
                            "HonorShare",
                            Path.GetFullPath(Path.Combine(DllModule.BaseDirectory, "..", "HonorImages", "Share.ico")),
                            "使用荣耀分享发送"));
                }
            }
            catch { }

            try
            {
                var folder = HuaweiPCManagerHelper.GetInstallPath();
                if (Directory.Exists(folder))
                {
                    ShellExtensions.ShellExtensionsClassFactory.RegisterInProcess(
                        PackagedClsid3,
                        () => new ContextMenu(
                            "HuaweiShare",
                            Path.GetFullPath(Path.Combine(DllModule.BaseDirectory, "..", "HuaweiImages", "Share.ico")),
                            "使用华为分享发送"));
                }
            }
            catch { }
        }

        [UnmanagedCallersOnly(EntryPoint = "DllCanUnloadNow")]
        private static int DllCanUnloadNow() => ShellExtensions.ShellExtensionsClassFactory.DllCanUnloadNow();

        [UnmanagedCallersOnly(EntryPoint = "DllGetClassObject")]
        private unsafe static int DllGetClassObject(Guid* clsid, Guid* riid, void** ppv) => ShellExtensions.ShellExtensionsClassFactory.DllGetClassObject(clsid, riid, ppv);

        // regsvr32 走的那三个导出：只写小米互传的键，别的一概不碰。
        [UnmanagedCallersOnly(EntryPoint = "DllRegisterServer")]
        private static int DllRegisterServer()
        {
            try
            {
                var dllFilePath = DllModule.Location;
                if (string.IsNullOrEmpty(dllFilePath))
                {
                    return E_FAIL;
                }

                using var classes = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64);

                using (var inprocServer32 = classes.CreateSubKey($@"CLSID\{DropFileCommandClsidString}\InprocServer32"))
                {
                    inprocServer32.SetValue(null, dllFilePath, RegistryValueKind.String);
                    inprocServer32.SetValue("ThreadingModel", "Apartment", RegistryValueKind.String);
                }

                // 经典（IExplorerCommand 之外的那条）菜单分支只认动词键上的 Icon 值，
                // 不读 IExplorerCommand::GetIcon。不写这个值，菜单项就是纯文字没图标。
                // 取小米互传自己的 midrop_logo.ico（6.x 在 MiSmartShare\Assets 下），
                // 取不到就退回管家 exe 的图标，再取不到就不写——绝不写空串。
                var icon = XiaomiPcManagerHelper.GetIconString();

                foreach (var verb in DropFileCommandVerbs)
                {
                    using var verbKey = classes.CreateSubKey($@"{verb}\{DropFileCommandKeyName}");
                    verbKey.SetValue("ExplorerCommandHandler", DropFileCommandClsidString, RegistryValueKind.String);
                    if (!string.IsNullOrEmpty(icon))
                    {
                        verbKey.SetValue("Icon", icon, RegistryValueKind.String);
                    }
                }

                return S_OK;
            }
            catch (UnauthorizedAccessException)
            {
                return E_ACCESSDENIED;
            }
            catch (System.Security.SecurityException)
            {
                return E_ACCESSDENIED;
            }
            catch
            {
                return E_FAIL;
            }
        }

        [UnmanagedCallersOnly(EntryPoint = "DllUnregisterServer")]
        private static int DllUnregisterServer()
        {
            try
            {
                using var classes = RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64);

                classes.DeleteSubKeyTree($@"CLSID\{DropFileCommandClsidString}", false);

                foreach (var verb in DropFileCommandVerbs)
                {
                    var verbPath = $@"{verb}\{DropFileCommandKeyName}";
                    using (var verbKey = classes.OpenSubKey(verbPath, true))
                    {
                        verbKey?.DeleteValue("ExplorerCommandHandler", false);
                        verbKey?.DeleteValue("Icon", false);
                    }
                    PruneEmptyKey(classes, verbPath);
                }

                return S_OK;
            }
            catch (UnauthorizedAccessException)
            {
                return E_ACCESSDENIED;
            }
            catch (System.Security.SecurityException)
            {
                return E_ACCESSDENIED;
            }
            catch
            {
                return E_FAIL;
            }
        }

        // 只写小米这套键；不做计划外的任何事。
        [UnmanagedCallersOnly(EntryPoint = "DllInstall")]
        private unsafe static int DllInstall(int bInstall, char* pszCmdLine) => E_NOTIMPL;

        private static void PruneEmptyKey(RegistryKey classes, string subKeyPath)
        {
            using var key = classes.OpenSubKey(subKeyPath, false);
            if (key is null)
            {
                return;
            }
            if (key.ValueCount == 0 && key.SubKeyCount == 0)
            {
                classes.DeleteSubKey(subKeyPath, false);
            }
        }

        // 诊断日志：只用来定位裸 COM（regsvr32）这条路为什么没动静。
        // 落盘到 %LOCALAPPDATA%\MiDropHelper\shellExt.log，写失败一律忽略。
        internal static void Trace(string message)
        {
            try
            {
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(localAppData))
                {
                    return;
                }

                var dir = System.IO.Path.Combine(localAppData, "MiDropHelper");
                Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(dir, "shellExt.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {message}\r\n");
            }
            catch
            {
            }
        }

        public static unsafe void StartShare(string target, string[] files)
        {
            // 小米互传优先用 Program Files 下的独立 helper（由用户以管理员身份放置，本进程只读不写）。
            // 每条候选都先做存在性检查，不存在就继续往下走原有候选。华为/荣耀完全不进这条分支。
            if (string.Equals(target, "XiaomiShare", StringComparison.Ordinal))
            {
                foreach (var miHelper in GetProgramFilesHelperCandidates())
                {
                    Trace($"StartShare program files candidate={miHelper} exists={System.IO.File.Exists(miHelper)}");
                    if (LaunchHelper(miHelper, target, files))
                    {
                        return;
                    }
                }
            }

            var installLocation = PackageProperties.Current?.PackageInstallLocation;
            if (!string.IsNullOrEmpty(installLocation))
            {
                var helperExecute = System.IO.Path.Combine(installLocation, "MiDrop.Helper", "MiDrop.Helper.exe");
                if (System.IO.File.Exists(helperExecute))
                {
                    var key = MiDrop.Core.FilesHelper.SaveFilesAsync(files, default).Result;
                    if (!string.IsNullOrEmpty(key))
                    {
                        try
                        {
                            Process.Start(helperExecute, $"--target {target} --share-files {key}");
                            Trace($"StartShare packaged helper ok: {helperExecute} key={key}");
                        }
                        catch (Exception ex)
                        {
                            Trace("StartShare packaged helper threw: " + ex);
                        }
                    }
                    else
                    {
                        Trace("StartShare packaged helper: SaveFilesAsync returned empty key");
                    }
                    return;
                }

                // sparse 包（uap10:AllowExternalContent）安装后 PackageInstallLocation 里只有清单和图标，
                // helper 实际在外部目录。只有小米互传继续往下走候选路径，其他 target 维持原样返回。
                Trace($"StartShare packaged but helper missing: {helperExecute}");
                if (!string.Equals(target, "XiaomiShare", StringComparison.Ordinal))
                {
                    return;
                }
            }

            // 没有 MSIX 包身份（裸 COM 注册，regsvr32 装的那条路）：
            // 只有小米互传降级，华为/荣耀保持原样——没有包身份就什么都不做。
            if (!string.Equals(target, "XiaomiShare", StringComparison.Ordinal))
            {
                return;
            }

            foreach (var helperExecute in GetUnpackagedHelperCandidates())
            {
                Trace($"StartShare candidate={helperExecute} exists={System.IO.File.Exists(helperExecute)}");
                if (LaunchHelper(helperExecute, target, files))
                {
                    return;
                }
            }

            Trace("StartShare: no helper candidate worked");
        }

        // Program Files 下的 helper 候选，按优先级排（目录名来自实测存在的 C:\Program Files\MI）。
        // 程序目录一律取 SpecialFolder.ProgramFiles，不拼字符串。
        private static IEnumerable<string> GetProgramFilesHelperCandidates()
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (string.IsNullOrEmpty(programFiles))
            {
                yield break;
            }

            var mi = System.IO.Path.Combine(programFiles, "MI");

            // 1) MI\MiDrop.Helper\MiDrop.Helper.exe（推荐：专用子目录）
            yield return System.IO.Path.Combine(mi, "MiDrop.Helper", "MiDrop.Helper.exe");

            // 2) MI\MiDrop.Helper.exe（兼容直接扔在 MI 根目录）
            yield return System.IO.Path.Combine(mi, "MiDrop.Helper.exe");

            // 3) 管家自己的父目录
            yield return System.IO.Path.Combine(mi, "XiaomiPCManager", "MiDrop.Helper", "MiDrop.Helper.exe");
        }

        // 裸注册时的 helper 位置，按优先级排。
        private static IEnumerable<string> GetUnpackagedHelperCandidates()
        {
            // 1) 与 DLL 同目录：MiSmartShare\MiDropShellExt.dll + MiSmartShare\MiDrop.Helper\MiDrop.Helper.exe
            //    MiDrop.Helper.exe 是自包含 AOT，不需要目标机装 .NET 运行时。
            yield return System.IO.Path.Combine(DllModule.BaseDirectory, "MiDrop.Helper", "MiDrop.Helper.exe");

            // 2) 用户目录兜底：%LOCALAPPDATA%\MiDropHelper\MiDrop.Helper.exe
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
            {
                yield return System.IO.Path.Combine(localAppData, "MiDropHelper", "MiDrop.Helper.exe");
            }
        }

        private static bool LaunchHelper(string helperExecute, string target, string[] files)
        {
            if (string.IsNullOrEmpty(helperExecute) || !System.IO.File.Exists(helperExecute))
            {
                return false;
            }

            var key = MiDrop.Core.FilesHelper.SaveFilesAsync(files, default).Result;
            if (string.IsNullOrEmpty(key))
            {
                Trace("LaunchHelper: SaveFilesAsync returned empty key");
                return false;
            }

            Trace($"LaunchHelper: key={key} files={files.Length}");

            try
            {
                Process.Start(helperExecute, $"--target {target} --share-files {key}");
                Trace("LaunchHelper: Process.Start ok");
                return true;
            }
            catch (Exception ex)
            {
                Trace("LaunchHelper: Process.Start threw: " + ex);
                return false;
            }
        }
    }
}
