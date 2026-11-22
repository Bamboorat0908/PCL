param(
    [string]$ToolsRoot = $env:BRCL_BUILD_TOOLS,
    [string]$MSBuildPath,
    [string]$DotnetPath,
    [string]$FrameworkRoot,
    [string]$PackagesPath,
    [string]$CoreTestLog
)
$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot '..\Plain Craft Launcher 2\BRCL.vbproj'
if (-not $MSBuildPath) {
    $MSBuildPath = (Get-Command msbuild.exe -ErrorAction SilentlyContinue).Source
    if (-not $MSBuildPath) {
        $vswhere = Join-Path ([Environment]::GetEnvironmentVariable('ProgramFiles(x86)')) 'Microsoft Visual Studio\Installer\vswhere.exe'
        if (Test-Path -LiteralPath $vswhere) {
            $MSBuildPath = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\Current\Bin\MSBuild.exe' | Select-Object -First 1
        }
    }
}
if (-not $MSBuildPath -or -not (Test-Path -LiteralPath $MSBuildPath -PathType Leaf)) { throw 'Install Visual Studio 2022 Build Tools with the .NET desktop build workload, or pass -MSBuildPath.' }
if ($ToolsRoot) {
    if (-not $DotnetPath) { $DotnetPath = Join-Path $ToolsRoot 'dotnet8\dotnet.exe' }
    if (-not $FrameworkRoot) { $FrameworkRoot = Join-Path $ToolsRoot 'net48ref\build' }
}
if (-not $DotnetPath) {
    $DotnetPath = (Get-Command dotnet.exe -ErrorAction SilentlyContinue).Source
    if (-not $DotnetPath) { $DotnetPath = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' }
}
if (-not (Test-Path -LiteralPath $DotnetPath -PathType Leaf)) { throw 'Install the .NET 8 SDK or pass -DotnetPath.' }
$dotnetRoot = Split-Path -Parent $DotnetPath
$sdkPath = Join-Path $dotnetRoot 'sdk\8.0.404\Sdks'
if (-not (Test-Path -LiteralPath $sdkPath -PathType Container)) { throw "Install .NET SDK 8.0.404 in $dotnetRoot." }
$env:DOTNET_ROOT = $dotnetRoot
$env:DOTNET_HOST_PATH = $DotnetPath
$env:MSBuildSDKsPath = $sdkPath
$arguments = @($projectPath, '-restore', '-p:Configuration=Release', '-p:MSBuildEnableWorkloadResolver=false', '-v:m', '-nologo')
if ($FrameworkRoot) {
    if (-not (Test-Path -LiteralPath $FrameworkRoot -PathType Container)) { throw "Missing .NET Framework reference assemblies: $FrameworkRoot" }
    $arguments += "-p:TargetFrameworkRootPath=$FrameworkRoot"
}
if ($PackagesPath) { $arguments += "-p:RestorePackagesPath=$PackagesPath" }
& $MSBuildPath @arguments
if ($LASTEXITCODE -ne 0) { throw "Release build failed: $LASTEXITCODE" }
if ($CoreTestLog) {
    $core = Join-Path $PSScriptRoot '..\MeloongCore\Tests'
    $arguments[0] = Join-Path $core 'MeloongCore.Tests.csproj'
    & $MSBuildPath @arguments
    if ($LASTEXITCODE -ne 0) { throw "MeloongCore test build failed: $LASTEXITCODE" }
    & (Join-Path $core 'bin\Release\net48\MeloongCore.Tests.exe') --no-ansi | Tee-Object -FilePath $CoreTestLog
    if ($LASTEXITCODE -ne 0) { throw "MeloongCore tests failed: $LASTEXITCODE" }
}
