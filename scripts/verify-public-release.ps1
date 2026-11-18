param(
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string]$Version = '2026.11.13.12',
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo ('artifacts\verifications\' + $Version) }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$base = 'http://t42.sjcmc.cn:25261/'
$localRelease = Join-Path $repo ('artifacts\updates\' + $Version)
foreach ($name in @('latest.json','latest.sig')) {
    $maximum = if ($name -eq 'latest.json') { 65536 } else { 4096 }
    & curl.exe --fail --silent --show-error --connect-timeout 10 --max-time 45 --max-filesize $maximum --output (Join-Path $OutputDirectory $name) ($base + $name)
    if ($LASTEXITCODE -ne 0) { throw "Public download failed: $name" }
}
$manifestPath = Join-Path $OutputDirectory 'latest.json'
$signaturePath = Join-Path $OutputDirectory 'latest.sig'
$bytes = [IO.File]::ReadAllBytes($manifestPath)
$signature = [Convert]::FromBase64String([IO.File]::ReadAllText($signaturePath).Trim())
$rsa = [Security.Cryptography.RSACryptoServiceProvider]::new()
try {
    $rsa.PersistKeyInCsp = $false
    $rsa.FromXmlString([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'update-server\public-key.xml')))
    if (-not $rsa.VerifyData($bytes,[Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256'),$signature)) { throw 'Public manifest signature is invalid.' }
} finally { $rsa.Dispose() }
$manifest = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
if ($manifest.version -ne $Version -or $manifest.package -cne "releases/BRCL-$Version.zip" -or $manifest.sha256 -notmatch '^[0-9a-f]{64}$' -or $manifest.size -le 0 -or $manifest.size -gt 268435456) { throw 'Public manifest does not describe the intended release.' }
Write-Output 'PASS public release version and trusted manifest signature'
$filename = "BRCL-$Version.zip"
$package = Join-Path $OutputDirectory $filename
& curl.exe --fail --silent --show-error --connect-timeout 10 --max-time 120 --max-filesize 268435456 --dump-header (Join-Path $OutputDirectory 'headers.txt') --output $package ($base + $manifest.package)
if ($LASTEXITCODE -ne 0) { throw 'Public package download failed.' }
$hash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
if ((Get-Item -LiteralPath $package).Length -ne $manifest.size -or $hash -ne $manifest.sha256) { throw 'Public package does not match its signed size/hash.' }
Write-Output 'PASS full public download size and SHA-256'
$downloaded = @('latest.json','latest.sig',$filename)
$directUrl = $null
if ($manifest.direct_exe) {
    if ($manifest.package_format -ne 'single-exe' -or $manifest.direct_exe -cne "releases/BRCL-$Version.exe" -or $manifest.direct_exe_sha256 -notmatch '^[0-9a-f]{64}$' -or $manifest.direct_exe_size -le 0 -or $manifest.direct_exe_size -gt 268435456) { throw 'Invalid signed direct executable metadata.' }
    $directName = "BRCL-$Version.exe"
    $directFile = Join-Path $OutputDirectory $directName
    $directUrl = $base + $manifest.direct_exe
    & curl.exe --fail --silent --show-error --connect-timeout 10 --max-time 120 --max-filesize 268435456 --dump-header (Join-Path $OutputDirectory 'direct-exe-headers.txt') --output $directFile $directUrl
    if ($LASTEXITCODE -ne 0) { throw 'Public direct executable download failed.' }
    if ((Get-Item -LiteralPath $directFile).Length -ne $manifest.direct_exe_size -or (Get-FileHash -LiteralPath $directFile -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.direct_exe_sha256) { throw 'Public direct executable does not match its signed size/hash.' }
    $downloaded += $directName
    Write-Output 'PASS full public direct EXE download size and signed SHA-256'
}
foreach ($name in $downloaded) {
    if ((Get-FileHash -LiteralPath (Join-Path $OutputDirectory $name) -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath (Join-Path $localRelease $name) -Algorithm SHA256).Hash) { throw "Public bytes differ from prepared release: $name" }
}
Write-Output "PASS all $($downloaded.Count) public files match the verified prepared release"
[pscustomobject]@{
    Version=$manifest.display_version
    Revision=$Version
    DownloadUrl=$(if ($directUrl) { $directUrl } else { $base+$manifest.package })
    UpdatePackageUrl=$base+$manifest.package
    UpdatePackageBytes=$manifest.size
    UpdatePackageSHA256=$hash
    DirectExeUrl=$directUrl
    DirectExeBytes=$manifest.direct_exe_size
    DirectExeSHA256=$manifest.direct_exe_sha256
    Bytes=$(if ($directUrl) { $manifest.direct_exe_size } else { $manifest.size })
    SHA256=$(if ($directUrl) { $manifest.direct_exe_sha256 } else { $hash })
    SignatureVerified=$true
    MatchesPreparedRelease=$true
    VerifiedUtc=[DateTime]::UtcNow.ToString('o')
} | ConvertTo-Json | Tee-Object -FilePath (Join-Path $OutputDirectory 'receipt.json')
