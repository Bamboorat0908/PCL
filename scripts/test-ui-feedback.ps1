$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework,System.Net.Http
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'))
foreach ($dependency in @('Newtonsoft.Json','NAudio')) { [void][Reflection.Assembly]::LoadFrom((Join-Path $project "Resources\$dependency.dll")) }
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $project 'bin\BRCL.exe'))
$audio = $assembly.GetType('PCL.BrclAudio',$true)
$cueType = $assembly.GetType('PCL.BrclAudio+UiSoundCue',$true)
$create = $audio.GetMethod('CreateCueSamples',[Reflection.BindingFlags]'NonPublic,Static')
$samplesByName = @{}
foreach ($cue in [Enum]::GetValues($cueType)) {
    $samples = [single[]]$create.Invoke($null,[object[]]@( $cue ))
    if ($samples.Length % 2 -or $samples.Length -lt 7000 -or $samples.Length -gt 65000) { throw "Invalid cue length: $cue" }
    $peak = 0.0
    $jump = 0.0
    for ($i=0;$i -lt $samples.Length;$i+=2) {
        if ([single]::IsNaN($samples[$i]) -or [single]::IsInfinity($samples[$i]) -or $samples[$i] -ne $samples[$i+1]) { throw "Invalid stereo samples: $cue" }
        $peak = [Math]::Max($peak,[Math]::Abs($samples[$i]))
        if ($i -gt 0) { $jump = [Math]::Max($jump,[Math]::Abs($samples[$i]-$samples[$i-2])) }
    }
    if ($peak -lt 0.05 -or $peak -gt 0.85 -or $jump -gt 0.1 -or $samples[0] -ne 0 -or [Math]::Abs($samples[-1]) -gt 0.00001) { throw "Cue clipping, silence or abrupt edges: $cue" }
    $samplesByName[$cue.ToString()] = $samples
    Write-Output "PASS $cue cue duration, stereo, finite samples, headroom and fade edges"
}
$settings = $assembly.GetType('PCL.Settings',$true)
$entries = $settings.GetField('Entries').GetValue($null)
foreach ($key in @('UiSoundEnabled','UiSoundInteraction','UiSoundStartup','UiSoundVolume','UiReduceMotion','SystemDebugAnim')) {
    if (-not $entries.ContainsKey($key)) { throw "Missing preference $key" }
    $entries[$key].Set($entries[$key].DefaultValue,$null)
}
if ($entries['UiSoundVolume'].DefaultValue -ne 35 -or $entries['UiReduceMotion'].DefaultValue -ne $false) { throw 'Unexpected feedback defaults' }
Write-Output 'PASS audio and reduced-motion preference defaults'
$play = $audio.GetMethod('PlayUiSound')
$outputField = $audio.GetField('AudioOutput',[Reflection.BindingFlags]'NonPublic,Static')
foreach ($case in @('master','interaction','startup','zero-volume')) {
    $entries['UiSoundEnabled'].Set($true,$null)
    $entries['UiSoundInteraction'].Set($true,$null)
    $entries['UiSoundStartup'].Set($true,$null)
    $entries['UiSoundVolume'].Set(35,$null)
    $cue = [Enum]::Parse($cueType,'Click')
    switch ($case) {
        master { $entries['UiSoundEnabled'].Set($false,$null) }
        interaction { $entries['UiSoundInteraction'].Set($false,$null) }
        startup { $entries['UiSoundStartup'].Set($false,$null); $cue=[Enum]::Parse($cueType,'Startup') }
        zero-volume { $entries['UiSoundVolume'].Set(0,$null) }
    }
    [void]$play.Invoke($null,[object[]]@( $cue ))
    if ($null -ne $outputField.GetValue($null)) { throw "Muted cue opened an audio device: $case" }
    Write-Output "PASS $case prevents audio initialization and playback"
}
$providerType = $assembly.GetType('PCL.BrclAudio+CueSampleProvider',$true)
$provider = [Activator]::CreateInstance($providerType,[object[]]@($samplesByName['Click'],[single]0.25))
$buffer = New-Object single[] 10000
$first = $provider.Read($buffer,3,9000)
if ($first -ne $samplesByName['Click'].Length -or $buffer[13] -ne [single]($samplesByName['Click'][10]*[single]0.25) -or $provider.Read($buffer,0,1) -ne 0 -or $buffer[0] -ne 0) { throw 'Cue provider overrun, wrong volume or incorrect offset' }
Write-Output 'PASS cached cue provider volume, offset and end-of-stream boundaries'
$revision = $audio.GetField('PreferenceRevision',[Reflection.BindingFlags]'NonPublic,Static')
$before = $revision.GetValue($null)
[void]$audio.GetMethod('UiSoundPreferencesChanged').Invoke($null,@())
if ($revision.GetValue($null) -ne $before+1) { throw 'Preference change did not cancel queued audio' }
Write-Output 'PASS changed preferences invalidate queued cues immediately'
$harnessSource = @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Windows.Threading;
using PCL;
public static class FeedbackAnimationChecks {
    static void PumpUntil(Func<bool> finished) {
        var frame = new DispatcherFrame();
        var clock = Stopwatch.StartNew();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (s,e) => { if (finished() || clock.ElapsedMilliseconds > 2000) frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
        if (!finished()) throw new Exception("Animation callbacks stalled without a visible window");
    }
    public static string[] Run() {
        var passed = new List<string>();
        Settings.Entries["UiReduceMotion"].Set(false,null);
        Settings.Entries["SystemDebugAnim"].Set(9,null);
        bool completed=false; double value=0; int callbacks=0;
        ModAnimation.AniStart(new List<ModAnimation.AniData> {
            ModAnimation.AaDouble(d=>value+=Convert.ToDouble(d),1,80),
            ModAnimation.AaCode(()=>{ callbacks++; completed=true; },0,true)
        },"FeedbackTest");
        ModAnimation.AniStart();
        PumpUntil(()=>completed);
        if(Math.Abs(value-1)>0.00001 || callbacks!=1) throw new Exception("Wrong animation result or duplicate completion");
        passed.Add("PASS frame fallback completes transforms and ordered callbacks once");
        var attached=typeof(ModAnimation).GetField("FrameAttached",BindingFlags.NonPublic|BindingFlags.Static);
        if((bool)attached.GetValue(null)) throw new Exception("Animation frame subscription leaked while idle");
        passed.Add("PASS idle animation engine releases rendering subscription");
        completed=false; int obsolete=0;
        ModAnimation.AniStart(ModAnimation.AaCode(()=>obsolete++,150),"FeedbackReplace");
        ModAnimation.AniStart(ModAnimation.AaCode(()=> {
            ModAnimation.AniStart(ModAnimation.AaCode(()=>completed=true,40),"FeedbackReplace");
        },40),"FeedbackReplace");
        PumpUntil(()=>completed);
        if(obsolete!=0) throw new Exception("Cancelled or reentrant replacement was executed incorrectly");
        passed.Add("PASS rapid same-name replacement and reentrant callback survive");
        Settings.Entries["UiReduceMotion"].Set(true,null);
        completed=false; value=0; var elapsed=Stopwatch.StartNew();
        ModAnimation.AniStart(new List<ModAnimation.AniData> {
            ModAnimation.AaDouble(d=>value+=Convert.ToDouble(d),1,5000,1000),
            ModAnimation.AaCode(()=>completed=true,200)
        },"FeedbackReducedMotion");
        PumpUntil(()=>completed);
        if(Math.Abs(value-1)>0.00001 || elapsed.ElapsedMilliseconds<170 || elapsed.ElapsedMilliseconds>1500) throw new Exception("Reduced motion broke visuals or functional timer");
        passed.Add("PASS reduced motion completes visuals and preserves functional timer");
        Settings.Entries["UiReduceMotion"].Set(false,null);
        return passed.ToArray();
    }
}
'@
$harnessDirectory = Join-Path $env:TEMP ('BRCL-Feedback-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $harnessDirectory | Out-Null
$sourceFile = Join-Path $harnessDirectory 'FeedbackAnimationChecks.cs'
$harnessFile = Join-Path $harnessDirectory 'FeedbackAnimationChecks.dll'
[IO.File]::WriteAllText($sourceFile,$harnessSource)
$compilerArguments = @('/nologo','/target:library',"/out:$harnessFile",('/reference:'+$assembly.Location),('/reference:'+([Windows.Application].Assembly.Location)),('/reference:'+([Windows.Media.Visual].Assembly.Location)),('/reference:'+([Windows.Threading.Dispatcher].Assembly.Location)),('/reference:'+(Join-Path $project 'bin\MeloongCore.dll')),$sourceFile)
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" @compilerArguments
if ($LASTEXITCODE -ne 0) { throw 'Animation harness compilation failed' }
[void][Reflection.Assembly]::LoadFrom($harnessFile)
[FeedbackAnimationChecks]::Run() | Write-Output
