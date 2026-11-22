param(
    [string]$PackArchive,
    [string]$PythonPath,
    [string]$NodePath,
    [string]$OutputDirectory = (Join-Path $env:TEMP ('BRCL-Checks-' + (Get-Date -Format 'yyyyMMdd-HHmmss')))
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$powershell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
if (-not $NodePath) {
    $NodePath = (Get-Command node -ErrorAction SilentlyContinue).Source
    if (-not $NodePath) { $NodePath = Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe' }
}
if (-not $PythonPath) {
    $PythonPath = (Get-Command python -ErrorAction SilentlyContinue).Source
    if (-not $PythonPath) { $PythonPath = Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' }
}
$exe = Join-Path $repo 'Plain Craft Launcher 2\bin\BRCL.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Build Release before running the checks.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$cases = @(
    @{Name='instance-game'; File='test-instance-game.ps1'; Args=@()},
    @{Name='instance-backup'; File='test-instance-backup.ps1'; Args=@()},
    @{Name='directory-safety'; File='test-directory-safety.ps1'; Args=@()},
    @{Name='home'; File='test-home-data.ps1'; Args=@()},
    @{Name='home-calendar'; File='test-home-calendar.ps1'; Args=@('-PreviewDirectory',(Join-Path $OutputDirectory 'home-calendar'))},
    @{Name='daily-fortune'; File='test-daily-fortune.ps1'; Args=@('-OutputDirectory',(Join-Path $OutputDirectory 'daily-fortune'))},
    @{Name='bank-page'; File='test-bank-page.ps1'; Args=@('-PreviewDirectory',(Join-Path $OutputDirectory 'bank'))},
    @{Name='sponsor-dialog'; File='test-sponsor-dialog.ps1'; Args=@('-PreviewDirectory',(Join-Path $OutputDirectory 'sponsor-dialog'))},
    @{Name='pack-layout'; File='test-brs-pack-layout.ps1'; Args=@('-OutputDirectory',(Join-Path $OutputDirectory 'pack-previews'))},
    @{Name='brs-launch-selection'; File='test-brs-launch-selection.ps1'; Args=@()},
    @{Name='brs'; File='test-brs-data.ps1'; Args=@()},
    @{Name='brs-dashboard'; File='test-brs-dashboard.ps1'; Args=@()},
    @{Name='brs-page'; File='test-brs-page.ps1'; Args=@()},
    @{Name='news-page'; File='test-news-page.ps1'; Args=@('-PreviewDirectory',(Join-Path $OutputDirectory 'news-previews'))},
    @{Name='global-backdrops'; File='test-global-backdrops.ps1'; Args=@('-PreviewDirectory',(Join-Path $OutputDirectory 'backdrops'))},
    @{Name='sponsors'; File='test-sponsors.ps1'; Args=@()},
    @{Name='errors'; File='test-brcl-errors.ps1'; Args=@('-OutputDirectory','bin')},
    @{Name='credential-redaction'; File='test-credential-redaction.ps1'; Args=@()},
    @{Name='privacy-offline'; File='test-crash-reports.ps1'; Args=@()},
    @{Name='webview-bundle'; File='test-webview-bundle.ps1'; Args=@()},
    @{Name='webview-lifecycle'; File='test-webview-lifecycle.ps1'; Args=@()},
    @{Name='assets'; File='test-industrial-assets.ps1'; Args=@()},
    @{Name='oauth-config'; File='test-ms-client-id.ps1'; Args=@()},
    @{Name='oauth-flow'; File='test-microsoft-login.ps1'; Args=@()},
    @{Name='oauth-performance'; File='test-ms-login-performance.ps1'; Args=@()},
    @{Name='network-response'; File='test-network-response.ps1'; Args=@()},
    @{Name='download-recovery'; File='test-download-recovery.ps1'; Args=@()},
    @{Name='native-extraction'; File='test-native-extraction.ps1'; Args=@()},
    @{Name='library-paths'; File='test-library-paths.ps1'; Args=@()},
    @{Name='loader-input'; File='test-loader-input.ps1'; Args=@()},
    @{Name='launch-patch'; File='test-launch-patch.ps1'; Args=@()},
    @{Name='java-management'; File='test-java-management.ps1'; Args=@()},
    @{Name='button-keyboard'; File='test-button-keyboard.ps1'; Args=@()},
    @{Name='control-cancelled-input'; File='test-control-cancelled-input.ps1'; Args=@()},
    @{Name='control-accessibility'; File='test-control-accessibility.ps1'; Args=@()},
    @{Name='setting-toggles'; File='test-setting-toggles.ps1'; Args=@()},
    @{Name='ui-polish'; File='test-ui-polish.ps1'; Args=@('-PreviewDirectory',(Join-Path $OutputDirectory 'ui-polish'))},
    @{Name='dialog-layout'; File='test-dialog-layout.ps1'; Args=@()},
    @{Name='update-rollback'; File='test-update.ps1'; Args=@()},
    @{Name='icon-theme'; File='test-icon-theme.ps1'; Args=@()},
    @{Name='exe-icon'; File='test-single-exe-icon.ps1'; Args=@('-ExePath',$exe)},
    @{Name='stock-resource'; File='test-stock-resource.ps1'; Args=@()}
    @{Name='ui-feedback'; File='test-ui-feedback.ps1'; Args=@()}
    @{Name='ui-performance'; File='test-ui-performance.ps1'; Args=@()}
    @{Name='startup-updates'; File='test-startup-updates.ps1'; Args=@()}
    @{Name='startup-navigation'; File='test-startup-navigation.ps1'; Args=@()}
    @{Name='startup-arguments'; File='test-startup-arguments.ps1'; Args=@()}
    @{Name='title-navigation'; File='test-title-navigation.ps1'; Args=@('-OutputDirectory','bin','-PreviewDirectory',(Join-Path $OutputDirectory 'title-previews'))}
    @{Name='page-cache-performance'; File='test-page-cache-performance.ps1'; Args=@()}
    @{Name='window-maximize'; File='test-window-maximize.ps1'; Args=@()}
    @{Name='single-exe'; File='test-single-exe.ps1'; Args=@()}
    @{Name='source-integrity'; File='test-source-integrity.ps1'; Args=@('-InventoryPath',(Join-Path $OutputDirectory 'source-inventory.json'),'-PythonPath',$PythonPath,'-NodePath',$NodePath)}
)
if ($PackArchive) { $cases += @{Name='pack'; File='test-brs-pack.ps1'; Args=@('-Archive',[IO.Path]::GetFullPath($PackArchive))} }
$results = @()
foreach ($case in $cases) {
    $script = Join-Path $PSScriptRoot $case.File
    $arguments = @('-NoProfile','-STA','-ExecutionPolicy','Bypass','-File',$script) + $case.Args
    # Windows PowerShell turns native stderr into error records. Preserve those
    # records in the report and continue to the remaining groups after a failure.
    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& $powershell @arguments 2>&1 | ForEach-Object { $_.ToString() })
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previousPreference }
    $count = @($output | Where-Object { $_ -match '^PASS(?:\s|:)' }).Count
    $output | Set-Content -LiteralPath (Join-Path $OutputDirectory ($case.Name + '.txt')) -Encoding UTF8
    $results += [pscustomobject]@{Name=$case.Name; ExitCode=$code; PassedAssertions=$count}
    Write-Output "$($case.Name): exit $code, $count passed checks"
}
$runtimeCases = @(
    @{Name='stock-theme'; Runtime='node'; File='test-stock-theme.cjs'; Args=@()},
    @{Name='news-api'; Runtime='python'; File='news-server\test_server.py'; Args=@('-v')},
    @{Name='update-http'; Runtime='python'; File='update-server\test_ranges.py'; Args=@('-v')}
    @{Name='bank-service'; Runtime='python'; File='..\services\bank\test_bank_service.py'; Args=@('-v')}
    @{Name='ink-admin'; Runtime='python'; File=''; Args=@('-B','-m','unittest','discover','-s',(Join-Path $repo 'InkAdmin'),'-v')}
)
foreach ($case in $runtimeCases) {
    $runtime = if ($case.Runtime -eq 'node') { $NodePath } else { $PythonPath }
    [string[]]$arguments = @(if ($case.File) { Join-Path $PSScriptRoot $case.File }; $case.Args)
    $previousPreference = $ErrorActionPreference
    try {
        if (-not $runtime -or -not (Test-Path -LiteralPath $runtime -PathType Leaf)) {
            $output = @("Required runtime missing: $($case.Runtime). Pass -NodePath or -PythonPath.")
            $code = 1
        } else {
            $ErrorActionPreference = 'Continue'
            $output = @(& $runtime @arguments 2>&1 | ForEach-Object { $_.ToString() })
            $code = $LASTEXITCODE
        }
    } finally { $ErrorActionPreference = $previousPreference }
    $count = @($output | Where-Object { $_ -match '^PASS(?:\s|:)|^test_.* \.\.\. ok$' }).Count
    $output | Set-Content -LiteralPath (Join-Path $OutputDirectory ($case.Name + '.txt')) -Encoding UTF8
    $results += [pscustomobject]@{Name=$case.Name; ExitCode=$code; PassedAssertions=$count}
    Write-Output "$($case.Name): exit $code, $count passed checks"
}
$results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $OutputDirectory 'results.json') -Encoding UTF8
if (@($results | Where-Object ExitCode -ne 0).Count) { throw "Checks failed; inspect $OutputDirectory" }
$total = ($results | Measure-Object -Property PassedAssertions -Sum).Sum
Write-Output "$($results.Count) groups passed, $total reported checks. Results: $OutputDirectory"
