param([Parameter(Mandatory)][string]$Executable)
$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath($Executable)
$version = [Reflection.AssemblyName]::GetAssemblyName($source).Version.ToString(3)
$folder = Join-Path $PSScriptRoot ('bin\update-helper-' + [Guid]::NewGuid().ToString('N'))
$stage = Join-Path $folder ('.sdu-update-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($stage) | Out-Null
$target = Join-Path $folder '自定义启动器 name.exe'
$helper = Join-Path $stage 'SDUCraft-Launcher.exe'
Copy-Item -LiteralPath $source -Destination $helper
Copy-Item -LiteralPath ($source + '.config') -Destination ($helper + '.config')
[IO.File]::WriteAllText($target, 'old launcher fixture')
[IO.File]::WriteAllText(($target + '.config'), '<configuration><!--old--></configuration>')
$settings = Join-Path $folder 'user-settings.ini'
[IO.File]::WriteAllText($settings, 'preserve user settings')
$oldHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
$oldConfigHash = (Get-FileHash -LiteralPath ($target + '.config') -Algorithm SHA256).Hash.ToLowerInvariant()
$parentInfo = [Diagnostics.ProcessStartInfo]::new('powershell.exe','-NoProfile -NonInteractive -Command "Start-Sleep -Seconds 3"')
$parentInfo.UseShellExecute = $false
$parentInfo.CreateNoWindow = $true
$parentInfo.WindowStyle = 'Hidden'
$parent = [Diagnostics.Process]::Start($parentInfo)
try {
    $plan = @{
        target = $target; parentId = $parent.Id; parentStarted = $parent.StartTime.ToUniversalTime().Ticks; restart = $false
        originalHash = $oldHash; originalConfigHash = $oldConfigHash
        release = @{
            schemaVersion = 1; version = $version
            exe = @{ sha256 = (Get-FileHash -LiteralPath $helper -Algorithm SHA256).Hash.ToLowerInvariant(); size = (Get-Item -LiteralPath $helper).Length }
            config = @{ sha256 = (Get-FileHash -LiteralPath ($helper + '.config') -Algorithm SHA256).Hash.ToLowerInvariant(); size = (Get-Item -LiteralPath ($helper + '.config')).Length }
        }
    }
    $planPath = Join-Path $stage 'update-plan.json'
    [IO.File]::WriteAllText($planPath, ($plan | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
    $start = [Diagnostics.ProcessStartInfo]::new($helper, ('--sdu-update "' + $planPath + '"'))
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = 'Hidden'
    $start.WorkingDirectory = $stage
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (-not $process.WaitForExit(20000)) { $process.Kill(); throw 'Production helper timed out' }
        if ($process.ExitCode -ne 0) { throw ('Production helper failed: ' + $process.ExitCode) }
    } finally { $process.Dispose() }
    if (-not $parent.HasExited) { throw 'Helper replaced files before parent exit' }
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -ne $plan.release.exe.sha256) { throw 'Production helper did not publish EXE' }
    if ((Get-FileHash -LiteralPath ($target + '.config') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $plan.release.config.sha256) { throw 'Production helper did not publish config' }
    if ((Get-FileHash -LiteralPath (Join-Path $stage 'backup.exe') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $oldHash) { throw 'EXE backup mismatch' }
    if ((Get-FileHash -LiteralPath (Join-Path $stage 'backup.exe.config') -Algorithm SHA256).Hash.ToLowerInvariant() -ne $oldConfigHash) { throw 'Config backup mismatch' }
    if ([IO.File]::ReadAllText($settings) -ne 'preserve user settings') { throw 'Helper changed user settings' }
    if (Test-Path -LiteralPath (Join-Path $stage 'PCL')) { throw 'Helper entered normal launcher initialization' }
    Write-Output 'PASS production helper: real EXE, parent exit wait, renamed target, EXE/config replacement, backups, user settings'
} finally {
    if (-not $parent.HasExited) { $parent.Kill() }
    $parent.Dispose()
}
