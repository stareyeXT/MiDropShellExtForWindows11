<#
.SYNOPSIS
    注销 sparse 包，回到「裸 COM / HKCU 覆盖」的状态。

.DESCRIPTION
    只移除 sparse 包注册，不删除任何文件，也不动注册表里的 HKCU 覆盖键。
    如果之前为了隔离验证写过
    HKCU\Software\Classes\CLSID\{504d69c0-...}\InprocServer32，
    那份配置仍然有效，右键照旧可用。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Unregister.ps1
#>
[CmdletBinding()]
param(
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$name = 'MiDropShellExt.Sparse'
$pkg = Get-AppxPackage -Name $name

if (-not $pkg) {
    Write-Host "没有已注册的 $name，无需注销。"
    return
}

Write-Host ("找到：{0}" -f $pkg.PackageFullName)
Write-Host ("位置：{0}" -f $pkg.InstallLocation)

if ($DryRun) {
    Write-Host ''
    Write-Host "[DryRun] 将要执行： Remove-AppxPackage -Package $($pkg.PackageFullName)"
    return
}

Remove-AppxPackage -Package $pkg.PackageFullName

if (Get-AppxPackage -Name $name) {
    Write-Warning '注销后仍能查到该包，可能需要重启 explorer。'
} else {
    Write-Host '已注销。'
}

Write-Host ''
Write-Host '当前 PackagedCom 索引：'
foreach ($c in @('976D43D8-907F-46AF-B47F-07084C71A2F0', '504d69c0-cb52-48df-b5b5-7161829fabc8')) {
    $key = "HKLM:\SOFTWARE\Classes\PackagedCom\ClassIndex\{$c}"
    if (Test-Path -LiteralPath $key) {
        Write-Host ("  {0} -> 仍在" -f $c)
    } else {
        Write-Host ("  {0} -> 已移除" -f $c)
    }
}
