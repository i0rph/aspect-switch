param([switch]$Test, [switch]$Package, [switch]$UiSmoke, [string]$DotNetPath = 'dotnet')
$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw 'The Windows .NET Framework x64 C# compiler is required.' }
$version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'VERSION') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'VERSION must be major.minor.patch' }
$bin = Join-Path $PSScriptRoot 'bin/Release'
New-Item -ItemType Directory -Force -Path $bin | Out-Null
$assemblyInfo = Join-Path $bin 'AssemblyInfo.g.cs'
$assemblySource = @"
using System.Reflection;
[assembly: AssemblyTitle("Aspect Switch")]
[assembly: AssemblyDescription("Input-dependent aspect ratio automation for DDC/CI monitors")]
[assembly: AssemblyVersion("$version.0")]
[assembly: AssemblyFileVersion("$version.0")]
"@
[IO.File]::WriteAllText($assemblyInfo, $assemblySource, [Text.UTF8Encoding]::new($false))
$exePath = Join-Path $bin 'AspectSwitch.exe'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | Sort-Object Name | ForEach-Object FullName)
$appIcon = Join-Path $PSScriptRoot 'assets/app.ico'
$options = @("/win32icon:$appIcon", "/resource:$appIcon,AspectSwitch.AppIcon", '/nologo', '/optimize+', '/platform:x64', '/target:winexe', '/codepage:65001', '/warnaserror+', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Web.Extensions.dll', "/out:$exePath", $assemblyInfo) + $sources
& $compilerPath @options
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src/App.config') -Destination ($exePath + '.config')
Write-Output "Built $version : $exePath"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$uiRuntime = Join-Path $bin 'ui-runtime'
& $DotNetPath publish (Join-Path $PSScriptRoot 'ui/AspectSettings.csproj') -c Release -o $uiRuntime -p:RestoreLockedMode=true --nologo -v minimal "-p:Version=$version"
if ($LASTEXITCODE -ne 0) { throw 'WinUI build failed (.NET 8 SDK and Windows SDK required)' }
if ($UiSmoke) {
    $report = Join-Path $bin ('ui-smoke-' + [Guid]::NewGuid().ToString('N') + '.json')
    $process = Start-Process -FilePath (Join-Path $uiRuntime 'AspectSwitch.Settings.exe') -ArgumentList "--preview --smoke `"$report`"" -PassThru -WindowStyle Hidden
    if (-not $process.WaitForExit(60000)) { $process.Kill(); throw 'WinUI smoke test timed out' }
    if (-not (Test-Path -LiteralPath $report)) { throw 'WinUI smoke test did not produce a report' }
    $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if (-not $result.Passed) { throw $result.Error }
    Write-Output ("WinUI smoke checks: " + ($result.Checks -join ', '))
}
if ($Test) {
    $testExe = Join-Path $bin 'AspectSwitch.Tests.exe'
    $testSources = @('src/AppDataReset.cs', 'src/Settings.cs', 'src/Compatibility.cs', 'src/Theme.cs', 'src/SettingsForm.cs', 'src/Policy.cs', 'tests/PolicyTests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
    & $compilerPath /nologo /platform:x64 /target:exe /codepage:65001 /warnaserror+ /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll "/out:$testExe" @testSources
    if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
    & $testExe
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
}
if ($Package) {
    $dist = Join-Path $PSScriptRoot 'dist'
    $staging = Join-Path $bin ('package-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $dist, $staging | Out-Null
    Copy-Item -LiteralPath $exePath, ($exePath + '.config') -Destination $staging
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'settings.example.json') -Destination (Join-Path $staging 'settings.json')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '사용안내.txt'), (Join-Path $PSScriptRoot 'LICENSE') -Destination $staging
    Copy-Item -LiteralPath $uiRuntime -Destination $staging -Recurse
    $zipPath = Join-Path $dist "Aspect-Switch-$version-win-x64.zip"
    $packageFiles = @(Get-ChildItem -LiteralPath $staging | ForEach-Object FullName)
    Compress-Archive -LiteralPath $packageFiles -DestinationPath $zipPath -Force
    $checksum = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText(($zipPath + '.sha256'), "$checksum  $([IO.Path]::GetFileName($zipPath))`n", [Text.UTF8Encoding]::new($false))
    Write-Output "Packaged: $zipPath"
}
