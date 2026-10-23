param(
    [Parameter(Mandatory)][string]$Executable,
    [string]$OutputDirectory = '',
    [switch]$StaWorker
)
$ErrorActionPreference = 'Stop'
$executablePath = [IO.Path]::GetFullPath($Executable)
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot ('bin\settings-layout-' + [Guid]::NewGuid().ToString('N')) }
if (-not $StaWorker) {
    & powershell.exe -NoProfile -STA -File $PSCommandPath -Executable $executablePath -OutputDirectory $OutputDirectory -StaWorker
    if ($LASTEXITCODE -ne 0) { throw 'Compiled settings/help layout regression failed' }
    return
}
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase
$assembly = [Reflection.Assembly]::LoadFrom($executablePath)
$app = [Activator]::CreateInstance($assembly.GetType('PCL.Application',$true),$true)
$app.InitializeComponent()
# Prevent settings controls from persisting initialization values. No main window or Loaded events are run.
$animation = $assembly.GetType('PCL.ModAnimation',$true)
$animation::AniControlEnabled = 1
$service = $assembly.GetType('PCL.SettingService',$true)
function Get-Tree($element) {
    Write-Output -NoEnumerate $element
    foreach ($child in [Windows.LogicalTreeHelper]::GetChildren($element)) {
        if ($child -is [Windows.DependencyObject]) { Get-Tree $child }
    }
}
function Get-RowHeight($element) {
    $child = $element
    while ($child.Parent) {
        $parent = $child.Parent
        if ($parent -is [Windows.Controls.Grid] -and $parent.RowDefinitions.Count -gt 0) {
            $index = [Windows.Controls.Grid]::GetRow($child)
            if ($index -ge $parent.RowDefinitions.Count) { throw 'Control refers to a missing grid row' }
            return $parent.RowDefinitions[$index].ActualHeight
        }
        $child = $parent
    }
    throw 'Cannot find an allocated settings row'
}
function Arrange-Page($page, [int]$width) {
    foreach ($node in @(Get-Tree $page)) { if ($node.GetType().Name -eq 'MyCard') { $null = $node.MainTextBlock } }
    $page.Measure([Windows.Size]::new($width,720))
    $page.Arrange([Windows.Rect]::new(0,0,$width,720))
    $page.UpdateLayout()
}
function Save-Preview($page, [string]$name, [double]$scale = 1) {
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new([int](1000*$scale),[int](720*$scale),96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($page)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.File]::Create((Join-Path $OutputDirectory ($name+'.png')))
    try { $encoder.Save($stream) } finally { $stream.Dispose() }
}
foreach ($case in @(
    @{Type='PageSetupLaunch'; Keys=@('LaunchArgumentTitle','LaunchArgumentInfo','LaunchArgumentVisible','LaunchArgumentPriority','LaunchArgumentWindowType')},
    @{Type='PageInstanceSetup'; Keys=@('VersionArgumentTitle','VersionArgumentInfo','VersionArgumentJavaV2')}
)) {
    $page = $assembly.CreateInstance('PCL.'+$case.Type)
    $page.InitializeComponent()
    $controls = @{}
    foreach ($node in @(Get-Tree $page)) {
        $key = $service::GetKey($node)
        if ($case.Keys -contains $key) { $controls[$key] = $node }
    }
    foreach ($width in @(640,1000)) {
        Arrange-Page $page $width
        foreach ($key in $case.Keys) {
            $control = $controls[$key]
            if (-not $control -or $control.ActualHeight -lt 24 -or $control.ActualWidth -le 0 -or (Get-RowHeight $control) -lt 28) {
                throw ('Clipped settings control at width '+$width+': '+$key)
            }
        }
    }
    Save-Preview $page $case.Type
    Save-Preview $page ($case.Type+'-150dpi') 1.5
    Write-Output ('PASS compiled '+$case.Type+': all input rows visible at 640/1000 DIP, 100/150 percent render')
}
$help = $assembly.CreateInstance('PCL.PageOtherHelp')
Arrange-Page $help 1000
if ($help.FindName('SearchBox') -or $help.FindName('PanList') -or $help.FindName('PanEmpty')) { throw 'Removed guides/search remain in help' }
$tree = @(Get-Tree $help)
if (@($tree | Where-Object {$_.GetType().Name -eq 'MyCard'}).Count -ne 1) { throw 'Help still contains guide cards' }
$links = @($tree | Where-Object {$_.GetType().Name -eq 'MyButton'})
if ($links.Count -ne 4) { throw 'Help website links changed unexpectedly' }
foreach ($link in $links) { if ($link.Tag -notmatch '^https://(www\.sducraft\.top|skin\.sducraft\.cn)/') { throw 'Unexpected help destination' } }
$help.Refresh()
Save-Preview $help 'Help'
Write-Output 'PASS compiled help: guide cards/search removed; four website links retained'
