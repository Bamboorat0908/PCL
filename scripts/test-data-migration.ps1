param(
    [string]$NewExe = (Join-Path $PSScriptRoot '..\Plain Craft Launcher 2\bin\BRCL.exe'),
    [string]$PreviousExe,
    [string]$FixtureDirectory = (Join-Path $env:TEMP ('BRCL-Migration-' + [Guid]::NewGuid().ToString('N'))),
    [ValidateSet('Run','Seed','Verify','Restart','Rollback')][string]$Phase = 'Run'
)
$ErrorActionPreference = 'Stop'
$NewExe = (Resolve-Path -LiteralPath $NewExe).Path
if ($PreviousExe) { $PreviousExe = (Resolve-Path -LiteralPath $PreviousExe).Path }
$root = [IO.Path]::GetFullPath($FixtureDirectory)
$launcher = Join-Path $root 'launcher'
$game = Join-Path $launcher '.minecraft'
$version = Join-Path $game 'versions\isolated'
$shared = Join-Path $game 'versions\shared'
$legacySetup = Join-Path $launcher 'PCL\Setup.ini'
$activeSetup = Join-Path $launcher 'BRCL\Setup.ini'
$legacyInstanceSetup = Join-Path $version 'PCL\Setup.ini'
$legacyConfig = Join-Path $version 'PCL\config.json'
$legacyList = Join-Path $game 'PCL.ini'
$world = Join-Path $version 'saves\example\level.dat'
$sharedWorld = Join-Path $game 'saves\shared-example\level.dat'
$exePath = Join-Path $launcher 'BRCL test.exe'
$registryId = $env:BRCL_TEST_REGISTRY_ID

function Assert([bool]$Condition, [string]$Label) {
    if (-not $Condition) { throw $Label }
    Write-Output "PASS $Label"
}

if ($Phase -eq 'Run') {
    if (Test-Path -LiteralPath $root) { throw 'Use a new directory for the migration fixture.' }
    if ($PreviousExe -and $root -match '[\\/]AppData[\\/]Local[\\/]Temp[\\/]') {
        throw 'Use an upgrade fixture directory outside the Windows temporary directory.'
    }
    $previousRegistryId = $env:BRCL_TEST_REGISTRY_ID
    $registryId = [Guid]::NewGuid().ToString('N')
    $env:BRCL_TEST_REGISTRY_ID = $registryId
    $registryFixture = "Software\BRCLRegression\$registryId"
    [void][IO.Directory]::CreateDirectory($launcher)
    $windowsPs = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    try {
        $previousArgs = @()
        if ($PreviousExe) { $previousArgs = @('-PreviousExe', $PreviousExe) }
        $childArgs = @('-NoProfile','-STA','-ExecutionPolicy','Bypass','-File',$PSCommandPath,
                       '-NewExe',$NewExe,'-FixtureDirectory',$root) + $previousArgs
        & $windowsPs @childArgs -Phase Seed
        if ($LASTEXITCODE) { throw 'Legacy fixture creation failed.' }

        if ($PreviousExe) {
            Copy-Item -LiteralPath $PreviousExe -Destination $exePath
            $oldHash = (Get-FileHash -LiteralPath $exePath).Hash
            $install = (New-Item -ItemType Directory -Path (Join-Path $root 'install')).FullName
            $payload = (New-Item -ItemType Directory -Path (Join-Path $root 'package')).FullName
            Copy-Item -LiteralPath $NewExe -Destination (Join-Path $payload 'BRCL.exe')
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            [IO.Compression.ZipFile]::CreateFromDirectory($payload, (Join-Path $install 'release.zip'))
            $oldAssembly = [Reflection.Assembly]::LoadFile($PreviousExe)
            $embedded = $oldAssembly.GetManifestResourceStream('BRCL.BrclInstall.ps1')
            if (-not $embedded) { $embedded = $oldAssembly.GetManifestResourceStream('PCL.BrclInstall.ps1') }
            if (-not $embedded) { throw 'The previous EXE does not contain its updater.' }
            $installer = Join-Path $install 'install.ps1'
            $out = [IO.File]::Create($installer)
            try { $embedded.CopyTo($out) } finally { $out.Dispose(); $embedded.Dispose() }
            [IO.File]::WriteAllText((Join-Path $install 'install.json'),
                (@{pid=2147483647;target=$launcher;exe=[IO.Path]::GetFileName($exePath)} | ConvertTo-Json))

            $previousPreview = $env:BRCL_UI_PREVIEW
            $previousTemp = $env:TEMP
            $previousTmp = $env:TMP
            $env:BRCL_UI_PREVIEW = '1'
            $env:TEMP = $env:TMP = (New-Item -ItemType Directory -Path (Join-Path $root 'cache')).FullName
            $upgrade = [pscustomobject]@{Process=$null}
            function Start-Process {
                param([string]$FilePath,[string]$WorkingDirectory)
                $upgrade.Process = Microsoft.PowerShell.Management\Start-Process -FilePath $FilePath -WorkingDirectory $WorkingDirectory -WindowStyle Hidden -PassThru
            }
            try {
                & $installer -Quiet
                Assert ($upgrade.Process -and -not $upgrade.Process.WaitForExit(1000) -and $upgrade.Process.Path -ieq $exePath) 'previous installer restarted the upgraded executable'
                $log = Join-Path $launcher 'BRCL\Log1.txt'
                $deadline = (Get-Date).AddSeconds(20)
                $ready = $false
                while (-not $upgrade.Process.HasExited -and (Get-Date) -lt $deadline) {
                    $ready = (Test-Path -LiteralPath $log) -and
                        [bool](Select-String -LiteralPath $log -Pattern '启动首屏可交互' -Quiet -ErrorAction SilentlyContinue)
                    if ($ready) { break }
                    Start-Sleep -Milliseconds 100
                }
                Assert $ready 'upgraded launcher reached the interactive home page'
                Assert ((Get-FileHash -LiteralPath $exePath).Hash -eq (Get-FileHash -LiteralPath $NewExe).Hash) 'upgrade installed the exact tested executable'
                $backup = Get-ChildItem -LiteralPath (Join-Path $launcher 'BRCL\UpdateBackups') -Directory | Select-Object -First 1
                $backupExe = Join-Path $backup.FullName ([IO.Path]::GetFileName($exePath))
                Assert ((Get-FileHash -LiteralPath $backupExe).Hash -eq $oldHash) 'upgrade retained the byte-identical previous executable'
            } finally {
                Remove-Item Function:\Start-Process
                if ($upgrade.Process -and -not $upgrade.Process.HasExited) {
                    [void]$upgrade.Process.CloseMainWindow()
                    if (-not $upgrade.Process.WaitForExit(3000)) {
                        $upgrade.Process.Kill()
                        $upgrade.Process.WaitForExit()
                    }
                }
                $env:BRCL_UI_PREVIEW = $previousPreview
                $env:TEMP = $previousTemp
                $env:TMP = $previousTmp
            }
        }

        foreach ($next in 'Verify','Restart') {
            & $windowsPs @childArgs -Phase $next
            if ($LASTEXITCODE) { throw "Migration phase $next failed." }
        }
        if ($PreviousExe) {
            [IO.File]::Copy($backupExe, $exePath, $true)
            & $windowsPs @childArgs -Phase Rollback
            if ($LASTEXITCODE) { throw 'Rollback compatibility failed.' }
        }
        Write-Output "Migration fixtures: $root"
    } finally {
        $env:BRCL_TEST_REGISTRY_ID = $previousRegistryId
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($registryFixture, $false)
    }
    return
}

if ($registryId -notmatch '^[0-9a-f]{32}$') { throw 'Missing isolated registry fixture ID.' }
$registryFixture = "Software\BRCLRegression\$registryId"
$legacyRegistryPath = "$registryFixture\Software\PCLDebug"
$activeRegistryPath = "$registryFixture\Software\BRCL"
[AppDomain]::CurrentDomain.SetData('APP_CONTEXT_BASE_DIRECTORY', $launcher + '\')
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml,System.Net.Http

$legacyPhase = $PreviousExe -and $Phase -in @('Seed','Rollback')
$sourceExe = if ($Phase -eq 'Rollback') { $exePath } elseif ($legacyPhase) { $PreviousExe } else { $NewExe }
$assembly = [Reflection.Assembly]::LoadFrom($sourceExe)
[Windows.Application]::ResourceAssembly = $assembly
$app = [Activator]::CreateInstance($assembly.GetType('BRCL.Application',$true),$true)
$app.InitializeComponent()
$core = [Reflection.Assembly]::Load('MeloongCore')
$core.GetType('MeloongCore.Main',$true).GetMethod('Init').Invoke($null,[object[]]@('BRCL')) | Out-Null
$flags = [Reflection.BindingFlags]'Public,Static'
$settings = $assembly.GetType('BRCL.Settings',$true)
$get = @($settings.GetMethods($flags) | Where-Object { $_.Name -eq 'Get' -and -not $_.IsGenericMethod })[0]
$set = $settings.GetMethod('Set',$flags)
$reset = $settings.GetMethod('Reset',$flags)
$instanceType = $assembly.GetType('BRCL.ModMinecraft+McInstance',$true)
$constructor = $instanceType.GetConstructor([type[]]@([string]))
$module = $assembly.GetType('BRCL.ModMinecraft',$true)
$ini = $assembly.GetType('BRCL.ModBase',$true)
$readIni = $ini.GetMethod('ReadIni',$flags)
$writeIni = $ini.GetMethod('WriteIni',$flags)
$accounts = @{
    CacheMsV2OAuthRefresh = 'fixture-refresh'
    CacheMsV2Access = 'fixture-access'
    CacheMsV2Name = 'FixtureUser'
    CacheMsV2Uuid = '00000000000000000000000000000001'
    LoginMsJson = '{"FixtureUser":"fixture-refresh"}'
    LoginLegacyName = 'FixtureOffline'
}

function Set-Setting($Key,$Value,$Instance = $null) {
    [void]$set.Invoke($null,[object[]]@($Key,$Value,$Instance))
}
function Get-Setting($Key,$Instance = $null) {
    $get.Invoke($null,[object[]]@($Key,$Instance))
}
function Set-Config($Provider,$Key,$Value) {
    [void]$Provider.GetType().GetMethod('Set').MakeGenericMethod([type[]]@($Value.GetType())).Invoke(
        $Provider,[object[]]@($Key,$Value,$false))
}
function Read-Config($Provider,$Key,$ExpectedType) {
    $Provider.GetType().GetMethod('Read').MakeGenericMethod([type[]]@($ExpectedType)).Invoke(
        $Provider,[object[]]@($Key,[Activator]::CreateInstance($ExpectedType),$false))
}
function Save-Config {
    $settingCore = $instance.Config.GetType().GetInterface('MeloongCore.IConfigProvider').Assembly
    [void]$settingCore.GetType('MeloongCore.ConfigUtils',$true).GetMethod('SaveAll').Invoke($null,@())
}

$hive = $null
try {
    if ($legacyPhase) {
        # The old build uses a fixed HKCU path. Override HKCU only in this child.
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class BrclRegistryFixture {
    [DllImport("advapi32.dll")]
    private static extern int RegOverridePredefKey(IntPtr key, IntPtr replacement);
    public static int Override(IntPtr replacement) {
        return RegOverridePredefKey(new IntPtr(unchecked((int)0x80000001)), replacement);
    }
}
'@
        $hive = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($registryFixture)
        $hive.SetValue('__BRCL_RegistryFixture',$registryId)
        $redirected = [BrclRegistryFixture]::Override($hive.Handle.DangerousGetHandle())
        if ($redirected -ne 0 -or [Microsoft.Win32.Registry]::CurrentUser.GetValue('__BRCL_RegistryFixture') -cne $registryId) {
            throw 'The legacy process did not bind to the isolated registry.'
        }
    }
    [void]$module.GetProperty('McFolderSelected',$flags).SetValue($null,$game+'\',$null)
    $instance = $constructor.Invoke([object[]]@([string]$version))
    $sharedInstance = $constructor.Invoke([object[]]@([string]$shared))

    switch ($Phase) {
        'Seed' {
            foreach ($folder in (Split-Path $world -Parent),(Split-Path $sharedWorld -Parent),(Split-Path $legacySetup -Parent),(Split-Path $activeSetup -Parent),(Split-Path $legacyConfig -Parent),(Join-Path $shared 'PCL')) {
                [void][IO.Directory]::CreateDirectory($folder)
            }
            [IO.File]::WriteAllText($world,'isolated save bytes')
            [IO.File]::WriteAllText($sharedWorld,'shared save bytes')
            foreach ($folder in $version,$shared) {
                $name = Split-Path $folder -Leaf
                $manifest = @{
                    id = $name; clientVersion = '1.21.1'; type = 'release'; libraries = @()
                    mainClass = 'net.minecraft.client.main.Main'; releaseTime = '2024-08-08T00:00:00Z'
                } | ConvertTo-Json
                [IO.File]::WriteAllText((Join-Path $folder "$name.json"), $manifest)
            }
            [IO.File]::WriteAllText($activeSetup,"UiLogoText:BRCL conflict winner`r`n")
            if ($legacyPhase) {
                foreach ($key in $accounts.Keys) { Set-Setting $key $accounts[$key] }
                Set-Setting 'CacheMsV2Migrated' $true
                Set-Setting 'SystemLastVersionReg' 408
                Set-Setting 'SystemEulaVersion' 2
                Set-Setting 'UiLogoText' 'legacy title'
                Set-Setting 'UiLauncherTransparent' 735
                Set-Setting 'VersionArgumentTitle' 'legacy instance' $instance
                Set-Setting 'VersionArgumentIndieV2' $true $instance
                Set-Setting 'VersionArgumentIndieV2' $true $sharedInstance
                Set-Setting 'VersionArgumentIndieV2' $false $sharedInstance
                [void]$writeIni.Invoke($null,[object[]]@($legacyList.PSObject.BaseObject,'Version','isolated'))
                Set-Config $instance.Config 'InstanceMigratedJava' $true
                Save-Config
                Assert ((Get-Setting 'CacheMsV2OAuthRefresh') -ceq $accounts.CacheMsV2OAuthRefresh) 'previous executable wrote and read a synthetic cached account'
                $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\PCLDebug',$true)
            } else {
                [IO.File]::WriteAllText($legacySetup,"UiLogoText:legacy title`r`nUiLauncherTransparent:735`r`n")
                [IO.File]::WriteAllText($legacyInstanceSetup,"VersionArgumentTitle:legacy instance`r`nVersionArgumentIndieV2:True`r`n")
                [IO.File]::WriteAllText((Join-Path $shared 'PCL\Setup.ini'),"VersionArgumentIndieV2:False`r`n")
                [IO.File]::WriteAllText($legacyList,"Version:isolated`r`n")
                [IO.File]::WriteAllText($legacyConfig,'{"InstanceMigratedJava":true}')
                $secret = $settings.GetMethod('DesEncrypt',[Reflection.BindingFlags]'NonPublic,Static')
                $identify = $ini.GetField('Identify',$flags).GetValue($null)
                [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($activeRegistryPath, $false)
                $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($legacyRegistryPath)
                foreach ($name in $accounts.Keys) {
                    $key.SetValue($name,$secret.Invoke($null,[object[]]@($accounts[$name],('PCL'+$identify))))
                }
                $key.SetValue('SystemEulaVersion','2')
            }
            try {
                $key.SetValue('FixtureLegacyDword',7,[Microsoft.Win32.RegistryValueKind]::DWord)
                $key.SetValue('FixtureConflict','PCL value')
            } finally { $key.Dispose() }
            $currentPath = if ($legacyPhase) { 'Software\BRCL' } else { $activeRegistryPath }
            $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($currentPath)
            try { $key.SetValue('FixtureConflict','BRCL value') } finally { $key.Dispose() }
            $files = @($legacySetup,$legacyInstanceSetup,(Join-Path $shared 'PCL\Setup.ini'),$legacyConfig,$legacyList,$world,$sharedWorld)
            $files | ForEach-Object { [pscustomobject]@{Path=$_;Hash=(Get-FileHash -LiteralPath $_).Hash} } |
                ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'old-files.json') -Encoding UTF8
        }
        'Verify' {
            foreach ($key in $accounts.Keys) {
                Assert ((Get-Setting $key) -ceq $accounts[$key]) "previous value is readable for $key"
            }
            $legacyKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($legacyRegistryPath)
            $activeKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($activeRegistryPath)
            try {
                $ciphertext = $legacyKey.GetValue('CacheMsV2OAuthRefresh')
                Assert ($ciphertext -cne $accounts.CacheMsV2OAuthRefresh -and $activeKey.GetValue('CacheMsV2OAuthRefresh') -ceq $ciphertext) 'account migration copies the ciphertext without disclosing the credential'
                Assert ($activeKey.GetValueKind('FixtureLegacyDword') -eq [Microsoft.Win32.RegistryValueKind]::DWord -and $activeKey.GetValue('FixtureLegacyDword') -eq 7) 'registry value kind is preserved'
                Assert ($activeKey.GetValue('FixtureConflict') -ceq 'BRCL value') 'existing BRCL registry value takes precedence'
            } finally { $activeKey.Dispose(); $legacyKey.Dispose() }
            Set-Setting 'CacheMsV2OAuthRefresh' 'fixture-refresh-next'
            [void]$reset.Invoke($null,[object[]]@('CacheMsV2Access',$null))
            Assert ((Get-Setting 'CacheMsV2Access') -ceq '') 'reset does not reintroduce the old cached credential'

            Assert ((Get-Setting 'UiLogoText') -ceq 'BRCL conflict winner') 'existing BRCL global value takes precedence'
            Assert ((Get-Setting 'UiLauncherTransparent') -eq 735) 'global migration fills missing keys from PCL'
            [void]$reset.Invoke($null,[object[]]@('UiLauncherTransparent',$null))
            Set-Setting 'UiLogoText' 'BRCL updated title'
            Assert ((Get-Setting 'UiLauncherTransparent') -eq 600 -and [IO.File]::ReadAllText($activeSetup).Contains('BRCL updated title')) 'global writes and reset affect only BRCL Setup.ini'

            Assert ((Get-Setting 'VersionArgumentTitle' $instance) -ceq 'legacy instance') 'instance setup migrates from the version PCL directory'
            Set-Setting 'VersionArgumentTitle' 'BRCL updated instance' $instance
            Assert ([IO.File]::ReadAllText((Join-Path $version 'BRCL\Setup.ini')).Contains('BRCL updated instance')) 'instance setting writes to the new BRCL path'
            Assert ((Read-Config $instance.Config 'InstanceMigratedJava' ([bool])) -eq $true) 'instance Java metadata survives JSON migration'
            Set-Config $instance.Config 'FixtureNewJavaMetadata' 17
            Assert ((Read-Config $instance.Config 'FixtureNewJavaMetadata' ([int])) -eq 17) 'instance JSON accepts the new metadata in memory'
            Save-Config
            Assert ([IO.File]::ReadAllText((Join-Path $version 'BRCL\config.json')).Contains('FixtureNewJavaMetadata')) 'subsequent JSON updates use the BRCL configuration'
            $gameIni = Join-Path $game 'BRCL.ini'
            Assert ($readIni.Invoke($null,[object[]]@($gameIni.PSObject.BaseObject,'Version','')) -ceq 'isolated') 'game folder metadata migrates from PCL.ini'
            [void]$writeIni.Invoke($null,[object[]]@($gameIni.PSObject.BaseObject,'Version','shared'))
            Assert ($readIni.Invoke($null,[object[]]@($gameIni.PSObject.BaseObject,'Version','')) -ceq 'shared') 'game folder writes switch to BRCL.ini'
            Assert ($instance.PathIndie -ieq ($version+'\') -and $sharedInstance.PathIndie -ieq ($game+'\')) 'isolated and shared saves still resolve to the original game directories'

            $blocked = Join-Path $game 'versions\blocked'
            [void][IO.Directory]::CreateDirectory((Join-Path $blocked 'PCL'))
            [IO.File]::WriteAllText((Join-Path $blocked 'PCL\Setup.ini'),"VersionArgumentTitle:old value`r`n")
            [IO.File]::WriteAllText((Join-Path $blocked 'PCL\config.json'),'{"InstanceMigratedJava":true}')
            $obstruction = Join-Path $blocked 'BRCL'
            [IO.File]::WriteAllText($obstruction,'deliberate fixture obstruction')
            $blockedInstance = $constructor.Invoke([object[]]@([string]$blocked))
            Assert ((Get-Setting 'VersionArgumentTitle' $blockedInstance) -ceq 'old value' -and
                    (Read-Config $blockedInstance.Config 'InstanceMigratedJava' ([bool]))) 'blocked migration falls back to readable old INI and JSON'
            Set-Setting 'VersionArgumentTitle' 'failed write' $blockedInstance
            Assert ((Get-Setting 'VersionArgumentTitle' $blockedInstance) -ceq 'old value' -and
                    [IO.File]::ReadAllText((Join-Path $blocked 'PCL\Setup.ini')).Contains('old value')) 'blocked migration leaves the old file and cached value intact'
            [IO.File]::Delete($obstruction)
            Set-Setting 'VersionArgumentTitle' 'successful retry' $blockedInstance
            Assert ([IO.File]::ReadAllText((Join-Path $blocked 'BRCL\Setup.ini')).Contains('successful retry')) 'writes switch after a successful retry'

            $overrides = Join-Path $root 'legacy-pack-overrides'
            $imported = Join-Path $game 'versions\imported'
            [void][IO.Directory]::CreateDirectory((Join-Path $overrides 'PCL'))
            [void][IO.Directory]::CreateDirectory((Join-Path $imported 'BRCL'))
            $portableIni = Join-Path $overrides 'PCL\Setup.ini'
            $importedIni = Join-Path $imported 'BRCL\Setup.ini'
            [IO.File]::WriteAllText($portableIni,"VersionArgumentTitle:pack title`r`nIsStar:True`r`n")
            [IO.File]::WriteAllText($importedIni,"VersionArgumentTitle:installer default`r`n")
            $import = $assembly.GetType('BRCL.ModModpack',$true).GetMethod('CopyOverrideDirectory',[Reflection.BindingFlags]'NonPublic,Static')
            [void]$import.Invoke($null,[object[]]@($overrides.PSObject.BaseObject,$imported.PSObject.BaseObject,$null))
            $importedSetup = [IO.File]::ReadAllText($importedIni)
            Assert ($importedSetup.Contains('VersionArgumentTitle:pack title') -and $importedSetup.Contains('VersionArgumentIndieV2:True') -and
                    $importedSetup.Contains('IsStar:False')) 'import preserves legacy pack overrides and enables instance isolation'
            Assert ([IO.File]::ReadAllText($portableIni).Contains('IsStar:True')) 'import leaves the source pack configuration intact'
        }
        'Restart' {
            Assert ((Get-Setting 'CacheMsV2OAuthRefresh') -ceq 'fixture-refresh-next' -and (Get-Setting 'CacheMsV2Access') -ceq '') 'restart reads migrated account state without reviving the old credential'
            Assert ((Get-Setting 'UiLauncherTransparent') -eq 600 -and (Get-Setting 'UiLogoText') -ceq 'BRCL updated title') 'restart does not restore reset global values from PCL'
            Assert ((Get-Setting 'VersionArgumentTitle' $instance) -ceq 'BRCL updated instance' -and
                    (Read-Config $instance.Config 'FixtureNewJavaMetadata' ([int])) -eq 17) 'restart reads the new instance settings and JSON'
        }
        'Rollback' {
            Assert ((Get-Setting 'CacheMsV2OAuthRefresh') -ceq $accounts.CacheMsV2OAuthRefresh -and
                    (Get-Setting 'CacheMsV2Access') -ceq $accounts.CacheMsV2Access) 'restored previous executable reads the preserved old account'
            Assert ((Get-Setting 'UiLogoText') -ceq 'legacy title' -and (Get-Setting 'UiLauncherTransparent') -eq 735) 'restored previous executable reads the preserved global settings'
            Assert ((Get-Setting 'VersionArgumentTitle' $instance) -ceq 'legacy instance' -and
                    (Read-Config $instance.Config 'InstanceMigratedJava' ([bool]))) 'restored previous executable reads the preserved instance data'
            Assert ($instance.PathIndie -ieq ($version+'\') -and $sharedInstance.PathIndie -ieq ($game+'\')) 'rollback sees both saves in their original locations'
        }
    }
    if ($Phase -ne 'Seed') {
        $snapshot = Get-Content -LiteralPath (Join-Path $root 'old-files.json') -Raw | ConvertFrom-Json
        foreach ($file in $snapshot) {
            if ((Get-FileHash -LiteralPath $file.Path).Hash -cne $file.Hash) { throw "Preserved file differs: $($file.Path)" }
        }
        Assert $true 'legacy settings files and both saves are byte-identical'
    }
} finally {
    if ($hive) {
        [void][BrclRegistryFixture]::Override([IntPtr]::Zero)
        $hive.Dispose()
    }
    $app.Shutdown()
}
