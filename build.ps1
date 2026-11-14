param([switch]$Verify, [string]$InstallerCompiler)

$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (!(Test-Path -LiteralPath (Join-Path $framework 'csc.exe'))) {
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework C# compiler was not found.' }

$output = New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot 'artifacts')
$references = @('WPF\PresentationFramework.dll', 'WPF\PresentationCore.dll', 'WPF\WindowsBase.dll',
    'WPF\UIAutomationClient.dll', 'WPF\UIAutomationTypes.dll',
    'System.Xaml.dll', 'System.Web.Extensions.dll', 'System.Windows.Forms.dll', 'System.Drawing.dll', 'System.Net.Http.dll') |
    ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$sources = @('Program.cs', 'LauncherPreferences.cs', 'UiText.cs', 'WindowTheme.cs', 'LocaleFile.cs', 'LeagueLauncher.cs',
    'RiotPlayButton.cs', 'ReleaseUpdate.cs') |
    ForEach-Object { Join-Path $PSScriptRoot $_ }
$options = @('/nologo', '/optimize+', '/codepage:65001',
    ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')),
    ('/resource:' + (Join-Path $PSScriptRoot 'Main.xaml') + ',Launcher.Main.xaml'),
    ('/resource:' + (Join-Path $PSScriptRoot 'UiText.tsv') + ',Launcher.UiText.tsv')) + $references
$launcher = Join-Path $output.FullName 'LOL国际服改中文语音工具.exe'
$appIcon = Join-Path $PSScriptRoot 'assets\app-icon.ico'
& $compiler @options '/target:winexe' ('/win32icon:' + $appIcon) ('/out:' + $launcher) @sources
if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed.' }

if ($Verify) {
    $verification = Join-Path $output.FullName 'Verify.exe'
    & $compiler @options '/target:exe' '/main:Verify' ('/out:' + $verification) @sources (Join-Path $PSScriptRoot 'Verify.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Verification build failed.' }
    & $verification (Join-Path $output.FullName 'qa')
    if ($LASTEXITCODE -ne 0) { throw 'Verification failed.' }
}

if ($InstallerCompiler) {
    $version = [Reflection.AssemblyName]::GetAssemblyName($launcher).Version.ToString(3)
    $translation = Join-Path $PSScriptRoot 'artifacts\installer-tools\ChineseSimplified.isl'
    if (!(Test-Path -LiteralPath $translation)) {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $translation) | Out-Null
        Invoke-WebRequest -UseBasicParsing -Uri 'https://raw.githubusercontent.com/jrsoftware/issrc/is-6_7_4/Files/Languages/Unofficial/ChineseSimplified.isl' -OutFile $translation
    }
    & $InstallerCompiler ('/DAppVersion=' + $version) (Join-Path $PSScriptRoot 'installer\Setup.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Windows installer build failed.' }
    $setup = Get-Item -LiteralPath (Join-Path $output.FullName 'installer\LOL-Chinese-Voice-Tool-Setup.exe')
    @{ Version = $version; Size = $setup.Length; Sha256 = (Get-FileHash -LiteralPath $setup.FullName).Hash } |
        ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $setup.DirectoryName 'update.json') -Encoding Ascii
    Write-Output $setup.FullName
}
Write-Output $launcher
