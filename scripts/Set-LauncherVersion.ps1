param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
$versionText = $Version -creplace '^v', ''
if ($versionText -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'Release version must be vMAJOR.MINOR.PATCH' }
$parsed = [version]$versionText
if ($parsed.Major -gt 65534 -or $parsed.Minor -gt 65534 -or $parsed.Build -gt 65534) { throw 'Version component exceeds assembly version limits' }
$repository = Split-Path $PSScriptRoot -Parent
$utf8 = [Text.UTF8Encoding]::new($false)
$basePath = Join-Path $repository 'Plain Craft Launcher 2\Modules\Base\ModBase.vb'
$base = [IO.File]::ReadAllText($basePath)
$pattern = 'Public Const VersionBaseName As String = "[^"]*"'
if ([regex]::Matches($base,$pattern).Count -ne 1) { throw 'VersionBaseName declaration not found' }
$base = [regex]::Replace($base,$pattern,('Public Const VersionBaseName As String = "' + $versionText + '"'))
if ($env:GITHUB_SHA -match '^[a-f0-9]{40}$') { $base = $base.Replace('Public Const CommitHash As String = ""', ('Public Const CommitHash As String = "' + $env:GITHUB_SHA + '"')) }
[IO.File]::WriteAllText($basePath,$base,$utf8)
$assemblyPath = Join-Path $repository 'Plain Craft Launcher 2\My Project\AssemblyInfo.vb'
$assembly = [IO.File]::ReadAllText($assemblyPath)
foreach ($attribute in @('AssemblyVersion','AssemblyFileVersion')) {
    $pattern = $attribute + '\("[^"]*"\)'
    if ([regex]::Matches($assembly,$pattern).Count -ne 1) { throw ('Missing version attribute: ' + $attribute) }
    $assembly = [regex]::Replace($assembly,$pattern,($attribute + '("' + $versionText + '.0")'))
}
[IO.File]::WriteAllText($assemblyPath,$assembly,$utf8)
if ($env:GITHUB_ENV) {
    Add-Content -LiteralPath $env:GITHUB_ENV -Value ('RELEASE_VERSION=' + $versionText) -Encoding utf8
    Add-Content -LiteralPath $env:GITHUB_ENV -Value ('RELEASE_TAG=v' + $versionText) -Encoding utf8
}
Write-Output ('Stamped launcher version: ' + $versionText)
