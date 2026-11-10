# Sparse 包（让右键项进入 Win11 新式菜单）

这个目录不含任何二进制，只有清单、图标和两个脚本。作用是把已经在用的
`MiDropShellExt.dll` 登记成 **sparse 包**，从而出现在 **Win11 新式右键菜单**里，
而不是只能藏在「显示更多选项」的老菜单里。

## 为什么需要它

Win11 新式菜单只认「包身份」注册的上下文菜单处理器：

```
HKLM\SOFTWARE\Classes\PackagedCom\ClassIndex\{CLSID}\<PackageFullName>
```

本机实测：这个 CLSID 在 `PackagedCom` 下根本不存在 → 只能进经典菜单。

```
HKLM\SOFTWARE\Classes\*\shell\MiDropExtMenu
  ExplorerCommandHandler = {504d69c0-cb52-48df-b5b5-7161829fabc8}
HKLM\SOFTWARE\Classes\CLSID\{504d69c0-…}\InprocServer32
  (Default) = C:\Program Files\MI\XiaomiPCManager\6.0.2.30\MiSmartShare\MiDropShellExt.dll
```

同机对照组（百度网盘）就是同一套做法：它的 DLL 是裸文件放在
`%APPDATA%\baidu\BaiduNetdisk\sysres\YunShellCommand64.dll`，
manifest 里 `uap10:AllowExternalContent=true` 的 sparse 包，新式菜单里显示正常。

**所以不需要打完整 MSIX，也不需要证书。**

## 清单里用到的 CLSID

| CLSID | 来源 | 说明 |
|---|---|---|
| `976D43D8-907F-46AF-B47F-07084C71A2F0` | `DllMain.cs` 的 `PackagedClsid1` | 打包形态用的 CLSID，DLL 无需改动 |

`{504d69c0-…}` 是系统里那支官方 DLL 自己注册的裸 COM CLSID，不参与 sparse 包。

## 目录约定

包内（本目录，`-Register` 指向这里）：

```
AppxManifest.xml
XiaomiImages\*.png      清单 Logo 引用的图片，必须是包内文件
Register.ps1
Unregister.ps1
```

包外（默认 `-ExternalLocation`，只读，不复制）：

```
%LOCALAPPDATA%\MiDropHelper\
  MiDropShellExt.dll    ShellExt 的 NativeAOT 产物
  MiDrop.Helper.exe     MiDrop.Helper 的自包含 AOT 产物
  ShareFiles\           每次发送生成的清单缓存
```

## 用法

先确认两个产物在位，然后干跑一遍：

```powershell
powershell -ExecutionPolicy Bypass -File src\SparsePackage\Register.ps1 -DryRun
```

确认无误再注册（不需要管理员，开发者模式开着就行）：

```powershell
powershell -ExecutionPolicy Bypass -File src\SparsePackage\Register.ps1
```

注册完成后**重启 explorer** 才加载新菜单：

```powershell
Stop-Process -Name explorer -Force
```

回滚：

```powershell
powershell -ExecutionPolicy Bypass -File src\SparsePackage\Unregister.ps1
```

`Unregister.ps1` 只移除包注册，不删文件，也不动 HKCU 里的隔离覆盖键。

## 已知前提

- **开发者模式**：`HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock`
  的 `AllowDevelopmentWithoutDevLicense=1`。本机已开启。
- **Identity 是自定的**：`MiDropShellExt.Sparse` / `CN=MiDropShellExt-Dev`，
  与参考包（`5f71dad9-…` / `CN=cnbluefire`）不同，避免和已装的正版包冲突。
- **必须是 x64**：两个产物都是 x64。

## 与官方 MSIX 包的差别

| 项 | 官方 MSIX | 这里的 sparse 包 |
|---|---|---|
| 需要安装证书 | 是 | 否（loose 注册） |
| 需要管理员 | 是 | 否 |
| 二进制进包 | 是 | 否，走 `-ExternalLocation` |
| 可回滚 | 卸载重装 | 删包注册即可，文件不动 |
| 开始菜单磁贴 | 有 | 有（`XiaomiApp`） |
| 系统「共享」面板 | 有 | 有（`windows.shareTarget`，需实测） |

## 没验证的部分

- 这份清单只做过 XML 合法性和字段检查，**没有真正 `Add-AppxPackage -Register` 过**。
- `shareTarget` 分支能不能真的弹出来，要等注册后实测。
- 新式菜单里显示的中文标题走 `IExplorerCommand::GetTitle`，与经典菜单共用一个实现，
  预期一致，同样未实测。
