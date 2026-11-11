param([switch]$Verify)

$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework C# compiler was not found.' }

$output = New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot 'artifacts')
$references = @('WPF\PresentationFramework.dll', 'WPF\PresentationCore.dll', 'WPF\WindowsBase.dll',
    'System.Xaml.dll', 'System.Web.Extensions.dll', 'System.Windows.Forms.dll', 'System.Drawing.dll') |
    ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$sources = @('Program.cs', 'LauncherPreferences.cs', 'UiText.cs', 'WindowTheme.cs', 'LocaleFile.cs', 'LeagueLauncher.cs') |
    ForEach-Object { Join-Path $PSScriptRoot $_ }
$options = @('/nologo', '/optimize+', '/codepage:65001',
    ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')),
    ('/resource:' + (Join-Path $PSScriptRoot 'Main.xaml') + ',Launcher.Main.xaml'),
    ('/resource:' + (Join-Path $PSScriptRoot 'UiText.tsv') + ',Launcher.UiText.tsv')) + $references
$launcher = Join-Path $output.FullName 'LOL国际服改中文语音工具.exe'
& $compiler @options '/target:winexe' ('/out:' + $launcher) @sources
if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed.' }

if ($Verify) {
    $verification = Join-Path $output.FullName 'Verify.exe'
    & $compiler @options '/target:exe' '/main:Verify' ('/out:' + $verification) @sources (Join-Path $PSScriptRoot 'Verify.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Verification build failed.' }
    & $verification (Join-Path $output.FullName 'qa')
    if ($LASTEXITCODE -ne 0) { throw 'Verification failed.' }
}
Write-Output $launcher
