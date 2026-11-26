param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'))

$ErrorActionPreference = 'Stop'
$compiler = Join-Path ([Environment]::GetFolderPath('Windows')) 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = (New-Item -ItemType Directory -Path $OutputDirectory -Force).FullName
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File
$references = @('System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll',
    'System.Net.Http.dll', 'System.Runtime.Serialization.dll', 'System.Security.dll', 'System.Xml.dll')
$fontDirectory = Join-Path $PSScriptRoot 'assets\fonts'
$fontResources = @('Vazirmatn-Regular.ttf', 'Vazirmatn-Bold.ttf', 'OFL.txt') | ForEach-Object {
    $path = Join-Path $fontDirectory $_
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing embedded resource: $path" }
    "/resource:$path,Nexa.Fonts.$_"
}
$chatLicense = Join-Path $PSScriptRoot 'assets\winforms-chat-MIT.txt'
$compilerArgs = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/codepage:65001',
    '/warnaserror+', "/win32icon:$PSScriptRoot\assets\Nexa.ico", "/out:$output\NEXA.exe",
    "/resource:$chatLicense,Nexa.Notices.winforms-chat-MIT.txt") +
    @($references | ForEach-Object { "/reference:$_" }) + $fontResources
& $compiler @compilerArgs @($sources.FullName)
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'NEXA.exe.config') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $fontDirectory 'OFL.txt') -Destination (Join-Path $output 'Vazirmatn-OFL.txt') -Force
Copy-Item -LiteralPath $chatLicense -Destination $output -Force
Get-Item -LiteralPath (Join-Path $output 'NEXA.exe')
