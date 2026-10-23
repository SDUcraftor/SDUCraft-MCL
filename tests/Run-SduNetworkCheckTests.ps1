param([string]$DotNetPath = 'dotnet')
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$output = Join-Path $PSScriptRoot ('bin\network-check-' + [Guid]::NewGuid().ToString('N'))
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
    $executable = Join-Path $output 'SduNetworkCheckTests.exe'
    $arguments = @(
        $compiler, '/nologo', '/noconfig', '/nostdlib', '/target:exe',
        "/out:$executable", "/vbruntime:$vbRuntime", '/optioninfer+', '/optionstrict+',
        '/imports:System,System.Collections.Generic,System.Linq,System.Threading,Microsoft.VisualBasic',
        "/r:$framework\mscorlib.dll", "/r:$framework\System.dll", "/r:$framework\System.Core.dll", "/r:$framework\System.Net.Http.dll",
        (Join-Path $repository 'Plain Craft Launcher 2\Modules\Sdu\SduNetworkCheck.vb'),
        (Join-Path $PSScriptRoot 'SduNetworkCheckTests.vb')
    )
    & $DotNetPath @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Network-check test compilation failed' }
    [IO.File]::Copy((Join-Path $repository 'Plain Craft Launcher 2\App.config'), ($executable + '.config'), $true)
    & $executable
    if ($LASTEXITCODE -ne 0) { throw 'Network-check tests failed' }
} finally {
    Pop-Location
}
