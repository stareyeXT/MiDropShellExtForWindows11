<#
.SYNOPSIS
    一键安装「使用小米互传发送」右键扩展（适配小米电脑管家 6.0.2.30）。

.DESCRIPTION
    双击、或右键「使用 PowerShell 运行」即可。脚本会自己请求管理员权限
    （会弹 UAC，点「是」），不需要你手工开管理员终端。

    做四件事：
      1. 前置检查：管理员权限、开发者模式、安装文件是否齐全
      2. helper 放到 C:\Program Files\MI\MiDrop.Helper\（只有这一步需要管理员）
      3. DLL 和 helper 副本放到 %LOCALAPPDATA%\MiDropHelper
      4. 注册 sparse 包（新式菜单、共享面板、开始菜单磁贴都来自这个包）
      最后询问是否重启资源管理器。

    不写任何小米自己的注册表键，也不覆盖小米电脑管家目录里的任何文件。

.PARAMETER NoElevate
    不提权直接跑，仅用于调试。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File 安装.ps1
#>
[CmdletBinding()]
param(
    [switch]$NoElevate,
    [string]$ProgramFilesRoot = ''
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }
try { $OutputEncoding = [Text.Encoding]::UTF8 } catch { }

$TOTAL = 5

function Write-Step {
    param([int]$n, [string]$text)
    Write-Host ''
    Write-Host ("[{0}/{1}] {2}" -f $n, $TOTAL, $text) -ForegroundColor Cyan
}

function Write-Ok {
    param([string]$text)
    Write-Host ("      OK  " + $text) -ForegroundColor Green
}

function Write-Note {
    param([string]$text)
    Write-Host ("      --  " + $text) -ForegroundColor DarkGray
}

function Write-Bad {
    param([string]$text)
    Write-Host ("      !!  " + $text) -ForegroundColor Red
}

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $pr = New-Object Security.Principal.WindowsPrincipal($id)
    return $pr.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-ErrorHint {
    param([string]$message)
    $m = $message
    if ($m -match '0x800B0109') {
        return '0x800B0109：包没有可信签名。本包走开发者模式注册，请在「设置 → 系统 → 开发者选项」里打开开发者模式后重试。'
    }
    if ($m -match '0x80073CF9') {
        return '0x80073CF9：同版本包已经装过了。先运行同目录的 卸载.ps1，再重新安装。'
    }
    if ($m -match '0x80073CF6') {
        return '0x80073CF6：包无法注册。通常是开发者模式没开，或另一个会话正在占用这个包。'
    }
    if ($m -match '0x80073CF3') {
        return '0x80073CF3：包更新/依赖校验失败。先卸载旧包，删掉 %LOCALAPPDATA%\MiDropHelper 后重装。'
    }
    if ($m -match '0x80073D54') {
        return '0x80073D54：该进程没有包身份。它不影响右键发送，可以忽略。'
    }
    if ($m -match 'SplashScreen|0x80070003') {
        return '0x80070003：清单引用的图片不在外部目录里。本脚本第 3 步会把 XiaomiImages 复制到 %LOCALAPPDATA%\MiDropHelper，若手工安装请自己补这一步。'
    }
    if ($m -match 'Access is denied|拒绝访问|UnauthorizedAccess') {
        return '拒绝访问：请确认是在管理员窗口里运行本脚本。'
    }
    return $null
}

function Resolve-Payload {
    param([string[]]$candidates, [string]$label)
    foreach ($c in $candidates) {
        if (Test-Path -LiteralPath $c) { return (Resolve-Path -LiteralPath $c).Path }
    }
    throw ("找不到" + $label + "。已查找：" + ($candidates -join ' ; '))
}

# ---------------------------------------------------------------- 提权
if (-not (Test-Admin)) {
    if ($NoElevate) {
        Write-Host ''
        Write-Host '未提权（-NoElevate）：只适合调试，写入 Program Files 那一步会失败。' -ForegroundColor Yellow
    } else {
        Write-Host ''
        Write-Host '需要管理员权限：正在请求提权，请在弹窗里点「是」。' -ForegroundColor Yellow
        try {
            Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList @(
                '-NoProfile', '-NoExit', '-ExecutionPolicy', 'Bypass',
                '-File', ('"' + $PSCommandPath + '"')
            ) | Out-Null
        } catch {
            Write-Bad ('提权被拒绝或失败：' + $_.Exception.Message)
            Write-Note '可以自己开一个「终端(管理员)」，再运行本脚本。'
            Read-Host '按回车退出' | Out-Null
            exit 1
        }
        exit 0
    }
}

Write-Host ''
Write-Host '==============================================' -ForegroundColor White
Write-Host ' 小米互传 · 右键扩展安装（管家 6.0.2.30 适配）' -ForegroundColor White
Write-Host '==============================================' -ForegroundColor White

# 路径：程序目录和用户目录都用环境变量取，不写死盘符
if (-not [string]::IsNullOrEmpty($ProgramFilesRoot)) {
    $programFiles = (Resolve-Path -LiteralPath $ProgramFilesRoot).Path
} else {
    $programFiles = [Environment]::GetFolderPath('ProgramFiles')
}
$localAppData = [Environment]::GetFolderPath('LocalApplicationData')
if ([string]::IsNullOrEmpty($programFiles) -or [string]::IsNullOrEmpty($localAppData)) {
    Write-Bad '取不到 ProgramFiles / LocalAppData 路径。'
    Read-Host '按回车退出' | Out-Null
    exit 1
}

$pfDir  = Join-Path $programFiles 'MI\MiDrop.Helper'
$extDir = Join-Path $localAppData 'MiDropHelper'

# ---------------------------------------------------------------- [1/5]
Write-Step 1 '前置检查'

if (Test-Admin) {
    Write-Ok '管理员权限：已具备'
} else {
    Write-Note '管理员权限：未提权（-NoElevate 调试模式，Program Files 那一步改用 -ProgramFilesRoot）'
}

$devModeOn = $false
try {
    $unlock = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction Stop
    if ($unlock.AllowDevelopmentWithoutDevLicense -eq 1) { $devModeOn = $true }
} catch { }
if ($devModeOn) {
    Write-Ok '开发者模式：已开启'
} else {
    Write-Note '开发者模式：未检测到已开启'
    Write-Note '若下一步注册失败并报 0x800B0109，请到「设置 → 系统 → 开发者选项」打开开发者模式。'
}

$root = $PSScriptRoot

$dllPath = Resolve-Payload -label ' MiDropShellExt.dll' -candidates @(
    (Join-Path $root 'bin\MiDropShellExt.dll'),
    (Join-Path $root '..\src\ShellExt\bin\x64\Release\net9.0-windows\win-x64\publish\ShellExt.dll')
)
$helperPath = Resolve-Payload -label ' MiDrop.Helper.exe' -candidates @(
    (Join-Path $root 'bin\MiDrop.Helper.exe'),
    (Join-Path $root '..\src\MiDrop.Helper\bin\x64\Release\net9.0-windows10.0.22000.0\win-x64\publish\MiDrop.Helper.exe')
)
$pkgDir = Resolve-Payload -label ' SparsePackage 目录' -candidates @(
    (Join-Path $root 'SparsePackage'),
    (Join-Path $root '..\src\SparsePackage')
)

$dllItem = Get-Item -LiteralPath $dllPath
$helperItem = Get-Item -LiteralPath $helperPath
Write-Ok ('DLL    ' + $dllItem.Name + '  ' + ('{0:N0}' -f $dllItem.Length) + ' B')
Write-Ok ('helper ' + $helperItem.Name + '  ' + ('{0:N0}' -f $helperItem.Length) + ' B')
Write-Ok ('包目录 ' + $pkgDir)

$registerPs1 = Join-Path $pkgDir 'Register.ps1'
if (-not (Test-Path -LiteralPath $registerPs1)) {
    Write-Bad ('缺少注册脚本：' + $registerPs1)
    Read-Host '按回车退出' | Out-Null
    exit 1
}

# ---------------------------------------------------------------- [2/5]
Write-Step 2 ('helper 安装到 ' + $pfDir + '（需要管理员，仅此一步）')

$canWritePf = (Test-Admin) -or (-not [string]::IsNullOrEmpty($ProgramFilesRoot))
if (-not $canWritePf) {
    Write-Note '当前未提权，跳过这一步。'
    Write-Note '正式运行时脚本会先弹 UAC，提权成功后自动完成这一步。'
} else {
try {
    if (-not (Test-Path -LiteralPath $pfDir)) {
        New-Item -ItemType Directory -Force -Path $pfDir | Out-Null
        Write-Ok ('新建目录 ' + $pfDir)
    } else {
        Write-Ok ('目录已存在 ' + $pfDir)
    }

    $pfHelper = Join-Path $pfDir 'MiDrop.Helper.exe'
    Copy-Item -LiteralPath $helperPath -Destination $pfHelper -Force

    $srcHash = (Get-FileHash -LiteralPath $helperPath -Algorithm SHA256).Hash
    $dstHash = (Get-FileHash -LiteralPath $pfHelper -Algorithm SHA256).Hash
    if ($srcHash -ne $dstHash) {
        throw ('复制后哈希不一致（源 ' + $srcHash + '，目标 ' + $dstHash + '）')
    }
    $pfSize = (Get-Item -LiteralPath $pfHelper).Length
    Write-Ok ('已写入 ' + $pfHelper + '  ' + ('{0:N0}' -f $pfSize) + ' B')
    Write-Ok ('SHA256 ' + $dstHash)
} catch {
    Write-Bad ('写入 Program Files 失败：' + $_.Exception.Message)
    $hint = Get-ErrorHint -message ($_.Exception.Message)
    if ($hint) { Write-Bad $hint }
    Write-Note '安装已中止。这一步是脚本请求提权的原因，请确认 UAC 点了「是」。'
    Read-Host '按回车退出' | Out-Null
    exit 1
}
}

# ---------------------------------------------------------------- [3/5]
Write-Step 3 ('DLL 安装到 ' + $extDir)

try {
    if (-not (Test-Path -LiteralPath $extDir)) {
        New-Item -ItemType Directory -Force -Path $extDir | Out-Null
        Write-Ok ('新建目录 ' + $extDir)
    } else {
        Write-Ok ('目录已存在 ' + $extDir)
    }

    Copy-Item -LiteralPath $dllPath -Destination (Join-Path $extDir 'MiDropShellExt.dll') -Force
    Write-Ok ('MiDropShellExt.dll  ' + ('{0:N0}' -f $dllItem.Length) + ' B')

    # 包清单声明的 ExternalLocation 校验要求这两个文件都在，所以这里也放一份 helper
    Copy-Item -LiteralPath $helperPath -Destination (Join-Path $extDir 'MiDrop.Helper.exe') -Force
    Write-Ok ('MiDrop.Helper.exe    ' + ('{0:N0}' -f $helperItem.Length) + ' B')
    Write-Note '运行时优先使用 Program Files 那一份 helper，这份是清单校验用的副本。'

    # -ExternalLocation 就是包的 root folder，清单里引用的图片必须也在这里，
    # 否则注册会报 0x80073CF6 + 0x80070003「找不到初始屏幕图像 SplashScreen.png」（实测）。
    $srcImages = Join-Path $pkgDir 'XiaomiImages'
    if (-not (Test-Path -LiteralPath $srcImages)) {
        throw ('包里缺 XiaomiImages 目录：' + $srcImages)
    }
    $dstImages = Join-Path $extDir 'XiaomiImages'
    if (-not (Test-Path -LiteralPath $dstImages)) {
        New-Item -ItemType Directory -Force -Path $dstImages | Out-Null
    }
    Copy-Item -Path (Join-Path $srcImages '*') -Destination $dstImages -Force -Recurse
    $imgCount = (Get-ChildItem -LiteralPath $dstImages -File).Count
    Write-Ok ('XiaomiImages         ' + $imgCount + ' 个图片（清单引用，必须放这里）')

    # 清单本身也放一份：-ExternalLocation 就是包根目录，保持一致更直观
    Copy-Item -LiteralPath (Join-Path $pkgDir 'AppxManifest.xml') -Destination (Join-Path $extDir 'AppxManifest.xml') -Force
    Write-Ok 'AppxManifest.xml     1 份（外部目录副本）'
} catch {
    Write-Bad ('写入 ' + $extDir + ' 失败：' + $_.Exception.Message)
    Read-Host '按回车退出' | Out-Null
    exit 1
}

# ---------------------------------------------------------------- [4/5]
Write-Step 4 '注册 sparse 包（新式菜单 / 共享面板 / 开始菜单磁贴）'

try {
    & $registerPs1 -ExternalLocation $extDir
    if (-not $?) { throw 'Register.ps1 返回失败' }

    $pkg = Get-AppxPackage -Name 'MiDropShellExt.Sparse'
    if (-not $pkg) {
        throw '注册命令执行完了，但 Get-AppxPackage 查不到 MiDropShellExt.Sparse'
    }
    Write-Ok ('已注册 ' + $pkg.PackageFullName)
    Write-Ok ('安装位置 ' + $pkg.InstallLocation)
} catch {
    $msg = $_.Exception.Message
    Write-Bad ('注册失败：' + $msg)
    $hint = Get-ErrorHint -message $msg
    if ($hint) { Write-Bad $hint }
    Write-Note '前面的文件已经就位；修好上面的问题后，可以重复运行本脚本。'
    Read-Host '按回车退出' | Out-Null
    exit 1
}

# ---------------------------------------------------------------- [5/5]
Write-Step 5 '重启资源管理器让菜单生效'

Write-Note '重启后任务栏会闪一下，已打开的文件夹窗口会关闭。'
$answer = Read-Host '      现在重启资源管理器吗？[Y/n]'
if ([string]::IsNullOrWhiteSpace($answer) -or $answer -match '^[Yy]') {
    Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 1500
    if (-not (Get-Process -Name explorer -ErrorAction SilentlyContinue)) {
        Start-Process explorer.exe
        Start-Sleep -Milliseconds 1500
    }
    Write-Ok '资源管理器已重启'
} else {
    Write-Note '跳过。需要时自己执行： Stop-Process -Name explorer -Force'
}

Write-Host ''
Write-Host '==============================================' -ForegroundColor Green
Write-Host ' 安装完成' -ForegroundColor Green
Write-Host '==============================================' -ForegroundColor Green
Write-Host ''
Write-Host ' 怎么用：'
Write-Host '   新式菜单  → 右键文件 →「应用扩展」→「使用小米互传发送」'
Write-Host '   经典菜单  → 右键文件 →「显示更多选项」→「使用小米互传发送」（会有 2 条，正常）'
Write-Host ''
Write-Host ' 日志：'
Write-Host ('   ' + (Join-Path $extDir 'shellExt.log'))
Write-Host ''
Write-Host ' 卸载：运行同目录的 卸载.ps1'
Write-Host ''
Read-Host '按回车关闭' | Out-Null
exit 0
