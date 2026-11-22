param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$ToolsRoot = $env:BRCL_BUILD_TOOLS,
    [string]$PackagesPath,
    [string]$PythonPath,
    [string]$NodePath,
    [string]$PackArchive
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$sourceCommit = (git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[0-9a-f]{40}$') { throw 'A committed Git checkout is required.' }
git -C $repo diff --quiet HEAD --
if ($LASTEXITCODE -ne 0) { throw 'Commit the source before recording a release.' }

$out = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $out) { throw "Output directory already exists: $out" }
New-Item -ItemType Directory -Path $out | Out-Null
$tests = Join-Path $out 'tests'
$coreLog = Join-Path $out 'meloongcore-tests.txt'
& (Join-Path $PSScriptRoot 'build-release.ps1') -ToolsRoot $ToolsRoot -PackagesPath $PackagesPath -CoreTestLog $coreLog
& (Join-Path $PSScriptRoot 'test-all.ps1') -OutputDirectory $tests -PythonPath $PythonPath -NodePath $NodePath -PackArchive $PackArchive

$exe = Join-Path $repo 'Plain Craft Launcher 2\bin\BRCL.exe'
$updateSource = [IO.File]::ReadAllText((Join-Path $repo 'Plain Craft Launcher 2\Modules\ModBrclUpdate.vb'))
$revision = [regex]::Match($updateSource, 'BrclUpdateRevision As String = "([0-9.]+)"').Groups[1].Value
$display = [regex]::Match($updateSource, 'BrclReleaseVersion As String = "([0-9.]+)"').Groups[1].Value
if ($revision -notmatch '^\d+\.\d+\.\d+\.\d+$' -or $display -notmatch '^\d+\.\d+\.\d+$' -or
    (Get-Item -LiteralPath $exe).VersionInfo.FileVersion -ne "$display.0") { throw 'Release version metadata differs.' }
$payload = Join-Path $out 'payload'
& (Join-Path $PSScriptRoot 'export-single-exe.ps1') -Exe $exe -OutputDirectory $payload
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = Join-Path $out "BRCL-$revision.zip"
[IO.Compression.ZipFile]::CreateFromDirectory($payload, $archive)
$packed = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    if ($packed.Entries.Count -ne 1 -or $packed.Entries[0].FullName -cne 'BRCL.exe') { throw 'Expected one file in the ZIP.' }
    $stream = $packed.Entries[0].Open()
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $insideHash = [Convert]::ToHexString($sha.ComputeHash($stream)) } finally { $stream.Dispose(); $sha.Dispose() }
} finally { $packed.Dispose() }
$exeHash = (Get-FileHash -LiteralPath (Join-Path $payload 'BRCL.exe')).Hash
if ($insideHash -ine $exeHash) { throw 'Packed EXE differs from the tested build.' }

$testReport = Join-Path $tests 'results.json'
$results = Get-Content -LiteralPath $testReport -Raw -Encoding UTF8 | ConvertFrom-Json
$record = [ordered]@{
    display_version = $display
    revision = $revision
    git_commit = $sourceCommit
    exe = 'payload/BRCL.exe'
    exe_sha256 = $exeHash.ToLowerInvariant()
    exe_bytes = (Get-Item -LiteralPath $exe).Length
    update_zip = "BRCL-$revision.zip"
    update_zip_sha256 = (Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant()
    test_report = 'tests/results.json'
    test_report_sha256 = (Get-FileHash -LiteralPath $testReport).Hash.ToLowerInvariant()
    test_groups = @($results).Count
    reported_checks = ($results | Measure-Object -Property PassedAssertions -Sum).Sum
    core_test_report = 'meloongcore-tests.txt'
    core_test_report_sha256 = (Get-FileHash -LiteralPath $coreLog).Hash.ToLowerInvariant()
}
[IO.File]::WriteAllText((Join-Path $out 'release-record.json'), ($record | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
Write-Output "Verified release: $out\release-record.json"
