param(
    [string]$InventoryPath = (Join-Path $env:TEMP 'BRCL-source-inventory.json'),
    [string]$PythonPath,
    [string]$NodePath
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$utf8 = New-Object Text.UTF8Encoding($false, $true)
if (-not $PythonPath) {
    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($python) { $PythonPath = $python.Source }
    else { $PythonPath = Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' }
}
if (-not $NodePath) { $NodePath = (Get-Command node -ErrorAction Stop).Source }
if (-not (Test-Path -LiteralPath $PythonPath -PathType Leaf)) { throw 'Python is required for AST and strict JSON validation; pass -PythonPath.' }

# Keep the website's dist directory: it is the maintained public HTML/JS source.
# Exclude generated output and third-party tools without traversing them.
$skipDirectories = @('.git','.vs','bin','obj','node_modules','vendor','packages','__pycache__','logs')
$sourceExtensions = @('.vb','.cs','.xaml','.xml','.json','.ps1','.py','.js','.cjs','.mjs','.html','.css','.props','.targets','.config','.vbproj','.csproj','.sln','.yml','.yaml','.resx','.manifest','.settings','.svg','.service','.example','.txt')
$xmlExtensions = @('.xaml','.xml','.resx','.vbproj','.csproj','.props','.targets','.config','.manifest','.settings','.svg')
$directories = New-Object 'Collections.Generic.Stack[IO.DirectoryInfo]'
$directories.Push((Get-Item -LiteralPath $repo))
$paths = New-Object 'Collections.Generic.List[string]'
while ($directories.Count) {
    $directory = $directories.Pop()
    foreach ($entry in Get-ChildItem -LiteralPath $directory.FullName -Force) {
        if ($entry.PSIsContainer) {
            if ($entry.Name -in $skipDirectories -or ($directory.FullName -eq $repo -and $entry.Name -in @('artifacts','tools','dist','build'))) { continue }
            if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
            $directories.Push($entry)
        } elseif ($entry.Extension.ToLowerInvariant() -in $sourceExtensions -or $entry.Name -eq 'LICENCE') {
            $paths.Add($entry.FullName)
        }
    }
}
$files = New-Object 'Collections.Generic.List[object]'
$byPath = @{}
function Add-Check($item, [string]$kind, [bool]$passed, [string]$detail = '') {
    $item.checks.Add([pscustomobject]@{kind=$kind; passed=$passed; detail=$detail})
}
function Test-SourceReference($item, [string]$reference, [string]$kind) {
    # MSBuild expressions/globs and compiler output are evaluated by the build.
    if ($reference -match '\$\(|[*?]' -or $reference -match '(^|[\\/])(bin|obj)([\\/]|$)') { return }
    $target = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $item.absolutePath) $reference))
    Add-Check $item $kind (Test-Path -LiteralPath $target -PathType Leaf) $reference
}
foreach ($path in ($paths | Sort-Object)) {
    $item = [pscustomobject]@{
        path=$path.Substring($repo.Length + 1).Replace('\','/'); absolutePath=$path
        extension=[IO.Path]::GetExtension($path).ToLowerInvariant(); lineCount=0
        sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        lastWriteTimeUtc=[IO.File]::GetLastWriteTimeUtc($path).ToString('o')
        checks=(New-Object 'Collections.Generic.List[object]')
        reviewCoverage='inventory_only'; manualReview='not_assessed_by_this_script'
    }
    $files.Add($item)
    $byPath[$path] = $item
    try {
        $text = [IO.File]::ReadAllText($path, $utf8)
        $item.lineCount = [IO.File]::ReadAllLines($path, $utf8).Length
        if ($item.extension -eq '.ps1') {
            $tokens = $null; $parseErrors = $null
            $null = [Management.Automation.Language.Parser]::ParseInput($text, $path, [ref]$tokens, [ref]$parseErrors)
            Add-Check $item 'powershell_parser' ($parseErrors.Count -eq 0) (($parseErrors | ForEach-Object { "line $($_.Extent.StartLineNumber): $($_.Message)" }) -join '; ')
        }
        if ($item.extension -in $xmlExtensions) {
            $settings = New-Object Xml.XmlReaderSettings
            $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
            $settings.XmlResolver = $null
            $fragment = $item.path -eq 'Plain Craft Launcher 2/Resources/Custom.xaml'
            $inputText = $null
            if ($fragment) {
                # This shipped custom-page example is intentionally a sequence of
                # cards; runtime custom pages supply this same namespace context.
                $wrapped = '<StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:local="clr-namespace:PCL;assembly=BRCL" xmlns:sys="clr-namespace:System;assembly=mscorlib">' + $text + '</StackPanel>'
                $inputText = New-Object IO.StringReader($wrapped)
                $reader = [Xml.XmlReader]::Create($inputText, $settings)
            } else { $reader = [Xml.XmlReader]::Create($path, $settings) }
            try {
                $document = New-Object Xml.XmlDocument
                $document.XmlResolver = $null
                $document.Load($reader)
            } finally { $reader.Dispose(); if ($inputText) { $inputText.Dispose() } }
            Add-Check $item $(if ($fragment) {'xml_fragment_syntax'} else {'xml_syntax'}) $true
            if ($item.extension -in @('.vbproj','.csproj')) {
                $items = $document.SelectNodes("//*[local-name()='ItemGroup']/*[@Include]")
                foreach ($resource in $items) {
                    if ($resource.LocalName -in @('Compile','Page','Resource','EmbeddedResource','Content','ProjectReference','None')) {
                        Test-SourceReference $item $resource.GetAttribute('Include') 'project_file_reference'
                    }
                }
                foreach ($hint in $document.SelectNodes("//*[local-name()='HintPath']")) { Test-SourceReference $item $hint.InnerText 'assembly_reference' }
            } elseif ($item.extension -eq '.resx') {
                foreach ($resource in $document.SelectNodes("/root/data[contains(@type,'ResXFileRef')]/value")) {
                    Test-SourceReference $item $resource.InnerText.Split(';')[0] 'resx_file_reference'
                }
            }
        }
        if ($item.extension -in @('.js','.cjs','.mjs')) {
            $previousPreference = $ErrorActionPreference
            try { $ErrorActionPreference = 'Continue'; $output = @(& $NodePath --check $path 2>&1 | ForEach-Object { $_.ToString() }); $code = $LASTEXITCODE }
            finally { $ErrorActionPreference = $previousPreference }
            Add-Check $item 'node_syntax' ($code -eq 0) ($output -join [Environment]::NewLine)
        }
    } catch { Add-Check $item 'source_validation' $false $_.Exception.Message }
}

# Parse Python without importing modules, running servers, creating bytecode or
# reading service credentials. JSON rejects NaN/Infinity as nonstandard tokens.
$pythonSource = @'
import ast, json, pathlib, sys, tokenize
results = []
def reject_constant(value):
    raise ValueError('Nonstandard JSON token: ' + value)
for filename in json.load(sys.stdin):
    kind = 'python_ast' if filename.lower().endswith('.py') else 'json_syntax'
    try:
        if kind == 'python_ast':
            with tokenize.open(filename) as stream:
                ast.parse(stream.read(), filename=filename)
        else:
            json.loads(pathlib.Path(filename).read_text(encoding='utf-8-sig'), parse_constant=reject_constant)
        results.append(dict(path=filename, kind=kind, passed=True, detail=''))
    except Exception as error:
        results.append(dict(path=filename, kind=kind, passed=False, detail=str(error)))
print(json.dumps(results, ensure_ascii=True))
'@
$pythonFiles = @($files | Where-Object { $_.extension -in @('.py','.json') } | ForEach-Object absolutePath)
if ($pythonFiles.Count) {
    $json = ConvertTo-Json -InputObject $pythonFiles -Compress
    $rawResults = $json | & $PythonPath -c $pythonSource
    if ($LASTEXITCODE -ne 0) { throw 'Python syntax validation could not run.' }
    foreach ($result in ($rawResults | ConvertFrom-Json)) { Add-Check $byPath[$result.path] $result.kind $result.passed $result.detail }
}
$checkCount = 0; $checkedFiles = 0; $failures = 0
foreach ($item in $files) {
    if ($item.checks.Count) { $checkedFiles++; $item.reviewCoverage = 'static_validation' }
    foreach ($check in $item.checks) {
        $checkCount++
        if (-not $check.passed) { $failures++; Write-Output "FAIL $($item.path) [$($check.kind)]: $($check.detail)" }
    }
}
$report = [ordered]@{
    schemaVersion=1; generatedAtUtc=[DateTime]::UtcNow.ToString('o'); repositoryRoot=$repo
    scope='Source inventory and syntax/reference checks; static validation does not claim manual line-by-line review or functional correctness. VB/C# compilation is checked by the Release build.'
    excluded='Git metadata, bin, obj, third-party tools/vendor/packages, node_modules, bytecode, logs, root artifacts/dist/build and binary assets. Maintained website/brcl/dist text sources are included.'
    tools=[ordered]@{powershell=$PSVersionTable.PSVersion.ToString(); python=$PythonPath; node=$NodePath}
    summary=[ordered]@{sourceFiles=$files.Count; sourceLines=($files | Measure-Object lineCount -Sum).Sum; staticallyCheckedFiles=$checkedFiles; checks=$checkCount; failures=$failures}
    files=$files.ToArray()
}
$InventoryPath = [IO.Path]::GetFullPath($InventoryPath)
$null = [IO.Directory]::CreateDirectory((Split-Path -Parent $InventoryPath))
[IO.File]::WriteAllText($InventoryPath, ($report | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
if ($failures) { throw "$failures source integrity checks failed; inspect $InventoryPath" }
Write-Output "PASS source inventory: $($files.Count) files, $($report.summary.sourceLines) lines, $checkedFiles statically checked files"
Write-Output "PASS $checkCount source syntax and resource reference checks"
Write-Output "Inventory: $InventoryPath"
