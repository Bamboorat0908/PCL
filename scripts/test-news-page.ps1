# Native news integration and offscreen visual checks. Run in Windows PowerShell 5.1, STA.
param([string]$BuildDirectory, [string]$PreviewDirectory='C:\works\BRCL\news-20261113-checks')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Net.Http
$project=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\Plain Craft Launcher 2'))
if (-not $BuildDirectory) { $BuildDirectory=Join-Path $project 'bin' }
[void][Reflection.Assembly]::LoadFrom((Join-Path $project 'Resources\Newtonsoft.Json.dll'))
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $BuildDirectory 'BRCL.exe'))
[Windows.Application]::ResourceAssembly=$assembly
$app=[Activator]::CreateInstance($assembly.GetType('PCL.Application',$true),$true)
$app.InitializeComponent()
$assembly.GetType('PCL.ModAnimation',$true).GetProperty('AniControlEnabled').SetValue($null,1,$null)
$type=$assembly.GetType('PCL.PageNews',$true)
$instanceFlags=[Reflection.BindingFlags]'NonPublic,Instance'
$staticFlags=[Reflection.BindingFlags]'NonPublic,Static'
$page=[Activator]::CreateInstance($type)
$apply=$type.GetMethod('ApplyData',$instanceFlags)
$parse=$type.GetMethod('ParseData',$staticFlags)
$showDetail=$type.GetMethod('ShowDetail',$instanceFlags)
$showList=$type.GetMethod('ShowList',$instanceFlags)
$applyError=$type.GetMethod('ApplyError',$instanceFlags)
$entriesField=$type.GetField('Entries',$instanceFlags)

function Parse-Data([string]$json) {
    $data=[Newtonsoft.Json.Linq.JObject]::Parse($json)
    return ,$parse.Invoke($null,[object[]](,$data))
}
function Set-Data([string]$json,[bool]$append=$false) {
    $data=[Newtonsoft.Json.Linq.JObject]::Parse($json)
    [void]$apply.Invoke($page,[object[]]@($data,$append))
}
function Assert-Text([string]$name,[string]$expected) {
    if ($page.FindName($name).Text -ne $expected) { throw "$name expected '$expected', got '$($page.FindName($name).Text)'" }
}
function Assert-Visible([string]$name,[bool]$visible) {
    $expected=if($visible){[Windows.Visibility]::Visible}else{[Windows.Visibility]::Collapsed}
    if ($page.FindName($name).Visibility -ne $expected) { throw "$name expected $expected" }
}
function Assert-Rejected([string]$json) {
    try { $null=Parse-Data $json } catch {
        $detail=$_.Exception
        while($detail.InnerException){$detail=$detail.InnerException}
        if($detail -is [FormatException]){return}
        throw
    }
    throw "Invalid schema/pagination was accepted: $json"
}
$initial=@'
{"items":[{"id":"first","title":"周目维护通知：为下一段旅程做好准备","category":"announcement","summary":"这是界面测试内容。维护时间、服务器变更与注意事项可在公告中完整阅读。","content":"这是界面测试内容。\n\n第一段：服务器公告正文，保留段落。\n第二段：<Button Command=\"example\">仅作为文本显示</Button>。","published_at":"2026-11-13T03:00:00Z","updated_at":"2026-11-13T04:10:00Z","pinned":true},{"id":2,"title":"方块世界的新进展","category":"update","summary":"这是界面测试内容。记录玩家共建的点滴，也把新的进展带给每一位伙伴。","content":"这是界面测试内容。\n一则社区动态。","published_at":"2026-11-12T13:00:00Z","updated_at":"2026-11-12T13:00:00Z","pinned":false}],"total":3,"has_more":true,"next_offset":30}
'@
$parsed=Parse-Data $initial
if($parsed.Items.Count -ne 2 -or $parsed.Items[0].Category -ne 'announcement' -or $parsed.Items[1].Category -ne 'update' -or -not $parsed.Items[0].Pinned){throw 'Published identities/categories/pinned metadata lost'}
if($parsed.Items[0].PublishedAt.UtcDateTime.Hour -ne 3 -or $parsed.NextOffset -ne 30 -or -not $parsed.HasMore){throw 'UTC date or paging metadata lost'}
foreach($bad in @('{}','{"items":null}','{"items":{},"has_more":false}','{"items":[],"has_more":true,"next_offset":30}','{"items":[{}],"has_more":true,"next_offset":0}')){Assert-Rejected $bad}
$malformed=Parse-Data '{"items":[null,false,{}, {"id":"x","title":" ","category":"announcement"}, {"id":"x","title":"bad","category":"draft"},{"id":"good","title":"valid","category":"update","published_at":"invalid"}],"total":1,"has_more":false}'
if($malformed.Items.Count -ne 1 -or $malformed.Items[0].PublishedAt -ne [DateTimeOffset]::MinValue){throw 'Malformed rows or optional dates were not isolated'}
Write-Output 'PASS schema validation, independent malformed rows, UTC dates and paging metadata'

Set-Data $initial
if($page.FindName('PanItems').Children.Count -ne 2){throw 'Native list lost article rows'}
Assert-Visible BtnLoadMore $true
Assert-Visible CardEmpty $false
Assert-Text LabCount '03 POSTS'
if($page.FindName('BtnAnnouncements').Tag -ne 'announcement' -or $page.FindName('BtnUpdates').Tag -ne 'update' -or $page.FindName('BtnAll').Tag -ne ''){throw 'Filter tags do not match server category contract'}
$entry=$entriesField.GetValue($page)[0]
[void]$showDetail.Invoke($page,[object[]]@($entry,$false))
Assert-Text LabDetailTitle $entry.Title
Assert-Text LabDetailContent $entry.Content
if($page.FindName('LabDetailContent').Text -notmatch '<Button' -or $page.FindName('LabDetailContent').Inlines.Count -ne 1){throw 'Remote body was interpreted or lost plain text paragraphs'}
Assert-Visible CardDetail $true
Assert-Visible PanList $false
[void]$type.GetMethod('Back_Click',$instanceFlags).Invoke($page,@())
Assert-Visible PanList $true
Assert-Visible CardDetail $false
if($type.GetField('LastReadId',$instanceFlags).GetValue($page) -ne $entry.Id -or $type.GetField('ReadButtons',$instanceFlags).GetValue($page)[$entry.Id].Text -ne '再次阅读'){throw 'Returning from reading lost the last article context'}
Write-Output 'PASS native category channels, plain text full reading and direct return'

# A valid extreme JSON date must not overflow when displaying update metadata.
Set-Data '{"items":[{"id":"max-date","title":"日期边界","category":"announcement","content":"boundary","published_at":"9999-12-31T23:59:59+00:00","updated_at":"9999-12-31T23:59:59+00:00"}],"total":1,"has_more":false}'
$entry=$entriesField.GetValue($page)[0]
[void]$showDetail.Invoke($page,[object[]]@($entry,$false))
Assert-Visible CardDetail $true
Write-Output 'PASS extreme publication dates display without DateTimeOffset arithmetic overflow'
Set-Data $initial

Set-Data '{"items":[{"id":"first","title":"修订后的公告标题","category":"announcement","content":"revised"},{"id":"third","title":"第三则动态","category":"update","content":"new"}],"total":3,"has_more":false,"next_offset":null}' $true
if($entriesField.GetValue($page).Count -ne 3 -or $entriesField.GetValue($page)[0].Title -ne '修订后的公告标题'){throw 'Appending did not deduplicate and update a changed article'}
Assert-Visible BtnLoadMore $false
$entry=$entriesField.GetValue($page)[0]
[void]$showDetail.Invoke($page,[object[]]@($entry,$false))
Set-Data '{"items":[{"id":"remaining","title":"保留的动态","category":"update","content":"remaining"}],"total":1,"has_more":false}'
Assert-Visible CardDetail $false
Assert-Visible PanList $true
if($page.FindName('PanItems').Children.Count -ne 1){throw 'Refresh retained a withdrawn article'}
[void]$applyError.Invoke($page,[object[]]@($false))
Assert-Visible CardError $true
if($page.FindName('PanItems').Children.Count -ne 1 -or $page.FindName('LabStatus').Text -notmatch '上次'){throw 'Failure discarded useful data or presented it as current'}
Set-Data '{"items":[],"total":0,"has_more":false}'
Assert-Visible CardError $false
Assert-Visible CardEmpty $true
Assert-Text LabCount '00 POSTS'
$type.GetField('SelectedCategory',$instanceFlags).SetValue($page,'announcement')
Set-Data '{"items":[],"total":0,"has_more":false}'
Assert-Text LabChannel '服务器公告'
Assert-Text LabEmptyTitle '还没有公告'
Write-Output 'PASS append deduplication, withdrawn detail removal, failure snapshot and category empty state'

if($app.Windows.Count -ne 0 -or $page.IsLoaded){throw 'Integration checks unexpectedly showed a window or started network I/O'}

function Set-Theme([string]$theme) {
    $colors=if($theme -eq 'dark'){@('#F1F2E9','#B3BAB9','#F2D649','#272C2E','#1C2022','#111416','#374041')}else{@('#191D1F','#555F60','#745900','#E8EAE1','#F5F5EF','#E7E8E3','#C6CBC7')}
    $keys=@('ColorBrush1','ColorBrush2','ColorBrush3','ColorBrush6','ColorBrush7','ColorBrush8','ColorBrushCardLine')
    for($i=0;$i -lt $keys.Count;$i++){$app.Resources[$keys[$i]]=[Windows.Media.BrushConverter]::new().ConvertFromString($colors[$i])}
    $app.Resources['ColorBrushRail']=[Windows.Media.BrushConverter]::new().ConvertFromString('#14181A')
}
function Render-Page([int]$width,[string]$name) {
    $height=1000
    $hostBorder=[Windows.Controls.Border]::new()
    $hostBorder.Background=$app.Resources['ColorBrush8']
    $hostBorder.Child=$page
    $hostBorder.Measure([Windows.Size]::new($width,$height))
    $hostBorder.Arrange([Windows.Rect]::new(0,0,$width,$height))
    $hostBorder.UpdateLayout()
    $artworkExpected=if($width -lt 500 -or $page.FindName('CardDetail').Visibility -eq [Windows.Visibility]::Visible){[Windows.Visibility]::Collapsed}else{[Windows.Visibility]::Visible}
    if($page.FindName('NewsArtwork').Visibility -ne $artworkExpected){throw "Hero artwork did not adapt at ${width}px"}
    $masthead=$page.FindName('CardMasthead')
    $maximumHeader=if($page.FindName('CardDetail').Visibility -eq [Windows.Visibility]::Visible){130}else{195}
    if($masthead.ActualHeight -gt $maximumHeader){throw "News header takes too much reading space: $($masthead.ActualHeight) at ${width}px"}
    $hostBorder.Measure([Windows.Size]::new($width,$height))
    $hostBorder.Arrange([Windows.Rect]::new(0,0,$width,$height))
    $hostBorder.UpdateLayout()
    foreach($controlName in @('BtnRefresh','BtnAll','BtnAnnouncements','BtnUpdates')){
        $control=$page.FindName($controlName)
        if($control.Visibility -eq [Windows.Visibility]::Visible -and $control.ActualWidth -gt 0){
            $origin=$control.TranslatePoint([Windows.Point]::new(0,0),$page)
            if($origin.X -lt -1 -or $origin.X+$control.ActualWidth -gt $width+1){throw "$controlName clips at ${width}px"}
        }
    }
    $scroll=$page.FindName('ScrollMain')
    if($scroll.ExtentWidth -gt $scroll.ViewportWidth+1){throw "News requires horizontal scrolling at ${width}px"}
    $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new($width,$height,96,96,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($hostBorder)
    $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream=[IO.File]::Create((Join-Path $PreviewDirectory ($name+'.png')))
    try{$encoder.Save($stream)}finally{$stream.Dispose()}
    $hostBorder.Child=$null
    Write-Output "RENDER $name : ${width}x$height"
}

New-Item -ItemType Directory -Force -Path $PreviewDirectory | Out-Null
$type.GetField('SelectedCategory',$instanceFlags).SetValue($page,'')
foreach($theme in @('dark','light')){
    Set-Theme $theme
    Set-Data $initial
    foreach($width in @(380,830)){Render-Page $width "news-list-$theme-$width"}
    $entry=$entriesField.GetValue($page)[0]
    [void]$showDetail.Invoke($page,[object[]]@($entry,$false))
    foreach($width in @(380,830)){Render-Page $width "news-detail-$theme-$width"}
    [void]$showList.Invoke($page,@())
    Set-Data '{"items":[],"total":0,"has_more":false}'
    Render-Page 380 "news-empty-$theme-380"
}
if($app.Windows.Count -ne 0 -or $page.IsLoaded){throw 'Offscreen renders unexpectedly mounted a live page'}
Write-Output 'PASS narrow/wide dark/light layouts and empty state render without a visible window or network request'
$app.Shutdown()
