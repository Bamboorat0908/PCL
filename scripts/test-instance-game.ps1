# Component tests run in Windows PowerShell 5.1 / STA; no application window or network.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework,System.IO.Compression.FileSystem,System.Net.Http
$project = Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'
[void][Reflection.Assembly]::LoadFrom((Join-Path $project 'Resources\Newtonsoft.Json.dll'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $project 'Resources\ThrottleDebounce.dll'))
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $project 'bin\BRCL.exe'))
function Assert($condition, [string]$message) { if (-not $condition) { throw $message }; Write-Output "PASS $message" }
$model = [Activator]::CreateInstance($assembly.GetType('BRCL.DinoRunnerModel'), [object[]]@(123))
$model.Jump()
$model.Advance(0.1)
Assert ($model.JumpHeight -gt 0 -and -not $model.IsGrounded) 'jump leaves ground'
$height = $model.JumpHeight
$model.Jump()
$model.Advance(0.1)
Assert ($model.JumpHeight -lt 100) 'jump cannot stack in midair'
for ($i=0; $i -lt 8; $i++) { $model.Advance(0.1) }
Assert $model.IsGrounded 'jump lands on ground'
$model.Reset()
$obstacleType = $assembly.GetType('BRCL.DinoRunnerModel+Obstacle')
function New-Obstacle([bool]$bird) {
    $item = [Activator]::CreateInstance($obstacleType)
    $item.X = 65; $item.Width = 24; $item.Height = 32; $item.Y = 144
    if ($bird) { $item.Y=129; $item.Height=20; $item.IsBird=$true }
    return $item
}
$model.Obstacles.Add((New-Obstacle $false))
$model.Advance(0.01)
Assert $model.IsOver 'cactus collision ends round'
$score = $model.Score
$model.Advance(0.1)
Assert ($model.Score -eq $score) 'game over freezes score'
$model.Reset(); $model.Ducking=$true
$model.Obstacles.Add((New-Obstacle $true))
$model.Advance(0.01)
Assert (-not $model.IsOver) 'ducking avoids flying bird'
$model.Reset(); $model.JumpHeight=80
$model.Obstacles.Add((New-Obstacle $false))
$model.Advance(0.01)
Assert (-not $model.IsOver) 'jump clears cactus'
$model.Reset(); $model.Advance(100)
Assert ($model.Elapsed -lt 0.11) 'stalled frames are clamped'
$model.Elapsed=500
Assert ($model.Speed -le 400) 'running speed has an upper bound'
$model.Reset()
Assert ($model.Score -eq 0 -and $model.IsGrounded -and -not $model.IsOver -and $model.Obstacles.Count -eq 0) 'restart resets round'
$root = Join-Path $env:TEMP ('BRCL-ContentTest-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
$store = $assembly.GetType('BRCL.InstanceContentStore')
$kind = $assembly.GetType('BRCL.InstanceContentKind')
function Invoke-Store([string]$method, [object[]]$arguments) {
    $unwrapped = [object[]]@($arguments | ForEach-Object { $_.psobject.BaseObject })
    return $store.GetMethod($method).Invoke($null, $unwrapped)
}
$source = Join-Path $root 'source-world'
New-Item -ItemType Directory -Path $source | Out-Null
[IO.File]::WriteAllText((Join-Path $source 'level.dat'), 'fixture')
$saves = Join-Path $root 'saves'
$saveKind = [Enum]::Parse($kind, 'Saves')
$first = Invoke-Store 'ImportItem' @($saves,$source,$saveKind)
$second = Invoke-Store 'ImportItem' @($saves,$source,$saveKind)
Assert ($first -ne $second -and (Test-Path (Join-Path $first 'level.dat'))) 'duplicate imports preserve existing saves'
$zip = Join-Path $root 'world.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($source, $zip)
$imported = Invoke-Store 'ImportItem' @($saves,$zip,$saveKind)
Assert (Test-Path (Join-Path $imported 'level.dat')) 'save ZIP imports into usable world folder'
$renamed = Invoke-Store 'RenameItem' @($saves,$second,'renamed-world')
Assert ((Test-Path $renamed) -and -not (Test-Path $second)) 'rename operates on the selected item'
$blocked = $false
try { Invoke-Store 'RenameItem' @($saves,$renamed,'..\outside') } catch { $blocked = $true }
Assert $blocked 'rename rejects paths outside content directory'
$badZip = Join-Path $root 'bad.zip'
$archive = [IO.Compression.ZipFile]::Open($badZip, [IO.Compression.ZipArchiveMode]::Create)
$entry = $archive.CreateEntry('../escaped.txt'); $archive.Dispose()
$blocked = $false
try { Invoke-Store 'ImportItem' @($saves,$badZip,$saveKind) } catch { $blocked = $true }
Assert ($blocked -and -not (Test-Path (Join-Path $root 'escaped.txt'))) 'save import rejects ZIP path traversal'
$badWorld = Join-Path $root 'not-a-save'
New-Item -ItemType Directory -Path $badWorld | Out-Null
$blocked = $false
try { Invoke-Store 'ImportItem' @($saves,$badWorld,$saveKind) } catch { $blocked = $true }
Assert $blocked 'save folder requires level.dat'
$nbt = Join-Path $root 'factory.nbt'; [IO.File]::WriteAllText($nbt, 'blueprint fixture')
$schematics = Join-Path $root 'schematics'
$blueprint = Invoke-Store 'ImportItem' @($schematics,$nbt,[Enum]::Parse($kind,'Schematics'))
Assert (Test-Path $blueprint) 'Create NBT imports into schematics'
$filtered = Invoke-Store 'ListEntries' @($saves,'renamed')
Assert ($filtered.Count -eq 1 -and $filtered[0].Name -eq 'renamed-world') 'content search filters by name'
Assert ((Invoke-Store 'ListEntries' @((Join-Path $root 'missing'),'' )).Count -eq 0) 'missing content folder is empty'
foreach ($name in @('Saves','ResourcePacks','Shaders','Screenshots','Schematics')) {
    Assert (-not [string]::IsNullOrEmpty((Invoke-Store 'FolderName' @([Enum]::Parse($kind,$name))))) "$name maps to game directory"
}

# Verify the new BRCL setting path stays scoped to the selected version.
$instanceType = $assembly.GetType('BRCL.ModMinecraft+McInstance')
if (-not $instanceType) { $instanceType = $assembly.GetType('BRCL.McInstance', $true) }
$one = [Activator]::CreateInstance($instanceType,[object[]]@([string](Join-Path $root 'versions\one')))
$two = [Activator]::CreateInstance($instanceType,[object[]]@([string](Join-Path $root 'versions\two')))
$settings = $assembly.GetType('BRCL.Settings')
$settings.GetMethod('Set').Invoke($null,[object[]]@('VersionArgumentTitle','One title',$one))
$settings.GetMethod('Set').Invoke($null,[object[]]@('VersionArgumentTitle','Two title',$two))
Assert (([IO.File]::ReadAllText((Join-Path $one.PathVersion 'BRCL\Setup.ini'))).Contains('One title')) 'BRCL version settings persist to selected version'
Assert (([IO.File]::ReadAllText((Join-Path $two.PathVersion 'BRCL\Setup.ini'))).Contains('Two title')) 'different versions keep separate settings'
Write-Output "Fixtures: $root"
