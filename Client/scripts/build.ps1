param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\dist'))

# Builds NEXA.exe with the C# compiler that ships with .NET Framework 4.x on Windows.
# No SDK, NuGet packages or downloads are needed. Language level is C# 5.
$ErrorActionPreference = 'Stop'
$client = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$compiler = Join-Path ([Environment]::GetFolderPath('Windows')) 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output = (New-Item -ItemType Directory -Path $OutputDirectory -Force).FullName
$sources = Get-ChildItem -LiteralPath (Join-Path $client 'src') -Filter '*.cs' -File -Recurse

$wpf = Join-Path ([Environment]::GetFolderPath('Windows')) 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$references = @(
    'System.dll', 'System.Core.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Net.Http.dll',
    'System.Runtime.Serialization.dll', 'System.Security.dll', 'System.Xml.dll', 'System.Xaml.dll'
) | ForEach-Object { "/reference:$_" }
$references += @('PresentationFramework.dll', 'PresentationCore.dll', 'WindowsBase.dll') | ForEach-Object { "/reference:$(Join-Path $wpf $_)" }

$resources = @()
foreach ($name in 'Vazirmatn-Regular.ttf', 'Vazirmatn-Bold.ttf', 'OFL.txt') {
    $path = Join-Path $client "assets\fonts\$name"
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing embedded resource: $path" }
    $resources += "/resource:$path,Nexa.Fonts.$name"
}
foreach ($xaml in Get-ChildItem -LiteralPath (Join-Path $client 'src\Ui\Themes') -Filter '*.xaml' -File) {
    $resources += "/resource:$($xaml.FullName),Nexa.Xaml.Themes.$($xaml.BaseName).xaml"
}

$compilerArgs = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/codepage:65001', '/warnaserror+',
    "/win32icon:$client\assets\Nexa.ico", "/out:$output\NEXA.exe") + $references + $resources
& $compiler @compilerArgs @($sources.FullName)
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }

Copy-Item -LiteralPath (Join-Path $client 'config\NEXA.exe.config') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $client 'assets\fonts\OFL.txt') -Destination (Join-Path $output 'Vazirmatn-OFL.txt') -Force
Get-Item -LiteralPath (Join-Path $output 'NEXA.exe')
