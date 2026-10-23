param([string]$DotNetPath = 'dotnet')
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$output = Join-Path $PSScriptRoot ('bin\startup-notice-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($output) | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$vbRuntime = Get-ChildItem (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\Microsoft.VisualBasic') -Filter Microsoft.VisualBasic.dll -Recurse | Select-Object -First 1 -ExpandProperty FullName
$executable = Join-Path $output 'SduStartupNoticeTests.exe'
& "$framework\vbc.exe" /nologo /noconfig /nostdlib /target:exe /optioninfer+ /optionstrict+ `
    '/imports:System,System.Collections.Generic,System.Linq,Microsoft.VisualBasic' `
    "/out:$executable" "/vbruntime:$vbRuntime" "/r:$framework\mscorlib.dll" "/r:$framework\System.dll" "/r:$framework\System.Core.dll" `
    (Join-Path $repository 'Plain Craft Launcher 2\Modules\Sdu\SduStartupNotice.vb') (Join-Path $PSScriptRoot 'SduStartupNoticeTests.vb')
if ($LASTEXITCODE -ne 0) { throw 'Startup notice regression compilation failed' }
$settings = [IO.File]::ReadAllText((Join-Path $repository 'Plain Craft Launcher 2\Pages\PageSetup\Settings.vb'))
if (-not $settings.Contains('New Setting("SduIntroShown", False, Source:=Sources.Registry)')) { throw 'Notice preference must persist per user in registry' }
$main = [IO.File]::ReadAllText((Join-Path $repository 'Plain Craft Launcher 2\FormMain.xaml.vb'))
if (-not $main.Contains('SduStartupNotice.ShowOnce()') -or $main.Contains('"SDUcraft 改版说明", "我知道了"')) { throw 'Main window bypasses once-only notice policy' }
& $executable
if ($LASTEXITCODE -ne 0) { throw 'Startup notice regression failed' }
