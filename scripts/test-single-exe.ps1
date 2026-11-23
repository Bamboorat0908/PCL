param([string]$ExePath = (Join-Path $PSScriptRoot '..\Plain Craft Launcher 2\bin\BRCL.exe'))
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml,System.Net.Http
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$stage=Join-Path $env:TEMP ('BRCL-SingleExe-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
$exe=Join-Path $stage 'BRCL.exe'
Copy-Item -LiteralPath $ExePath -Destination $exe
if (@(Get-ChildItem -LiteralPath $stage -File).Count -ne 1) { throw 'Single EXE fixture has extra files.' }
# Keep a small budget for code growth without allowing the duplicate icon to return.
$exeSize = (Get-Item -LiteralPath $exe).Length
if ($exeSize -gt 5300000) { throw "Standalone EXE is $exeSize bytes; budget is 5300000." }
Write-Output "PASS standalone EXE stays within its 5300000-byte budget ($exeSize bytes)"
$assembly=[Reflection.Assembly]::LoadFrom($exe)
[Windows.Application]::ResourceAssembly=$assembly
$appType=$assembly.GetType('BRCL.Application',$true)
$app=[Activator]::CreateInstance($appType,$true)
$app.InitializeComponent()
$startup=$appType.GetMethod('Application_Startup',[Reflection.BindingFlags]'NonPublic,Instance')
$app.remove_Startup([Delegate]::CreateDelegate([Windows.StartupEventHandler],$app,$startup))
[Windows.Application].GetField('_startupUri',[Reflection.BindingFlags]'NonPublic,Instance').SetValue($app,$null)
Write-Output 'PASS EXE-only directory initializes WPF application resources without a config file'
$names=@('NAudio','Newtonsoft.Json','Ookii.Dialogs.Wpf','Imazen.WebP','CacheCow.Common','CacheCow.Client.FileStore','CacheCow.Client','ThrottleDebounce','System.Net.Http.Formatting','Microsoft.Web.WebView2.Core','Microsoft.Web.WebView2.Wpf','Microsoft.Win32.Registry','BRCL.Core','MeloongCore.Wpf','MeloongCore')
foreach($name in $names) {
    $dependency=[Reflection.Assembly]::Load($name)
    if ($dependency.GetName().Name -ne $name -or $dependency.Location.Length -ne 0) { throw "Dependency was not resolved from embedded bytes: $name" }
}
Write-Output 'PASS all fifteen managed dependencies resolve from embedded assemblies'
$core=[Reflection.Assembly]::Load('BRCL.Core')
[void]$core.GetType('BRCL.Core.Java',$true)
$resources=$assembly.GetManifestResourceNames()
if ($assembly.GetName().Name -cne 'BRCL' -or 'BRCL.Resources.resources' -cnotin $resources -or
    'BRCL.Packed.BRCL.Core' -cnotin $resources -or 'BRCL.Packed.PCLCS' -cin $resources -or
    $assembly.GetType('PCL.Application',$false)) {
    throw 'The launcher still has a legacy runtime identity or is missing a renamed resource.'
}
Write-Output 'PASS launcher, embedded core and compiled resources use the BRCL runtime names'
$formatting=[Reflection.Assembly]::Load('System.Net.Http.Formatting')
$formatterType=$formatting.GetType('System.Net.Http.Formatting.JsonMediaTypeFormatter',$true)
$formatter=[Activator]::CreateInstance($formatterType)
$serializer=$formatterType.GetProperty('SerializerSettings').GetValue($formatter,$null)
if ($serializer.GetType().Assembly.GetName().Version.Major -ne 13) { throw 'Legacy Newtonsoft reference failed to bind to embedded v13.' }
Write-Output 'PASS legacy Newtonsoft v6 reference uses embedded v13 without binding redirects'
$settingsType=[Net.HttpWebRequest].Assembly.GetType('System.Net.Configuration.SettingsSectionInternal')
$section=$settingsType.GetProperty('Section',[Reflection.BindingFlags]'NonPublic,Static').GetValue($null,$null)
$field=$settingsType.GetField('useUnsafeHeaderParsing',[Reflection.BindingFlags]'NonPublic,Instance')
$original=$field.GetValue($section)
try {
    $field.SetValue($section,$false)
    $method=$appType.GetMethod('ConfigureBundledNetworkSettings',[Reflection.BindingFlags]'NonPublic,Static')
    if (-not $method.Invoke($null,@()) -or -not $field.GetValue($section) -or -not $method.Invoke($null,@())) { throw 'Embedded network configuration failed.' }
} finally { $field.SetValue($section,$original) }
Write-Output 'PASS legacy network configuration is process-local and idempotent without App.config'
$stream=$assembly.GetManifestResourceStream('BRCL.LICENCE')
if ($null -eq $stream) { throw 'Single EXE is missing the license.' }
$memory=New-Object IO.MemoryStream
try { $stream.CopyTo($memory); $bytes=$memory.ToArray() } finally { $stream.Dispose(); $memory.Dispose() }
$expected=[IO.File]::ReadAllBytes((Join-Path $repo 'LICENCE'))
if ([Convert]::ToBase64String($bytes) -ne [Convert]::ToBase64String($expected)) { throw 'Embedded license differs from repository license.' }
Write-Output 'PASS license is embedded byte-for-byte in the EXE'
$stream=$assembly.GetManifestResourceStream('BRCL.BrclInstall.ps1')
$memory=New-Object IO.MemoryStream
try { $stream.CopyTo($memory); $installer=$memory.ToArray() } finally { $stream.Dispose(); $memory.Dispose() }
if ([Convert]::ToBase64String($installer) -ne [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $repo 'Plain Craft Launcher 2\Modules\BrclInstall.ps1')))) { throw 'Embedded installer does not match the current validated installer.' }
Write-Output 'PASS current renamed-EXE updater is embedded byte-for-byte'
foreach($name in @('BRCL.PageLaunchRight','BRCL.PageBRS','BRCL.PageStock','BRCL.PageMap','BRCL.PageSetupChangelog','BRCL.PageOtherAbout')) {
    $page=[Activator]::CreateInstance($assembly.GetType($name,$true))
    if ($null -eq $page) { throw "Cannot create embedded page: $name" }
}
Write-Output 'PASS native pages, web page shells, about and changelog load with no loose DLLs'
if (@(Get-ChildItem -LiteralPath $stage -File).Count -ne 1 -or (Test-Path -LiteralPath ($exe+'.config'))) { throw 'Managed initialization created loose shipping files.' }
Write-Output 'PASS EXE-only fixture still contains exactly one root file'
$legacyHelpDirectory=Join-Path $stage 'legacy-help'
[void][IO.Directory]::CreateDirectory($legacyHelpDirectory)
$legacyHelpJson=Join-Path $legacyHelpDirectory 'legacy.json'
$legacyHelpXaml=Join-Path $legacyHelpDirectory 'legacy.xaml'
$legacyMarkup='<local:MyButton xmlns:local="clr-namespace:PCL;assembly=BRCL" Text="Legacy help" />'
[IO.File]::WriteAllText($legacyHelpJson,'{"Title":"Legacy help"}')
[IO.File]::WriteAllText($legacyHelpXaml,$legacyMarkup)
$entryType=$assembly.GetType('BRCL.ModMain+HelpEntry',$true)
$entry=$entryType.GetConstructor([type[]]@([string])).Invoke([object[]]@([string]$legacyHelpJson))
$detail=[Activator]::CreateInstance($assembly.GetType('BRCL.PageOtherHelpDetail',$true))
if (-not $detail.Init($entry)) { throw 'Existing help markup failed to load.' }
$control=$detail.FindName('PanCustom').Children[0].Children[0]
if ($control.GetType().FullName -cne 'BRCL.MyButton' -or $control.Text -cne 'Legacy help' -or
    [IO.File]::ReadAllText($legacyHelpXaml) -cne $legacyMarkup) {
    throw 'Existing help markup lost its controls or was modified on disk.'
}
Write-Output 'PASS existing help XAML renders through the BRCL namespace without changing the saved file'
$app.Shutdown()
