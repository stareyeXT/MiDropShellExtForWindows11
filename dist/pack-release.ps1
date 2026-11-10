$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$repo = 'C:\Users\ME\.dsh\bot\workspace\MiDropShellExtForWindows11'
$outDir = 'C:\Users\ME\.dsh\bot\workspace\midrop-outgoing'
$zipName = 'MiDropShellExt-v1.0.12.0-pcmanager6.0.2.30-win-x64.zip'
$zipPath = Join-Path $outDir $zipName

$dll = Join-Path $repo 'src\ShellExt\bin\x64\Release\net9.0-windows\win-x64\publish\ShellExt.dll'
$helper = Join-Path $repo 'src\MiDrop.Helper\bin\x64\Release\net9.0-windows10.0.22000.0\win-x64\publish\MiDrop.Helper.exe'
$pkg = Join-Path $repo 'src\SparsePackage'

$stage = Join-Path $env:TEMP 'midrop-pkg'
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'bin') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'SparsePackage') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'docs') | Out-Null

Copy-Item -LiteralPath $dll -Destination (Join-Path $stage 'bin\MiDropShellExt.dll') -Force
Copy-Item -LiteralPath $helper -Destination (Join-Path $stage 'bin\MiDrop.Helper.exe') -Force
Copy-Item -LiteralPath (Join-Path $pkg 'AppxManifest.xml') -Destination (Join-Path $stage 'SparsePackage\AppxManifest.xml') -Force
Copy-Item -LiteralPath (Join-Path $pkg 'Register.ps1') -Destination (Join-Path $stage 'SparsePackage\Register.ps1') -Force
Copy-Item -LiteralPath (Join-Path $pkg 'Unregister.ps1') -Destination (Join-Path $stage 'SparsePackage\Unregister.ps1') -Force
New-Item -ItemType Directory -Force -Path (Join-Path $stage 'SparsePackage\XiaomiImages') | Out-Null
Copy-Item -Path (Join-Path $pkg 'XiaomiImages\*') -Destination (Join-Path $stage 'SparsePackage\XiaomiImages') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repo 'dist\安装.ps1') -Destination (Join-Path $stage '安装.ps1') -Force
Copy-Item -LiteralPath (Join-Path $repo 'dist\卸载.ps1') -Destination (Join-Path $stage '卸载.ps1') -Force
Copy-Item -LiteralPath (Join-Path $repo 'dist\安装.cmd') -Destination (Join-Path $stage '安装.cmd') -Force
Copy-Item -LiteralPath (Join-Path $repo 'dist\卸载.cmd') -Destination (Join-Path $stage '卸载.cmd') -Force

foreach ($d in @('安装说明.md', '功能对照表.md', '回滚说明.md')) {
    $src = Join-Path $repo ('docs\' + $d)
    if (Test-Path -LiteralPath $src) { Copy-Item -LiteralPath $src -Destination (Join-Path $stage ('docs\' + $d)) -Force }
}

Copy-Item -LiteralPath (Join-Path $repo 'README.txt') -Destination (Join-Path $stage 'README.txt') -Force

# SHA256SUMS
$lines = New-Object System.Collections.Generic.List[string]
$files = Get-ChildItem -LiteralPath $stage -Recurse -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | Sort-Object FullName
foreach ($f in $files) {
    $rel = $f.FullName.Substring($stage.Length + 1) -replace '\\', '/'
    $h = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $lines.Add(('{0}  {1}' -f $h, $rel))
}
[IO.File]::WriteAllLines((Join-Path $stage 'SHA256SUMS.txt'), $lines, (New-Object Text.UTF8Encoding($false)))

# ZIP（正斜杠路径 + UTF-8 标志）
if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$fs = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew)
try {
    $zip = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create, $true)
    try {
        foreach ($f in (Get-ChildItem -LiteralPath $stage -Recurse -File | Sort-Object FullName)) {
            $rel = $f.FullName.Substring($stage.Length + 1) -replace '\\', '/'
            $entry = $zip.CreateEntry($rel, [System.IO.Compression.CompressionLevel]::Optimal)
            $es = $entry.Open()
            try {
                $input = [IO.File]::OpenRead($f.FullName)
                try { $input.CopyTo($es) } finally { $input.Dispose() }
            } finally { $es.Dispose() }
        }
    } finally { $zip.Dispose() }
} finally { $fs.Dispose() }

$zi = Get-Item -LiteralPath $zipPath
$zh = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
Remove-Item -LiteralPath $stage -Recurse -Force

Write-Host ('zip      : ' + $zi.FullName)
Write-Host ('size     : {0:N0} B' -f $zi.Length)
Write-Host ('sha256   : ' + $zh)
$zr = [IO.Compression.ZipFile]::OpenRead($zipPath); try { $n = $zr.Entries.Count } finally { $zr.Dispose() }; Write-Host ('entries  : ' + $n)
