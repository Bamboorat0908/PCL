param(
    [string]$ToolsRoot = 'C:\works\BRCL\tools',
    [string]$MSBuildPath = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'
)
$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot '..\Plain Craft Launcher 2\Plain Craft Launcher 2.vbproj'
$env:DOTNET_ROOT = Join-Path $ToolsRoot 'dotnet8'
$env:MSBuildSDKsPath = Join-Path $env:DOTNET_ROOT 'sdk\8.0.404\Sdks'
$env:DOTNET_HOST_PATH = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
foreach ($required in @($MSBuildPath, $projectPath, $env:DOTNET_HOST_PATH, (Join-Path $ToolsRoot 'net48ref\build'))) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Missing build dependency: $required" }
}
& $MSBuildPath $projectPath -p:Configuration=Release -p:MSBuildEnableWorkloadResolver=false "-p:TargetFrameworkRootPath=$ToolsRoot\net48ref\build" -v:m -nologo
if ($LASTEXITCODE -ne 0) { throw "Release build failed: $LASTEXITCODE" }
