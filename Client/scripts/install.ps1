param(
    [switch]$StartWithWindows,
    [string]$PackageDirectory = (Join-Path $PSScriptRoot '..\dist')
)

$ErrorActionPreference = 'Stop'
$package = $PackageDirectory
$destination = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'NEXA\App'
$exe = Join-Path $destination 'NEXA.exe'
$config = Join-Path $destination 'NEXA.exe.config'

foreach ($file in @('NEXA.exe', 'NEXA.exe.config')) {
    if (!(Test-Path -LiteralPath (Join-Path $package $file) -PathType Leaf)) {
        throw 'Run scripts\build.ps1 before installing NEXA.'
    }
}

New-Item -ItemType Directory -Path $destination -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $package 'NEXA.exe') -Destination $exe -Force
Copy-Item -LiteralPath (Join-Path $package 'NEXA.exe.config') -Destination $config -Force
foreach ($notice in @('Vazirmatn-OFL.txt')) {
    $source = Join-Path $package $notice
    if (Test-Path -LiteralPath $source -PathType Leaf) {
        Copy-Item -LiteralPath $source -Destination $destination -Force
    }
}

$programs = [Environment]::GetFolderPath('Programs')
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path $programs 'NEXA.lnk'))
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $destination
$shortcut.Save()

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$existing = Get-ItemProperty -LiteralPath $runKey -Name NEXA -ErrorAction SilentlyContinue
if ($StartWithWindows -or $existing) {
    if (!(Test-Path -LiteralPath $runKey)) {
        New-Item -Path $runKey | Out-Null
    }
    New-ItemProperty -LiteralPath $runKey -Name NEXA -Value ('"{0}" --tray' -f $exe) -PropertyType String -Force | Out-Null
}
Get-Item -LiteralPath $exe
