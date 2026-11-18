param(
    [string]$Version = '2026.11.13.12',
    [string]$DisplayVersion = '1.0.2.2',
    [string]$Notes = '启用 BRCL 联网更新。',
    [string]$SourceExe,
    [switch]$Critical,
    [string]$SigningKey = (Join-Path $env:USERPROFILE '.codex\secrets\brcl-update-signing.xml'),
    [string]$Server = 'root@t42.sjcmc.cn',
    [int]$Port = 25210,
    [switch]$PrepareOnly,
    [switch]$PublishPrepared,
    [switch]$DirectExe = $true
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Invalid release version.' }
if ($PrepareOnly -and $PublishPrepared) { throw 'Choose PrepareOnly or PublishPrepared.' }
if ($DisplayVersion -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?$') { throw 'Invalid display version.' }
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source = [IO.File]::ReadAllText((Join-Path $repo 'Plain Craft Launcher 2\Modules\ModBrclUpdate.vb'))
if ($source -notmatch ('BrclUpdateRevision As String = "' + [regex]::Escape($Version) + '"')) { throw 'Update BrclReleaseVersion in ModBrclUpdate.vb to match the release first.' }
if ($source -notmatch ('BrclReleaseVersion As String = "' + [regex]::Escape($DisplayVersion) + '"')) { throw 'Display version does not match the client.' }
$bin = Join-Path $repo 'Plain Craft Launcher 2\bin'
$out = Join-Path $repo ('artifacts\updates\' + $Version)
$legacyNames = @('BRCL.exe', 'BRCL.exe.config', 'PCLCS.dll', 'MeloongCore.dll', 'MeloongCore.Wpf.dll', 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.Wpf.dll', 'WebView2Loader.dll', 'Microsoft.Win32.Registry.dll', 'LICENCE')
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = Join-Path $out "BRCL-$Version.zip"
$directFile = Join-Path $out "BRCL-$Version.exe"
$manifestPath = Join-Path $out 'latest.json'
$signaturePath = Join-Path $out 'latest.sig'
if (-not $PublishPrepared) {
    if (Test-Path -LiteralPath $out) { throw "Release already exists: $out" }
    $releaseExe = Join-Path $bin 'BRCL.exe'
    if ($SourceExe) {
        $releaseExe = [IO.Path]::GetFullPath($SourceExe)
        if (-not (Test-Path -LiteralPath $releaseExe -PathType Leaf)) { throw 'Source EXE does not exist.' }
    } else {
        & (Join-Path $PSScriptRoot 'build-release.ps1')
    }
    & (Join-Path $PSScriptRoot 'export-single-exe.ps1') -Exe $releaseExe -OutputDirectory (Join-Path $out 'payload')
    [IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $out 'payload'), $archive)
    $manifest = [ordered]@{version=$Version; display_version=$DisplayVersion; package="releases/BRCL-$Version.zip"; size=(Get-Item -LiteralPath $archive).Length; sha256=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant(); package_format='single-exe'; notes=$Notes; critical=[bool]$Critical}
    if ($DirectExe) {
        Copy-Item -LiteralPath (Join-Path $out 'payload\BRCL.exe') -Destination $directFile
        $manifest['direct_exe'] = "releases/BRCL-$Version.exe"
        $manifest['direct_exe_size'] = (Get-Item -LiteralPath $directFile).Length
        $manifest['direct_exe_sha256'] = (Get-FileHash -LiteralPath $directFile -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    $signer = [Security.Cryptography.RSACryptoServiceProvider]::new()
    try {
        $signer.PersistKeyInCsp = $false
        $signer.FromXmlString([IO.File]::ReadAllText($SigningKey))
        $signature = $signer.SignData([IO.File]::ReadAllBytes($manifestPath), [Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256'))
        [IO.File]::WriteAllText($signaturePath, [Convert]::ToBase64String($signature))
    } finally { $signer.Dispose() }
}
# Verify precisely the bytes that will be published, including on a retry.
$manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
if ($manifest.version -ne $Version -or $manifest.display_version -ne $DisplayVersion -or $manifest.package -ne "releases/BRCL-$Version.zip") { throw 'Prepared manifest version or package path mismatch.' }
if ((Get-Item -LiteralPath $archive).Length -ne $manifest.size -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.sha256) { throw 'Prepared package hash or size mismatch.' }
$rsa = [Security.Cryptography.RSACryptoServiceProvider]::new()
try {
    $rsa.PersistKeyInCsp = $false
    $rsa.FromXmlString([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'update-server\public-key.xml')))
    $signature = [Convert]::FromBase64String([IO.File]::ReadAllText($signaturePath).Trim())
    if (-not $rsa.VerifyData([IO.File]::ReadAllBytes($manifestPath), [Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256'), $signature)) { throw 'Signature verification failed.' }
} finally { $rsa.Dispose() }
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    [string[]]$expectedFiles = if ($manifest.package_format -eq 'single-exe') { @('BRCL.exe') } elseif (-not $manifest.package_format) { $legacyNames } else { throw 'Unknown prepared package format.' }
    if ($zip.Entries.Count -ne $expectedFiles.Count -or @($zip.Entries.FullName | Select-Object -Unique).Count -ne $expectedFiles.Count) { throw 'Unexpected release package entries.' }
    foreach ($entry in $zip.Entries) {
        if ($entry.FullName -cnotin $expectedFiles) { throw "Unexpected release file: $($entry.FullName)" }
    }
} finally { $zip.Dispose() }
$hasDirectExe = -not [string]::IsNullOrEmpty($manifest.direct_exe)
if ($hasDirectExe) {
    if ($manifest.package_format -ne 'single-exe' -or $manifest.direct_exe -cne "releases/BRCL-$Version.exe") { throw 'Invalid direct EXE path or package format.' }
    $directHash = (Get-FileHash -LiteralPath $directFile -Algorithm SHA256).Hash.ToLowerInvariant()
    if ((Get-Item -LiteralPath $directFile).Length -ne $manifest.direct_exe_size -or $directHash -ne $manifest.direct_exe_sha256 -or $directHash -ne (Get-FileHash -LiteralPath (Join-Path $out 'payload\BRCL.exe') -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'Prepared direct EXE does not match its manifest and payload.' }
}
$downloadUrl = "http://t42.sjcmc.cn:25261/releases/BRCL-$Version.zip"
$directUrl = "http://t42.sjcmc.cn:25261/releases/BRCL-$Version.exe"
$primaryUrl = if ($hasDirectExe) { $directUrl } else { $downloadUrl }
if ($PrepareOnly) { Write-Output "Prepared BRCL $DisplayVersion : $archive"; Write-Output "Download URL after publishing: $primaryUrl"; if ($hasDirectExe) { Write-Output "Internal update ZIP: $downloadUrl" }; return }
$uploadId = [Guid]::NewGuid().ToString('N')
& scp -o BatchMode=yes -o ConnectTimeout=15 -P $Port $archive "${Server}:/opt/brcl-updates/public/releases/BRCL-$Version.zip.$uploadId.part"
if ($LASTEXITCODE -ne 0) { throw 'Package upload failed.' }
if ($hasDirectExe) {
    & scp -o BatchMode=yes -o ConnectTimeout=15 -P $Port $directFile "${Server}:/opt/brcl-updates/public/releases/BRCL-$Version.exe.$uploadId.part"
    if ($LASTEXITCODE -ne 0) { throw 'Direct EXE upload failed.' }
}
foreach ($name in @('latest.json', 'latest.sig')) {
    & scp -o BatchMode=yes -o ConnectTimeout=15 -P $Port (Join-Path $out $name) "${Server}:/opt/brcl-updates/public/$name.$uploadId.new"
    if ($LASTEXITCODE -ne 0) { throw "Upload failed: $name" }
}
$manifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
$signatureHash = (Get-FileHash -LiteralPath $signaturePath -Algorithm SHA256).Hash.ToLowerInvariant()
$remoteChecks = @('set -e', 'cd /opt/brcl-updates/public', "echo '$($manifest.sha256)  releases/BRCL-$Version.zip.$uploadId.part' | sha256sum -c -", "echo '$manifestHash  latest.json.$uploadId.new' | sha256sum -c -", "echo '$signatureHash  latest.sig.$uploadId.new' | sha256sum -c -", "test ""`$(wc -c < releases/BRCL-$Version.zip.$uploadId.part)"" -eq $($manifest.size)")
$remoteMoves = @("if test -e releases/BRCL-$Version.zip; then echo '$($manifest.sha256)  releases/BRCL-$Version.zip' | sha256sum -c -; rm releases/BRCL-$Version.zip.$uploadId.part; else mv releases/BRCL-$Version.zip.$uploadId.part releases/BRCL-$Version.zip; fi")
$publicFiles = "releases/BRCL-$Version.zip"
if ($hasDirectExe) {
    $remoteChecks += "echo '$($manifest.direct_exe_sha256)  releases/BRCL-$Version.exe.$uploadId.part' | sha256sum -c -"
    $remoteChecks += "test ""`$(wc -c < releases/BRCL-$Version.exe.$uploadId.part)"" -eq $($manifest.direct_exe_size)"
    $remoteMoves += "if test -e releases/BRCL-$Version.exe; then echo '$($manifest.direct_exe_sha256)  releases/BRCL-$Version.exe' | sha256sum -c -; rm releases/BRCL-$Version.exe.$uploadId.part; else mv releases/BRCL-$Version.exe.$uploadId.part releases/BRCL-$Version.exe; fi"
    $publicFiles += " releases/BRCL-$Version.exe"
}
$activate = (($remoteChecks + $remoteMoves + @("chmod 644 $publicFiles latest.json.$uploadId.new latest.sig.$uploadId.new", "mv latest.sig.$uploadId.new latest.sig", "mv latest.json.$uploadId.new latest.json")) -join '; ')
& ssh -o BatchMode=yes -o ConnectTimeout=15 -p $Port $Server $activate
if ($LASTEXITCODE -ne 0) { throw 'Release activation failed.' }
Write-Output "Published BRCL $Version : $archive"
Write-Output "Download URL: $primaryUrl"
if ($hasDirectExe) { Write-Output "Internal update ZIP: $downloadUrl" }
