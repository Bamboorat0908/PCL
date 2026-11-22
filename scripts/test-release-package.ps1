param(
    [string]$ReleaseDirectory = (Join-Path $PSScriptRoot '..\artifacts\updates\2026.11.13.12'),
    [string]$Version = '2026.11.13.12',
    [string]$DisplayVersion = '1.0.2.2'
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$ReleaseDirectory = [IO.Path]::GetFullPath($ReleaseDirectory)
$manifestPath = Join-Path $ReleaseDirectory 'latest.json'
$bytes = [IO.File]::ReadAllBytes($manifestPath)
$manifest = [Text.Encoding]::UTF8.GetString($bytes) | ConvertFrom-Json
if ($manifest.version -ne $Version -or $manifest.display_version -ne $DisplayVersion -or $manifest.package -ne "releases/BRCL-$Version.zip") { throw 'Release manifest version mismatch.' }
$rsa = New-Object Security.Cryptography.RSACryptoServiceProvider
try {
    $rsa.PersistKeyInCsp = $false
    $rsa.FromXmlString([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'update-server\public-key.xml')))
    $signature = [Convert]::FromBase64String([IO.File]::ReadAllText((Join-Path $ReleaseDirectory 'latest.sig')).Trim())
    if (-not $rsa.VerifyData($bytes,[Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256'),$signature)) { throw 'Release signature is invalid.' }
    $tampered = [byte[]]$bytes.Clone()
    $tampered[0] = $tampered[0] -bxor 1
    if ($rsa.VerifyData($tampered,[Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256'),$signature)) { throw 'Tampered manifest was accepted.' }
} finally { $rsa.Dispose() }
Write-Output 'PASS release manifest version, trusted signature and tamper rejection'
$archive = Join-Path $ReleaseDirectory "BRCL-$Version.zip"
if ((Get-Item -LiteralPath $archive).Length -ne $manifest.size -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.sha256) { throw 'Release package size/hash mismatch.' }
Write-Output 'PASS release package size and SHA-256'
$singleExe = $manifest.package_format -eq 'single-exe'
[string[]]$allowed = if ($singleExe) { @('BRCL.exe') } elseif (-not $manifest.package_format) { @('BRCL.exe','BRCL.exe.config','PCLCS.dll','MeloongCore.dll','MeloongCore.Wpf.dll','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.Wpf.dll','WebView2Loader.dll','Microsoft.Win32.Registry.dll','LICENCE') } else { throw 'Unknown release package format.' }
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    if ($zip.Entries.Count -ne $allowed.Count -or @($zip.Entries.FullName | Select-Object -Unique).Count -ne $allowed.Count) { throw 'Missing or duplicate release entries.' }
    foreach ($entry in $zip.Entries) {
        if ($entry.FullName -cnotin $allowed -or $entry.Length -le 0) { throw "Unexpected or empty release file: $($entry.FullName)" }
        $stream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
        finally { $stream.Dispose(); $sha.Dispose() }
        if ($hash -ne (Get-FileHash -LiteralPath (Join-Path $ReleaseDirectory ('payload\'+$entry.FullName)) -Algorithm SHA256).Hash) { throw "Packaged file differs from validated payload: $($entry.FullName)" }
    }
} finally { $zip.Dispose() }
Write-Output $(if ($singleExe) { 'PASS update ZIP contains only BRCL.exe and matches its single-file test payload' } else { 'PASS legacy package contains only the ten public launcher files and matches its test payload' })
$exe = Join-Path $ReleaseDirectory 'payload\BRCL.exe'
$fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion
$parsedVersion = [Version]::Parse($DisplayVersion)
$expectedFileVersion = '{0}.{1}.{2}.{3}' -f $parsedVersion.Major,$parsedVersion.Minor,[Math]::Max(0,$parsedVersion.Build),[Math]::Max(0,$parsedVersion.Revision)
if ($fileVersion -ne $expectedFileVersion) { throw "File version mismatch: $fileVersion" }
$assembly = [Reflection.Assembly]::LoadFrom($exe)
if ($singleExe) {
    $license = $assembly.GetManifestResourceStream('BRCL.LICENCE')
    if ($null -eq $license) { throw 'Single-file package is missing its embedded license.' }
    try {
        $copy = New-Object IO.MemoryStream
        try { $license.CopyTo($copy); $licenseBytes = $copy.ToArray() } finally { $copy.Dispose() }
    } finally { $license.Dispose() }
    if ([Convert]::ToBase64String($licenseBytes) -cne [Convert]::ToBase64String([IO.File]::ReadAllBytes([IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\LICENCE'))))) { throw 'Packaged embedded license differs from repository license.' }
    Write-Output 'PASS single-file executable embeds the complete repository license'
}
$updates = $assembly.GetType('PCL.ModBrclUpdate',$true)
if ($updates.GetField('BrclReleaseVersion').GetRawConstantValue() -ne $DisplayVersion -or $updates.GetField('BrclUpdateRevision').GetRawConstantValue() -ne $Version) { throw 'Packaged updater version mismatch.' }
$stream = $assembly.GetManifestResourceStream('BRCL.Changelog.json')
$reader = New-Object IO.StreamReader($stream)
try { $log = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
$releaseDate = [DateTime]::ParseExact((($Version.Split('.') | Select-Object -First 3) -join '.'),'yyyy.MM.dd',[Globalization.CultureInfo]::InvariantCulture).ToString('yyyy-MM-dd')
$structured = $null -ne $log[0].sections
$threePart = $structured -and $DisplayVersion -match '^\d+\.\d+\.\d+$'
$versionPattern = if ($threePart) { '^[0-9]+(?:\.[0-9]+){2}$' } else { '^[0-9]+(?:\.[0-9]+){2,3}$' }
if ($structured) {
    $previous = $null
    foreach ($entry in $log) {
        if ($entry.title -cnotmatch $versionPattern -or $entry.date -cnotmatch '^\d{4}-\d{2}-\d{2}$' -or @($entry.sections).Count -lt 1) { throw 'Invalid version entry in changelog.' }
        $current = [Version]$entry.title
        if ($null -ne $previous -and $current -ge $previous) { throw 'Changelog must list unique versions newest first.' }
        $previous = $current
        foreach ($section in $entry.sections) {
            if ([string]::IsNullOrWhiteSpace($section.title) -or @($section.items).Count -lt 1) { throw 'Empty changelog section.' }
            foreach ($item in $section.items) {
                if ([string]::IsNullOrWhiteSpace($item.summary) -or [string]::IsNullOrWhiteSpace($item.detail)) { throw 'Empty changelog item.' }
            }
        }
    }
    $releaseItems = @($log[0].sections | ForEach-Object { $_.items } | ForEach-Object { $_.summary + '。' + $_.detail })
} else {
    if ($log[0].status -ne '版本发布') { throw 'Previous-format changelog has an invalid status.' }
    $releaseItems = @($log[0].items)
}
$titlePrefix = if ($structured) { '' } else { 'BRCL ' }
if ($log[0].title -cne ($titlePrefix + $DisplayVersion) -or $log[0].date -ne $releaseDate -or $releaseItems.Count -lt 1 -or @($releaseItems | Where-Object { $_ -isnot [string] -or [string]::IsNullOrWhiteSpace($_) }).Count) { throw 'Release changelog is missing or invalid.' }
$originalRelease = @($log | Where-Object {
    $_.title -ceq ($titlePrefix + '1.0.1.1') -or $_.original_version -ceq '1.0.1.1'
})
if ($originalRelease.Count -ne 1) { throw 'Previous release history was lost.' }
Write-Output 'PASS executable file version, updater revision and embedded release history'

$websiteDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\website\brcl\dist'))
$websiteRelease = [IO.File]::ReadAllText((Join-Path $websiteDirectory 'release.json')) | ConvertFrom-Json
if ($websiteRelease.display_version -ne $DisplayVersion -or $websiteRelease.version -ne $Version -or $websiteRelease.build_date -ne $releaseDate) { throw 'Website release version/date differs from the executable.' }
$websiteItems = @($websiteRelease.release_notes)
$websiteHtml = [IO.File]::ReadAllText((Join-Path $websiteDirectory 'index.html'))
$regexOptions = [Text.RegularExpressions.RegexOptions]::Singleline
$latestPattern = if ($structured) { '<!-- release-current:start -->(.*?)<!-- release-current:end -->' } else { '<ul\b[^>]*\bid="release-notes-list"[^>]*>(.*?)</ul>' }
$visibleList = [regex]::Match($websiteHtml, $latestPattern, $regexOptions)
$visibleItems = @([regex]::Matches($visibleList.Groups[1].Value, '<li>(.*?)</li>', $regexOptions) | ForEach-Object {
    [Net.WebUtility]::HtmlDecode(($_.Groups[1].Value -replace '</?strong>', ''))
})
if ($structured) {
    $history = [regex]::Match($websiteHtml, '<!-- release-history:start -->(.*?)<!-- release-history:end -->', $regexOptions)
    $visibleVersions = @([regex]::Matches($history.Groups[1].Value, '<summary><span>([0-9.]+)</span><time') | ForEach-Object { $_.Groups[1].Value })
    $expectedVersions = @($log | Select-Object -Skip 1 -ExpandProperty title)
    if (($visibleVersions -join ',') -cne ($expectedVersions -join ',')) { throw 'Website version history differs from the embedded changelog.' }
}
if ($websiteItems.Count -ne $releaseItems.Count -or $visibleItems.Count -ne $releaseItems.Count) { throw 'Website release-note count differs from the embedded changelog.' }
for ($i=0; $i -lt $releaseItems.Count; $i++) {
    if ($websiteItems[$i] -cne $releaseItems[$i] -or $visibleItems[$i] -cne $releaseItems[$i]) { throw 'Website release notes differ from the embedded changelog.' }
}
Write-Output 'PASS website metadata and visible release notes exactly match the embedded release changelog'
Write-Output ('Update package URL: http://t42.sjcmc.cn:25261/' + $manifest.package)
if ($manifest.direct_exe) {
    if (-not $singleExe -or $manifest.direct_exe -cne "releases/BRCL-$Version.exe") { throw 'Invalid direct executable path or package format.' }
    $directFile = Join-Path $ReleaseDirectory "BRCL-$Version.exe"
    $directHash = (Get-FileHash -LiteralPath $directFile -Algorithm SHA256).Hash.ToLowerInvariant()
    if ((Get-Item -LiteralPath $directFile).Length -ne $manifest.direct_exe_size -or $directHash -ne $manifest.direct_exe_sha256 -or $directHash -ne (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()) { throw 'Direct executable differs from its signed manifest or update payload.' }
    Write-Output 'PASS signed direct EXE metadata and byte-for-byte identity with the update payload'
    Write-Output ('Download URL: http://t42.sjcmc.cn:25261/' + $manifest.direct_exe)
}
