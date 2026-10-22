param([string]$DotNetPath = 'dotnet')
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$output = Join-Path $PSScriptRoot ('bin\scroll-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($output) | Out-Null
Push-Location $repository
try {
    $version = (& $DotNetPath --version).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot locate .NET SDK' }
    $sdkLine = @(& $DotNetPath --list-sdks) | Where-Object { $_.StartsWith($version + ' ') } | Select-Object -First 1
    if ($sdkLine -notmatch '\[(.+)\]') { throw "Cannot locate SDK compiler: $version" }
    $compiler = Join-Path $Matches[1] "$version\Roslyn\bincore\vbc.dll"
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
    $vbRuntime = Get-ChildItem (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\Microsoft.VisualBasic') -Filter Microsoft.VisualBasic.dll -Recurse | Select-Object -First 1 -ExpandProperty FullName
    $executable = Join-Path $output 'SduScrollTests.exe'
    $arguments = @($compiler, '/nologo', '/noconfig', '/nostdlib', '/target:exe', '/rootnamespace:PCL', "/out:$executable", "/vbruntime:$vbRuntime", '/optioninfer+', '/optionstrict-', '/imports:System,System.Collections.Generic,System.Linq,System.Threading,Microsoft.VisualBasic,System.Windows,System.Windows.Controls,System.Windows.Input,System.Windows.Media')
    foreach ($assembly in @('mscorlib','System','System.Core','System.Xaml','System.Xml','System.Runtime.Serialization')) { $arguments += "/r:$framework\$assembly.dll" }
    foreach ($assembly in @('PresentationFramework','PresentationCore','WindowsBase')) { $arguments += "/r:$framework\WPF\$assembly.dll" }
    $arguments += @((Join-Path $repository 'Plain Craft Launcher 2\Controls\MyScrollViewer.vb'), (Join-Path $PSScriptRoot 'SduScrollTests.vb'))
    $json = Join-Path $repository 'Plain Craft Launcher 2\Resources\Newtonsoft.Json.dll'
    $arguments += @("/r:$json", (Join-Path $repository 'Plain Craft Launcher 2\Pages\PageLaunch\PageLaunchRight.SduDownloadPanel.vb'), (Join-Path $repository 'Plain Craft Launcher 2\Modules\Sdu\SduDownloadItem.vb'), (Join-Path $repository 'Plain Craft Launcher 2\Modules\Sdu\SduInstallOutcome.vb'), (Join-Path $PSScriptRoot 'SduPanelTests.vb'))
    $arguments += '/imports:System.Windows.Data'
    & $DotNetPath @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Scroll routing test compilation failed' }
    [IO.File]::Copy($json, (Join-Path $output 'Newtonsoft.Json.dll'), $true)
    [IO.File]::Copy((Join-Path $repository 'Plain Craft Launcher 2\App.config'), ($executable + '.config'), $true)
    # Use production ScrollViewer templates as well as the actual input handler.
    [xml]$application = Get-Content (Join-Path $repository 'Plain Craft Launcher 2\Application.xaml') -Raw -Encoding UTF8
    $namespaces = [Xml.XmlNamespaceManager]::new($application.NameTable)
    $namespaces.AddNamespace('p','http://schemas.microsoft.com/winfx/2006/xaml/presentation')
    $namespaces.AddNamespace('x','http://schemas.microsoft.com/winfx/2006/xaml')
    $resources = $application.SelectNodes('//p:Application.Resources/p:ResourceDictionary/p:SolidColorBrush | //p:Style[@x:Key="MyThumb"] | //p:Style[@TargetType="local:MyScrollBar"] | //p:Style[@TargetType="local:MyScrollViewer"] | //p:Style[@x:Key="ScrollViewerNoMargin"]', $namespaces)
    foreach ($resource in $resources) { if ($resource.LocalName -eq 'Style') { $resource.RemoveAttribute('Name','http://schemas.microsoft.com/winfx/2006/xaml') | Out-Null } }
    $xaml = '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:sys="clr-namespace:System;assembly=mscorlib" xmlns:local="clr-namespace:PCL;assembly=SduScrollTests">' + (($resources | ForEach-Object { $_.OuterXml }) -join "`n") + '</ResourceDictionary>'
    $xaml = $xaml.Replace('xmlns:local="clr-namespace:PCL"', 'xmlns:local="clr-namespace:PCL;assembly=SduScrollTests"')
    $resourcePath = Join-Path $output 'ScrollResources.xaml'
    [IO.File]::WriteAllText($resourcePath,$xaml,[Text.UTF8Encoding]::new($false))
    & $executable $resourcePath
    if ($LASTEXITCODE -ne 0) { throw 'Scroll routing tests failed' }
} finally {
    Pop-Location
}
