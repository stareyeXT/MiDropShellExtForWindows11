<#
.SYNOPSIS
    注册 MiDrop 右键扩展的 sparse 包（不需要证书、不需要管理员、开发者模式即可）。

.DESCRIPTION
    Win11 新式右键菜单只认 PackagedCom 里登记的处理器。这个脚本把
    AppxManifest.xml 以 loose 方式注册成 sparse 包，清单里的 COM 服务器、
    fileExplorerContextMenus、shareTarget 才会进入系统索引。

    -ExternalLocation 指向放着两个 AOT 产物（MiDropShellExt.dll /
    MiDrop.Helper.exe）的目录，脚本只读它，不复制、不修改。

.PARAMETER ExternalLocation
    DLL 和 helper 所在目录，默认 %LOCALAPPDATA%\MiDropHelper

.PARAMETER DryRun
    只做检查并打印将要执行的动作，不真正注册

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Register.ps1 -DryRun
    powershell -ExecutionPolicy Bypass -File Register.ps1
#>
[CmdletBinding()]
param(
    [string]$ExternalLocation = (Join-Path $env:LOCALAPPDATA 'MiDropHelper'),
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$pkgDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$manifest = Join-Path $pkgDir 'AppxManifest.xml'

if (-not (Test-Path -LiteralPath $manifest)) {
    throw "找不到清单：$manifest"
}
if (-not (Test-Path -LiteralPath $ExternalLocation)) {
    throw "外部目录不存在：$ExternalLocation"
}

$dll    = Join-Path $ExternalLocation 'MiDropShellExt.dll'
$helper = Join-Path $ExternalLocation 'MiDrop.Helper.exe'

foreach ($f in @($dll, $helper)) {
    if (-not (Test-Path -LiteralPath $f)) {
        throw "缺少文件：$f"
    }
    $i = Get-Item -LiteralPath $f
    Write-Host ("  OK  {0}  {1:N0} B" -f $i.Name, $i.Length)
}

# 清单里 Logo 引用的图片是包内文件，必须在包目录里
foreach ($p in @(
        'XiaomiImages\StoreLogo.png',
        'XiaomiImages\Square150x150Logo.png',
        'XiaomiImages\Square44x44Logo.png',
        'XiaomiImages\Wide310x150Logo.png',
        'XiaomiImages\SplashScreen.png')) {
    $full = Join-Path $pkgDir $p
    if (-not (Test-Path -LiteralPath $full)) {
        throw "包内缺少图片：$full（从 src\MiDropShellExt.Package\XiaomiImages 复制过来）"
    }
}

$devMode = $null
try {
    $devMode = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction Stop
} catch { }
if ($devMode) {
    Write-Host ("  开发者模式 AllowDevelopmentWithoutDevLicense = {0}" -f $devMode.AllowDevelopmentWithoutDevLicense)
} else {
    Write-Warning '未读到 AppModelUnlock 键；如果注册失败，先开「开发者模式」。'
}

Write-Host ''
Write-Host "包目录   : $pkgDir"
Write-Host "清单     : $manifest"
Write-Host "外部目录 : $ExternalLocation"

if ($DryRun) {
    Write-Host ''
    Write-Host '[DryRun] 将要执行：'
    Write-Host "  Add-AppxPackage -Register `"$manifest`" -ExternalLocation `"$ExternalLocation`""
    return
}

Add-AppxPackage -Register $manifest -ExternalLocation $ExternalLocation

$pkg = Get-AppxPackage -Name 'MiDropShellExt.Sparse'
if ($pkg) {
    Write-Host ''
    Write-Host ("已注册：{0}" -f $pkg.PackageFullName)
    Write-Host ("安装位置：{0}" -f $pkg.InstallLocation)
} else {
    Write-Warning '注册命令返回了，但 Get-AppxPackage 查不到包。'
}

Write-Host ''
Write-Host '检查 PackagedCom 索引（新式菜单的关键）：'
$clsids = @('976D43D8-907F-46AF-B47F-07084C71A2F0', '504d69c0-cb52-48df-b5b5-7161829fabc8')
foreach ($c in $clsids) {
    $key = "HKLM:\SOFTWARE\Classes\PackagedCom\ClassIndex\{$c}"
    if (Test-Path -LiteralPath $key) {
        $names = (Get-ChildItem -LiteralPath $key | Select-Object -ExpandProperty PSChildName) -join ', '
        Write-Host ("  {0} -> {1}" -f $c, $names)
    } else {
        Write-Host ("  {0} -> 不在 ClassIndex 里" -f $c)
    }
}

Write-Host ''
Write-Host '重启 explorer 后右键生效：'
Write-Host '  Stop-Process -Name explorer -Force'
