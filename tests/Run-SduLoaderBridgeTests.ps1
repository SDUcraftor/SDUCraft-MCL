param([string]$DotNetPath = 'dotnet')
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$output = Join-Path $PSScriptRoot ('bin\loader-bridge-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($output) | Out-Null

# Compile the actual LoaderBase/LoaderTask/LoaderCombo source and production
# bridge. Only UI, logging, unused loader kinds and other host services are stubbed.
$loaderFile = Join-Path $repository 'Plain Craft Launcher 2\Modules\Base\ModLoader.vb'
$loaderSource = Get-Content $loaderFile -Raw -Encoding UTF8
$classes = @('LoaderBase','LoaderTask','LoaderCombo') | ForEach-Object {
    $match = [regex]::Match($loaderSource, '(?ms)^    Public (?:MustInherit )?Class ' + $_ + '\b.*?^    End Class')
    if (-not $match.Success) { throw "Loader source not found: $_" }
    $match.Value
}
$stateSource = Get-Content (Join-Path $repository 'Plain Craft Launcher 2\Modules\Base\ModBase.vb') -Raw -Encoding UTF8
$state = [regex]::Match($stateSource,'(?ms)^    Public Enum LoadState\b.*?^    End Enum').Value
if (-not $state) { throw 'LoadState source not found' }
$runtimeSource = 'Public Module ModLoader' + "`r`n" + $state + "`r`n" + ($classes -join "`r`n") + "`r`nEnd Module"
$runtimeFile = Join-Path $output 'ActualLoaders.vb'
[IO.File]::WriteAllText($runtimeFile,$runtimeSource,[Text.UTF8Encoding]::new($false))
$installSource = Get-Content (Join-Path $repository 'Plain Craft Launcher 2\Pages\PageDownload\ModDownloadLib.vb') -Raw -Encoding UTF8
$installState = [regex]::Matches($installSource, '(?ms)^    Public Sub McInstallState\(.*?^    End Sub')
if ($installState.Count -ne 2) { throw 'Expected both production install-state overloads' }
$installStateFile = Join-Path $output 'ActualInstallState.vb'
[IO.File]::WriteAllText($installStateFile, ('Public Module ActualInstallState' + "`r`n" + (($installState | ForEach-Object { $_.Value }) -join "`r`n") + "`r`nEnd Module"), [Text.UTF8Encoding]::new($false))

Push-Location $repository
try {
    $version = (& $DotNetPath --version).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot locate .NET SDK' }
    $sdkLine = @(& $DotNetPath --list-sdks) | Where-Object { $_.StartsWith($version + ' ') } | Select-Object -First 1
    if ($sdkLine -notmatch '\[(.+)\]') { throw "Cannot locate SDK compiler: $version" }
    $compiler = Join-Path $Matches[1] "$version\Roslyn\bincore\vbc.dll"
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
    $vbRuntime = Get-ChildItem (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\Microsoft.VisualBasic') -Filter Microsoft.VisualBasic.dll -Recurse | Select-Object -First 1 -ExpandProperty FullName
    $executable = Join-Path $output 'SduLoaderBridgeTests.exe'
    $arguments = @(
        $compiler, '/nologo', '/noconfig', '/nostdlib', '/target:exe',
        "/out:$executable", "/vbruntime:$vbRuntime", '/optioninfer+', '/optionstrict-',
        '/imports:System,System.Collections.Generic,System.Linq,System.Threading,Microsoft.VisualBasic',
        "/r:$framework\mscorlib.dll", "/r:$framework\System.dll", "/r:$framework\System.Core.dll",
        $runtimeFile,
        $installStateFile,
        (Join-Path $repository 'Plain Craft Launcher 2\Modules\Sdu\SduLoaderBridge.vb'),
        (Join-Path $repository 'Plain Craft Launcher 2\Modules\Sdu\SduInstallOutcome.vb'),
        (Join-Path $PSScriptRoot 'SduLoaderBridgeHost.vb'),
        (Join-Path $PSScriptRoot 'SduInstallStateHost.vb'),
        (Join-Path $PSScriptRoot 'SduLoaderBridgeTests.vb')
    )
    & $DotNetPath @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Loader lifecycle test compilation failed' }
    [IO.File]::Copy((Join-Path $repository 'Plain Craft Launcher 2\App.config'),($executable + '.config'),$true)
    & $executable
    if ($LASTEXITCODE -ne 0) { throw 'Loader lifecycle tests failed' }
} finally {
    Pop-Location
}
