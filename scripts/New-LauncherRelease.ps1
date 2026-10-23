param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$Notes = '',
    [switch]$Critical
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$versionText = $Version -creplace '^v', ''
if ($versionText -cnotmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'Invalid release version' }
if ([Reflection.AssemblyName]::GetAssemblyName([IO.Path]::GetFullPath($Executable)).Version -ne [version]($versionText + '.0')) { throw 'Built EXE version does not match release version' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $output) -and @(Get-ChildItem -LiteralPath $output -Force).Count -gt 0) { throw 'Release output directory must be empty' }
[IO.Directory]::CreateDirectory($output) | Out-Null
$exePath = Join-Path $output 'SDUCraft-Launcher.exe'
$configPath = $exePath + '.config'
Copy-Item -LiteralPath $Executable -Destination $exePath
Copy-Item -LiteralPath (Join-Path $repository 'Plain Craft Launcher 2\App.config') -Destination $configPath
if ($Notes.Length -gt 4000) { $Notes = $Notes.Substring(0,4000) }
$manifest = [ordered]@{
    schemaVersion = 1
    version = $versionText
    notes = $Notes
    critical = $Critical.IsPresent
    exe = @{ sha256 = (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash.ToLowerInvariant(); size = (Get-Item -LiteralPath $exePath).Length }
    config = @{ sha256 = (Get-FileHash -LiteralPath $configPath -Algorithm SHA256).Hash.ToLowerInvariant(); size = (Get-Item -LiteralPath $configPath).Length }
}
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $output 'sducraft-update.json'), ($manifest | ConvertTo-Json -Depth 4), $utf8)
[IO.File]::WriteAllText((Join-Path $output 'release-notes.md'), $Notes, $utf8)
Write-Output ('Release assets prepared: ' + $output)
