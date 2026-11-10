<#
.SYNOPSIS
    一键卸载「使用小米互传发送」右键扩展。

.DESCRIPTION
    双击 卸载.cmd（推荐；从网上下载的包也能直接跑）。脚本会自己请求管理员权限。

    做的事情：
      1. 前置检查（管理员、找到当前安装）
      2. 注销 sparse 包（新式菜单 / 共享面板 / 磁贴的来源）
      3. 删除 %LOCALAPPDATA%\MiDropHelper
      4. 删除 C:\Program Files\MI\MiDrop.Helper（删除前二次确认）
      最后询问是否重启资源管理器。

    不动小米电脑管家自己的任何文件，也不动它的注册表键。

.PARAMETER NoElevate
    不提权直接跑，仅用于调试。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\卸载.ps1   # 出问题时用这条；平时直接双击 卸载.cmd
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
$PKG_NAME = 'MiDropShellExt.Sparse'

# 从网上下载的 zip 解压后，每个文件都带「来自 Internet」标记（Zone.Identifier）。
# 这里顺手把自己所在目录解锁，免得下次还要手工右键属性「解除锁定」。失败不影响卸载。
try {
    Get-ChildItem -LiteralPath $PSScriptRoot -Recurse -File -ErrorAction SilentlyContinue |
        Unblock-File -ErrorAction SilentlyContinue
} catch { }

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

# ---------------------------------------------------------------- 提权
if (-not (Test-Admin)) {
    if ($NoElevate) {
        Write-Host ''
        Write-Host '未提权（-NoElevate）：只适合调试，删除 Program Files 那一步会失败。' -ForegroundColor Yellow
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
            Read-Host '按回车退出' | Out-Null
            exit 1
        }
        exit 0
    }
}

Write-Host ''
Write-Host '==============================================' -ForegroundColor White
Write-Host ' 小米互传 · 右键扩展卸载' -ForegroundColor White
Write-Host '==============================================' -ForegroundColor White

if (-not [string]::IsNullOrEmpty($ProgramFilesRoot)) {
    $programFiles = (Resolve-Path -LiteralPath $ProgramFilesRoot).Path
} else {
    $programFiles = [Environment]::GetFolderPath('ProgramFiles')
}
$localAppData = [Environment]::GetFolderPath('LocalApplicationData')
$pfDir  = Join-Path $programFiles 'MI\MiDrop.Helper'
$extDir = Join-Path $localAppData 'MiDropHelper'

# ---------------------------------------------------------------- [1/5]
Write-Step 1 '前置检查'

if (Test-Admin) {
    Write-Ok '管理员权限：已具备'
} else {
    Write-Note '管理员权限：未提权（-NoElevate 调试模式，Program Files 那一步会失败）'
}

$pkg = Get-AppxPackage -Name $PKG_NAME
if ($pkg) {
    Write-Ok ('找到已注册的包：' + $pkg.PackageFullName)
} else {
    Write-Note ('没有已注册的 ' + $PKG_NAME + '，跳过第 2 步。')
}

if (Test-Path -LiteralPath $extDir) { Write-Ok ('用户目录：' + $extDir) } else { Write-Note ('用户目录不存在：' + $extDir) }
if (Test-Path -LiteralPath $pfDir)  { Write-Ok ('Program Files 目录：' + $pfDir) } else { Write-Note ('Program Files 目录不存在：' + $pfDir) }

# ---------------------------------------------------------------- [2/5]
Write-Step 2 '注销 sparse 包'

if ($pkg) {
    try {
        Remove-AppxPackage -Package $pkg.PackageFullName
        if (Get-AppxPackage -Name $PKG_NAME) {
            Write-Bad '注销命令执行了，但包还在。可能需要重启后重试。'
        } else {
            Write-Ok '已注销'
        }
    } catch {
        Write-Bad ('注销失败：' + $_.Exception.Message)
        Write-Note '可以先把资源管理器关掉再重试一次： Stop-Process -Name explorer -Force'
    }
} else {
    Write-Note '跳过'
}

# ---------------------------------------------------------------- [3/5]
Write-Step 3 ('删除 ' + $extDir)

if (Test-Path -LiteralPath $extDir) {
    try {
        Remove-Item -LiteralPath $extDir -Recurse -Force
        Write-Ok '已删除'
    } catch {
        Write-Bad ('删除失败：' + $_.Exception.Message)
        Write-Note '可能有进程正占用 DLL。重启后再删。'
    }
} else {
    Write-Note '跳过（不存在）'
}

# ---------------------------------------------------------------- [4/5]
Write-Step 4 ('删除 ' + $pfDir + '（删除前需要二次确认）')

if (Test-Path -LiteralPath $pfDir) {
    Write-Note '这个目录里只有 helper 一个文件，删掉不影响小米电脑管家本身。'
    $confirm = Read-Host ('      确认删除 ' + $pfDir + ' 吗？输入大写 YES 继续，直接回车跳过')
    if ($confirm -ceq 'YES') {
        try {
            Remove-Item -LiteralPath $pfDir -Recurse -Force
            Write-Ok '已删除'
        } catch {
            Write-Bad ('删除失败：' + $_.Exception.Message)
            Write-Note '可能正被占用（刚点过菜单会短暂锁住），稍后重试或重启后再删。'
        }
    } else {
        Write-Note '已跳过，目录保留'
    }
} else {
    Write-Note '跳过（不存在）'
}

# ---------------------------------------------------------------- [5/5]
Write-Step 5 '重启资源管理器'

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
Write-Host ' 卸载完成' -ForegroundColor Green
Write-Host '==============================================' -ForegroundColor Green
Write-Host ''
Write-Host ' 现在文件右键里不再有我们加的那一条。'
Write-Host ' 小米自己注册的「使用小米互传发送」仍在（那是管家自带的，没有动它）。'
Write-Host ''
Read-Host '按回车关闭' | Out-Null
exit 0
