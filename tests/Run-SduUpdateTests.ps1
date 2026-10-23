param([string]$DotNetPath = 'dotnet')
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$output = Join-Path $PSScriptRoot ('bin\update-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($output) | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$vbRuntime = Get-ChildItem (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\Microsoft.VisualBasic') -Filter Microsoft.VisualBasic.dll -Recurse | Select-Object -First 1 -ExpandProperty FullName
$json = Join-Path $repository 'Plain Craft Launcher 2\Resources\Newtonsoft.Json.dll'
$executable = Join-Path $output 'SduUpdateTests.exe'
& "$framework\vbc.exe" /nologo /noconfig /nostdlib /target:exe /optioninfer+ /optionstrict+ `
    '/imports:System,System.Collections.Generic,System.Linq,Microsoft.VisualBasic' `
    "/out:$executable" "/vbruntime:$vbRuntime" "/r:$framework\mscorlib.dll" "/r:$framework\System.dll" `
    "/r:$framework\System.Core.dll" "/r:$framework\System.Xml.dll" "/r:$framework\System.Net.Http.dll" "/r:$json" `
    (Join-Path $repository 'Plain Craft Launcher 2\Modules\Sdu\SduUpdateCore.vb') (Join-Path $PSScriptRoot 'SduUpdateTests.vb')
if ($LASTEXITCODE -ne 0) { throw 'Update regression compilation failed' }
Copy-Item -LiteralPath $json -Destination $output
Copy-Item -LiteralPath (Join-Path $repository 'Plain Craft Launcher 2\App.config') -Destination ($executable + '.config')
& $executable
if ($LASTEXITCODE -ne 0) { throw 'Update lifecycle regression failed' }

# Exercise the exact publishing scripts in a disposable repository fixture.
$fixture = Join-Path $output 'release-fixture'
[IO.Directory]::CreateDirectory((Join-Path $fixture 'scripts')) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $fixture 'Plain Craft Launcher 2\Modules\Base')) | Out-Null
[IO.Directory]::CreateDirectory((Join-Path $fixture 'Plain Craft Launcher 2\My Project')) | Out-Null
Copy-Item -LiteralPath (Join-Path $repository 'scripts\Set-LauncherVersion.ps1'),(Join-Path $repository 'scripts\New-LauncherRelease.ps1') -Destination (Join-Path $fixture 'scripts')
Copy-Item -LiteralPath (Join-Path $repository 'Plain Craft Launcher 2\Modules\Base\ModBase.vb') -Destination (Join-Path $fixture 'Plain Craft Launcher 2\Modules\Base')
Copy-Item -LiteralPath (Join-Path $repository 'Plain Craft Launcher 2\My Project\AssemblyInfo.vb') -Destination (Join-Path $fixture 'Plain Craft Launcher 2\My Project')
Copy-Item -LiteralPath (Join-Path $repository 'Plain Craft Launcher 2\App.config') -Destination (Join-Path $fixture 'Plain Craft Launcher 2')
$savedGithubEnv = $env:GITHUB_ENV
try {
    # A fixture must never overwrite the real workflow's RELEASE_VERSION.
    $env:GITHUB_ENV = Join-Path $fixture 'github-env.txt'
    & (Join-Path $fixture 'scripts\Set-LauncherVersion.ps1') -Version 'v1.2.3'
    if (-not ([IO.File]::ReadAllText($env:GITHUB_ENV)).Contains('RELEASE_TAG=v1.2.3')) { throw 'Release environment stamping failed' }
} finally { $env:GITHUB_ENV = $savedGithubEnv }
$stamped = [IO.File]::ReadAllText((Join-Path $fixture 'Plain Craft Launcher 2\My Project\AssemblyInfo.vb'))
if (-not $stamped.Contains('AssemblyVersion("1.2.3.0")') -or -not $stamped.Contains('AssemblyFileVersion("1.2.3.0")')) { throw 'Release version stamping failed' }
$assets = Join-Path $output 'release-assets'
& (Join-Path $fixture 'scripts\New-LauncherRelease.ps1') -Version 'v1.2.3' -Executable $executable -OutputDirectory $assets -Notes 'Regression release' -Critical
$manifest = Get-Content -LiteralPath (Join-Path $assets 'sducraft-update.json') -Raw | ConvertFrom-Json
if ($manifest.version -ne '1.2.3' -or -not $manifest.critical -or $manifest.notes -ne 'Regression release') { throw 'Release metadata mismatch' }
foreach ($item in @(@('exe','SDUCraft-Launcher.exe'),@('config','SDUCraft-Launcher.exe.config'))) {
    $path = Join-Path $assets $item[1]
    if ($manifest.($item[0]).sha256 -ne (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -or $manifest.($item[0]).size -ne (Get-Item -LiteralPath $path).Length) { throw 'Published checksum mismatch' }
}
$rejected = $false
try { & (Join-Path $fixture 'scripts\New-LauncherRelease.ps1') -Version '1.2.4' -Executable $executable -OutputDirectory (Join-Path $output 'wrong-version') } catch { $rejected = $true }
if (-not $rejected) { throw 'Mismatched release version was accepted' }
Write-Output 'PASS release scripts: stamping, assets, hashes, version mismatch'
