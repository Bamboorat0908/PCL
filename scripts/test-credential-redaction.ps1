param([string]$ExePath = (Join-Path $PSScriptRoot '..\Plain Craft Launcher 2\bin\BRCL.exe'))
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[Reflection.Assembly]::LoadFrom((Join-Path $repo 'Plain Craft Launcher 2\Resources\Newtonsoft.Json.dll')) | Out-Null
[Reflection.Assembly]::LoadFrom((Join-Path $repo 'Plain Craft Launcher 2\Resources\ThrottleDebounce.dll')) | Out-Null
$assembly = [Reflection.Assembly]::LoadFrom([IO.Path]::GetFullPath($ExePath))
$flags = [Reflection.BindingFlags]'Public,NonPublic,Static'
$methods = @(
    $assembly.GetType('PCL.BrclErrorPresentation', $true).GetMethod('SanitizeBrclDetails', $flags),
    $assembly.GetType('PCL.ModCrashReports', $true).GetMethod('RedactCrashText', $flags)
)
$fixtures = @(
    @{Text='{"password":"private first second","message":"keep detail"}'; Secrets=@('private','first','second')},
    @{Text="{'client_secret': 'private first second', 'message': 'keep detail'}"; Secrets=@('private','first','second')},
    @{Text='--password "private first second" keep detail'; Secrets=@('private','first','second')},
    @{Text="--refresh-token='private first second' keep detail"; Secrets=@('private','first','second')},
    @{Text='{"access_token":"private\"first second","message":"keep detail"}'; Secrets=@('private','first','second')},
    @{Text='Authorization: Basic private-secret'; Secrets=@('private-secret')},
    @{Text='Authorization: Bearer private-secret'; Secrets=@('private-secret')},
    @{Text='https://example.invalid/?api_key=private-secret&message=keep'; Secrets=@('private-secret')},
    @{Text='{"id-token":"private-secret","message":"keep detail"}'; Secrets=@('private-secret')}
)
foreach ($method in $methods) {
    foreach ($fixture in $fixtures) {
        $clean = [string]$method.Invoke($null, [object[]]@($fixture.Text))
        foreach ($secret in $fixture.Secrets) {
            if ($clean.Contains($secret)) { throw "$($method.Name) leaked a credential fragment from fixture: $($fixture.Text)" }
        }
        if ($fixture.Text.Contains('keep detail') -and -not $clean.Contains('keep detail')) { throw "$($method.Name) removed unrelated diagnostic detail." }
    }
    if ($method.Invoke($null, [object[]]@($null)) -ne '') { throw "$($method.Name) does not accept null input." }
    Write-Output "PASS $($method.Name): quoted, escaped, header, CLI and URL credentials are fully removed"
}
