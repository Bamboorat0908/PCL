param([string]$BuildDirectory)
$ErrorActionPreference = 'Stop'

# Run in Windows PowerShell (-STA), matching the launcher's .NET Framework runtime.
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml,System.Net.Http,System.IO.Compression,System.IO.Compression.FileSystem
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'))
if (-not $BuildDirectory) { $BuildDirectory = Join-Path $project 'bin' }
foreach ($dependency in @('Newtonsoft.Json.dll', 'ThrottleDebounce.dll')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $project ('Resources\' + $dependency)))
}
$core = [Reflection.Assembly]::LoadFrom((Join-Path $BuildDirectory 'MeloongCore.dll'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $BuildDirectory 'MeloongCore.Wpf.dll'))
$pclCs = [Reflection.Assembly]::LoadFrom((Join-Path $BuildDirectory 'PCLCS.dll'))
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $BuildDirectory 'BRCL.exe'))
$javaModule = $assembly.GetType('PCL.ModJava', $true)
$rangeType = $core.GetType('MeloongCore.ValueRange`1', $true).MakeGenericType([type[]]@([version]))
$recommend = $javaModule.GetMethod('GetRecommendedJavaMajor')
$runtimePath = $javaModule.GetMethod('GetJavaRuntimeFilePath')
if (-not $recommend -or -not $runtimePath) { throw 'Build the Java-management implementation before running these checks.' }

function New-Range([string]$Factory, [string[]]$Versions = @()) {
    $arguments = [object[]]@($Versions | ForEach-Object { [version]$_ })
    return $rangeType.GetMethod($Factory).Invoke($null, $arguments)
}
function Assert-Recommended([string]$Label, [string]$Vanilla, [datetime]$Released, [int]$Metadata,
                            [object]$Range, [int]$Expected) {
    $version = if ($Vanilla) { [version]$Vanilla } else { $null }
    $actual = $recommend.Invoke($null, [object[]]@($version, $Released, $Metadata, $Range))
    if ($actual -ne $Expected) { throw "$Label expected Java $Expected but selected $actual." }
    Write-Output "PASS $Label -> Java $actual"
}

$all = New-Range 'All'
$java8 = New-Range 'ClosedOpen' @('8.0', '9.0')
$java16 = New-Range 'ClosedOpen' @('16.0', '17.0')
$java17 = New-Range 'AtLeast' @('17.0')
$java21 = New-Range 'AtLeast' @('21.0')
Assert-Recommended 'Older Minecraft uses Java 8' '16.0.5' '2021-01-15' 0 $all 8
Assert-Recommended 'Minecraft 1.17 uses Java 16' '17.0.0' '2021-06-08' 0 $all 16
Assert-Recommended 'Minecraft 1.18 uses Java 17' '18.0.0' '2021-11-30' 0 $java17 17
Assert-Recommended 'Minecraft 1.20.4 remains on Java 17' '20.0.4' '2023-12-07' 0 $java17 17
Assert-Recommended 'Minecraft 1.20.5 switches to Java 21' '20.0.5' '2024-04-23' 0 $java21 21
Assert-Recommended 'Minecraft 1.21 uses Java 21' '21.0.1' '2024-08-08' 0 $java21 21
Assert-Recommended 'Metadata supports a future Java major' '26.0.1' '2026-03-24' 25 (New-Range 'AtLeast' @('25.0')) 25
Assert-Recommended 'Metadata wins over a date fallback' '' '2024-04-23' 17 $java17 17
Assert-Recommended 'Snapshot before Java 16 transition' '' '2021-05-10' 0 $all 8
Assert-Recommended 'Snapshot with Java 16' '' '2021-05-12' 0 $all 16
Assert-Recommended 'Snapshot before Java 17 transition' '' '2021-11-15' 0 $all 16
Assert-Recommended 'Snapshot with Java 17' '' '2021-11-16' 0 $all 17
Assert-Recommended 'Snapshot before Java 21 transition' '' '2024-04-01' 0 $all 17
Assert-Recommended 'Snapshot with Java 21' '' '2024-04-03' 0 $all 21
Assert-Recommended 'Old Forge remains below Java 9' '16.0.5' '2021-01-15' 21 $java8 8
Assert-Recommended 'Legacy Forge can require Java 7' '7.0.2' '2013-10-25' 8 (New-Range 'ClosedOpen' @('7.0', '8.0')) 7
Assert-Recommended 'Java 8 update bounds still permit major 8' '16.0.5' '2021-01-15' 0 (New-Range 'Closed' @('8.0.141', '8.0.320')) 8
Assert-Recommended 'Early Forge 1.17 cannot receive Java 17' '17.0.1' '2021-07-06' 17 $java16 16
Assert-Recommended 'Custom Java 11 range is respected' '20.0.5' '2024-04-23' 21 (New-Range 'ClosedOpen' @('11.0', '12.0')) 11
Assert-Recommended 'Inclusive boundary allows exact Java 21' '20.0.4' '2023-12-07' 17 (New-Range 'Exactly' @('21.0.1')) 21
Assert-Recommended 'An empty range cannot select a runtime' '20.0.5' '2024-04-23' 21 (New-Range 'ClosedOpen' @('21.0', '21.0')) 0

$rank = $javaModule.GetMethod('RankJavaCandidates')
$javaType = $pclCs.GetType('PCLCS.Java', $true)
$candidateVersions = @('21.0.9', '17.0.8', '8.0.392', '17.0.9', $null)
$candidates = [Array]::CreateInstance($javaType, $candidateVersions.Count)
for ($i = 0; $i -lt $candidateVersions.Count; $i++) {
    $candidateVersion = if ($candidateVersions[$i]) { [version]$candidateVersions[$i] } else { $null }
    $candidatePath = Join-Path ([IO.Path]::GetTempPath()) ("BRCL-Java-Candidate-$i\bin")
    $candidates.SetValue($javaType.GetConstructor([type[]]@([string], [version])).Invoke([object[]]@([string]$candidatePath, [version]$candidateVersion)), $i)
}
$rankArguments = New-Object object[] 2
$rankArguments[0] = $candidates
$rankArguments[1] = 17
$ranked = @($rank.Invoke($null, $rankArguments))
$actualOrder = @($ranked | ForEach-Object { $_.Version.ToString() }) -join ','
if ($actualOrder -ne '17.0.9,17.0.8,21.0.9,8.0.392') {
    throw "Local Java ranking did not prioritize the recommended major and newest local patch: $actualOrder"
}
if ($candidates[0].Version.ToString() -ne '21.0.9') { throw 'Java ranking changed the stored manual list order.' }
$rankArguments[0] = [Array]::CreateInstance($javaType, 0)
if (@($rank.Invoke($null, $rankArguments)).Count -ne 0) { throw 'An empty Java list returned a candidate.' }
Write-Output 'PASS local Java ranking selects the recommended major and newest patch without changing manual priority'

# No file is written: validate how untrusted runtime-manifest paths are resolved.
$base = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) 'BRCL-Java-Path-Checks'))
foreach ($relative in @('bin/java.exe', 'bin\server\jvm.dll', 'lib/security/cacerts')) {
    $actual = $runtimePath.Invoke($null, [object[]]@($base, $relative))
    $expected = [IO.Path]::GetFullPath((Join-Path $base $relative))
    if ($actual -ine $expected) { throw "Runtime path resolution changed $relative." }
}
Write-Output 'PASS valid nested runtime files resolve within the install directory'

foreach ($relative in @('', '..', '../outside.exe', 'bin/../../outside.exe',
                         '..\BRCL-Java-Path-Checks-other\java.exe',
                         'C:\outside.exe', 'C:outside.exe', '\\server\share\java.exe',
                         '/outside.exe', '\outside.exe', 'bin/java.exe:stream')) {
    $failure = $null
    try { [void]$runtimePath.Invoke($null, [object[]]@($base, $relative)) }
    catch { $failure = $_.Exception.GetBaseException() }
    if ($failure -isnot [IO.InvalidDataException]) {
        throw "Unsafe runtime path was not rejected with InvalidDataException: '$relative'."
    }
}
Write-Output 'PASS traversal, sibling-prefix escape, rooted paths and alternate data streams are rejected'

# Exercise the production ZIP extractor with real archives and existing files.
$extract = $javaModule.GetMethod('ExtractJavaRuntimeArchive')
$archiveTempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
$archiveTempName = 'BRCL-Java-Archive-' + [Guid]::NewGuid().ToString('N')
$archiveTemp = [IO.Path]::GetFullPath((Join-Path $archiveTempBase $archiveTempName))
if ([IO.Path]::GetDirectoryName($archiveTemp) -ine $archiveTempBase -or [IO.Path]::GetFileName($archiveTemp) -cne $archiveTempName) {
    throw 'Unexpected Java archive fixture directory.'
}
[void][IO.Directory]::CreateDirectory($archiveTemp)
function New-TestArchive([string]$ZipPath, [string]$EntryName, [byte[]]$Bytes, [bool]$SymbolicLink = $false) {
    $zip = [IO.Compression.ZipFile]::Open($ZipPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $entry = $zip.CreateEntry($EntryName)
        if ($SymbolicLink) { $entry.ExternalAttributes = -1610612736 } # POSIX S_IFLNK in the upper 16 bits.
        $stream = $entry.Open()
        try { $stream.Write($Bytes, 0, $Bytes.Length) } finally { $stream.Dispose() }
    } finally { $zip.Dispose() }
}
function Invoke-Extract([string]$ZipPath, [string]$Destination, [Threading.CancellationToken]$Token = [Threading.CancellationToken]::None) {
    try { $extract.Invoke($null, [object[]]@($ZipPath, $Destination, $Token)) }
    catch { throw $_.Exception.GetBaseException() }
}
try {
    $instanceType = $assembly.GetType('PCL.ModMinecraft+McInstance', $true)
    $fixturePath = Join-Path $archiveTemp 'versions\metadata17'
    [void][IO.Directory]::CreateDirectory($fixturePath)
    $fixtureJson = @{
        id = 'metadata17'
        clientVersion = '1.16.5'
        mainClass = 'net.minecraft.client.main.Main'
        type = 'release'
        releaseTime = '2021-01-15T00:00:00Z'
        libraries = @()
        javaVersion = @{ majorVersion = 17; component = 'java-runtime-gamma' }
    } | ConvertTo-Json -Depth 5 -Compress
    [IO.File]::WriteAllText((Join-Path $fixturePath 'metadata17.json'), $fixtureJson)
    $fixture = $instanceType.GetConstructor([type[]]@([string])).Invoke([object[]]@([string]$fixturePath))
    $requirement = $javaModule.GetMethod('GetJavaRequirement').Invoke($null, [object[]]@($fixture))
    if ($requirement.Item1.Contains([version]'8.0.392') -or $requirement.Item1.Contains([version]'16.0.2') -or
        -not $requirement.Item1.Contains([version]'17.0.9') -or $requirement.Item2 -cne 'java-runtime-gamma') {
        throw 'Instance metadata requiring Java 17 allowed an incompatible fallback or lost its runtime component.'
    }
    Write-Output 'PASS real instance metadata raises the Java minimum and prevents fallback to Java 8 or 16'

    $payload = [byte[]]@(80, 75, 0, 127, 255, 18)
    $normal = Join-Path $archiveTemp 'normal.zip'
    New-TestArchive $normal 'jdk-17/bin/java.exe' $payload
    $normalOutput = Join-Path $archiveTemp 'normal-output'
    Invoke-Extract $normal $normalOutput
    $extracted = [IO.File]::ReadAllBytes((Join-Path $normalOutput 'jdk-17\bin\java.exe'))
    if ([Convert]::ToBase64String($extracted) -cne [Convert]::ToBase64String($payload)) { throw 'ZIP extraction changed runtime bytes.' }
    Write-Output 'PASS a real Java-shaped ZIP extracts byte-for-byte into a fresh directory'

    $traversal = Join-Path $archiveTemp 'traversal.zip'
    New-TestArchive $traversal '../escaped.exe' $payload
    $failure = $null
    try { Invoke-Extract $traversal (Join-Path $archiveTemp 'traversal-output') } catch { $failure = $_.Exception.GetBaseException() }
    if ($failure -isnot [IO.InvalidDataException] -or [IO.File]::Exists((Join-Path $archiveTemp 'escaped.exe'))) {
        throw 'ZIP traversal escaped or was not rejected.'
    }
    $link = Join-Path $archiveTemp 'symlink.zip'
    New-TestArchive $link 'bin/java.exe' $payload $true
    $failure = $null
    try { Invoke-Extract $link (Join-Path $archiveTemp 'symlink-output') } catch { $failure = $_.Exception.GetBaseException() }
    if ($failure -isnot [IO.InvalidDataException] -or [IO.File]::Exists((Join-Path $archiveTemp 'symlink-output\bin\java.exe'))) {
        throw 'A symbolic-link ZIP entry was extracted.'
    }
    Write-Output 'PASS ZIP traversal and symbolic links are rejected before their files are created'

    $existingOutput = Join-Path $archiveTemp 'existing-runtime'
    $existingFile = Join-Path $existingOutput 'jdk-17\bin\java.exe'
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($existingFile))
    [IO.File]::WriteAllText($existingFile, 'existing-runtime-sentinel')
    $failure = $null
    try { Invoke-Extract $normal $existingOutput } catch { $failure = $_.Exception.GetBaseException() }
    if ($failure -isnot [IO.IOException] -or [IO.File]::ReadAllText($existingFile) -cne 'existing-runtime-sentinel') {
        throw 'ZIP extraction overwrote an existing runtime file.'
    }
    Write-Output 'PASS an existing Java file survives an attempted overwrite'

    $cancelExtraction = New-Object Threading.CancellationTokenSource
    try {
        $cancelExtraction.Cancel()
        $failure = $null
        try { Invoke-Extract $normal $existingOutput $cancelExtraction.Token } catch { $failure = $_.Exception.GetBaseException() }
        if ($failure -isnot [OperationCanceledException] -or [IO.File]::ReadAllText($existingFile) -cne 'existing-runtime-sentinel') {
            throw 'Cancelled ZIP extraction altered or removed an existing runtime.'
        }
        $cancelOutput = Join-Path $archiveTemp 'cancelled-output'
        $failure = $null
        try { Invoke-Extract $normal $cancelOutput $cancelExtraction.Token } catch { $failure = $_.Exception.GetBaseException() }
        if ($failure -isnot [OperationCanceledException] -or [IO.Directory]::Exists($cancelOutput)) {
            throw 'Pre-cancelled ZIP extraction created a destination.'
        }
    } finally { $cancelExtraction.Dispose() }
    Write-Output 'PASS pre-cancelled extraction preserves existing Java and creates no installation directory'
} finally {
    # Only remove the uniquely named fixture directory verified above.
    $cleanupTarget = [IO.Path]::GetFullPath($archiveTemp)
    if ([IO.Path]::GetDirectoryName($cleanupTarget) -ine $archiveTempBase -or [IO.Path]::GetFileName($cleanupTarget) -cne $archiveTempName) {
        throw 'Refusing to clean an unexpected Java test path.'
    }
    if (Test-Path -LiteralPath $cleanupTarget) { Remove-Item -LiteralPath $cleanupTarget -Recurse -Force }
}

$sources = $assembly.GetType('PCL.ModJavaDownloadSources', $true)
$mapMojang = $sources.GetMethod('MapMojangJavaUrl')
$mapTemurin = $sources.GetMethod('MapTemurinJavaUrl')
function Assert-Throws([Reflection.MethodInfo]$Method, [object[]]$Arguments, [type]$Expected, [string]$Label) {
    $failure = $null
    try { [void]$Method.Invoke($null, $Arguments) } catch { $failure = $_.Exception.GetBaseException() }
    if ($null -eq $failure -or -not $Expected.IsInstanceOfType($failure)) {
        throw "$Label was not rejected with $($Expected.Name)."
    }
}
foreach ($hostName in @('piston-meta.mojang.com', 'piston-data.mojang.com', 'launchermeta.mojang.com', 'launcher.mojang.com')) {
    $official = "https://$hostName/v1/objects/0123456789abcdef/runtime.bin?cache=1"
    if ($mapMojang.Invoke($null, [object[]]@($official, 1)) -cne $official) {
        throw 'Official Mojang source changed its URL.'
    }
    if ($mapMojang.Invoke($null, [object[]]@($official, 2)) -cne 'https://bmclapi2.bangbang93.com/v1/objects/0123456789abcdef/runtime.bin?cache=1') {
        throw 'BMCLAPI mapping lost the object path or query.'
    }
}
foreach ($badUrl in @('http://piston-data.mojang.com/a', 'https://piston-data.mojang.com.evil.example/a',
                       'https://evil.example/piston-data.mojang.com/a', 'file:///C:/java.exe', 'not-a-url')) {
    Assert-Throws $mapMojang @($badUrl, 2) ([ArgumentException]) 'Untrusted Mojang runtime URL'
}
Assert-Throws $mapMojang @('https://piston-data.mojang.com/v1/objects/a', 3) ([NotSupportedException]) 'Unsupported Mojang source'
Write-Output 'PASS Mojang source mapping preserves content identity and rejects untrusted hosts or protocols'

$zipName = 'OpenJDK21U-jre_x64_windows_hotspot_21.0.9_10.zip'
$temurinUrl = 'https://github.com/adoptium/temurin21-binaries/releases/download/jdk-21.0.9%2B10/' + $zipName
if ($mapTemurin.Invoke($null, [object[]]@($temurinUrl, 21, 1)) -cne $temurinUrl) {
    throw 'Official Temurin source changed its URL.'
}
foreach ($source in @(@{Id=3; Host='mirrors.tuna.tsinghua.edu.cn'; Root='Adoptium'}, @{Id=4; Host='mirrors.nju.edu.cn'; Root='adoptium'})) {
    $expected = "https://$($source.Host)/$($source.Root)/21/jre/x64/windows/$zipName"
    if ($mapTemurin.Invoke($null, [object[]]@($temurinUrl, 21, [int]$source.Id)) -cne $expected) {
        throw 'Temurin mirror mapping selected a different runtime package.'
    }
}
foreach ($badUrl in @($temurinUrl.Replace('https:', 'http:'), $temurinUrl.Replace('github.com', 'github.com.evil.example'),
                       $temurinUrl.Replace('/adoptium/', '/another-publisher/'),
                       $temurinUrl.Replace('temurin21-binaries', 'temurin17-binaries'),
                       $temurinUrl.Replace('.zip', '.exe'))) {
    Assert-Throws $mapTemurin @($badUrl, 21, 3) ([ArgumentException]) 'Untrusted Temurin runtime URL'
}
Assert-Throws $mapTemurin @($temurinUrl, 17, 3) ([ArgumentException]) 'Wrong Temurin Java major'
Assert-Throws $mapTemurin @($temurinUrl, 21, 2) ([NotSupportedException]) 'Unsupported Temurin source'
Write-Output 'PASS official and Chinese Temurin mirrors use the same Windows x64 package and reject mismatched packages'

$orderSources = $sources.GetMethod('OrderJavaDownloadUrls')
$readMetadata = $sources.GetMethod('ReadJavaSourceText')
$cancel = New-Object Threading.CancellationTokenSource
try {
    $orderArguments = New-Object object[] 3
    $orderArguments[0] = [string[]]@($temurinUrl)
    $orderArguments[1] = [Threading.CancellationToken]::None
    $orderArguments[2] = $true
    $ordered = @($orderSources.Invoke($null, $orderArguments))
    if ($ordered.Count -ne 1 -or $ordered[0] -cne $temurinUrl) {
        throw 'Manual source selection added or substituted a download source.'
    }
    $orderArguments[0] = [string[]]@($temurinUrl, $temurinUrl)
    $ordered = @($orderSources.Invoke($null, $orderArguments))
    if ($ordered.Count -ne 1 -or $ordered[0] -cne $temurinUrl) {
        throw 'Duplicate source URLs were not reduced to a single source.'
    }
    $cancel.Cancel()
    $orderArguments[1] = $cancel.Token
    Assert-Throws $orderSources $orderArguments ([OperationCanceledException]) 'Cancelled source selection'
    Assert-Throws $readMetadata @('https://example.invalid/never-requested', $cancel.Token) ([OperationCanceledException]) 'Cancelled metadata lookup'
    $orderArguments[0] = [string[]]@('http://example.invalid/never-requested')
    $orderArguments[1] = [Threading.CancellationToken]::None
    Assert-Throws $orderSources $orderArguments ([ArgumentException]) 'Insecure speed probe'
    Assert-Throws $readMetadata @('http://example.invalid/never-requested', [Threading.CancellationToken]::None) ([ArgumentException]) 'Insecure metadata lookup'
    $orderArguments[0] = [string[]]@()
    Assert-Throws $orderSources $orderArguments ([ArgumentException]) 'Empty source selection'
} finally { $cancel.Dispose() }
Write-Output 'PASS manual source choice is preserved and cancellation or invalid requests stop before network access'
