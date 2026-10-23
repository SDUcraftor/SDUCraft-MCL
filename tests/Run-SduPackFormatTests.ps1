param([Parameter(Mandatory)][string]$Executable, [string]$OutputDirectory = '', [switch]$StaWorker)
$ErrorActionPreference = 'Stop'
$executablePath = [IO.Path]::GetFullPath($Executable)
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot ('bin\pack-formats-' + [Guid]::NewGuid().ToString('N')) }
if (-not $StaWorker) {
    & powershell.exe -NoProfile -STA -File $PSCommandPath -Executable $executablePath -OutputDirectory $OutputDirectory -StaWorker
    if ($LASTEXITCODE -ne 0) { throw 'Compiled pack format regression failed' }
    return
}
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
[Environment]::CurrentDirectory = $OutputDirectory
Add-Type -AssemblyName PresentationFramework, System.IO.Compression, System.IO.Compression.FileSystem
$assembly = [Reflection.Assembly]::LoadFrom($executablePath)
$app = [Activator]::CreateInstance($assembly.GetType('PCL.Application', $true), $true)
$flags = [Reflection.BindingFlags]'Static,Public,NonPublic'
$minecraft = $assembly.GetType('PCL.ModMinecraft', $true)
$mc = Join-Path $OutputDirectory 'mc'
[IO.Directory]::CreateDirectory($mc) | Out-Null
# Avoid the public folder setter: it saves the user's selection. Only the child process uses this field.
$minecraft.GetField('_McFolderSelected', $flags).SetValue($null, ($mc + '\'))
$assembly.GetType('PCL.ModBase').GetField('PathTemp', $flags).SetValue($null, ((Join-Path $OutputDirectory 'cache') + '\'))
$modpack = $assembly.GetType('PCL.ModModpack', $true)
$install = @($modpack.GetMethods($flags) | Where-Object { $_.Name -eq 'ModpackInstall' -and $_.GetParameters().Count -eq 6 })[0]
$merge = $assembly.GetType('PCL.SduModMerge', $true).GetMethod('Apply', $flags)
function Check($value, $message) { if (-not $value) { throw $message } }
function Write-File($path, $text) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
    [IO.File]::WriteAllText($path, $text)
}
function New-Pack($path, $entries) {
    $stream = [IO.File]::Create($path)
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in $entries.Keys) {
            $writer = [IO.StreamWriter]::new($zip.CreateEntry($name).Open())
            try { $writer.Write($entries[$name]) } finally { $writer.Dispose() }
        }
    } finally { $zip.Dispose(); $stream.Dispose() }
}
$cases = @(
    @{Name='Modrinth'; Marker='modrinth.index.json'; Json='{"name":"embedded-name","dependencies":{"minecraft":"1.21.1"},"files":[]}'; Overrides='overrides/'},
    @{Name='CurseForge'; Marker='manifest.json'; Json='{"name":"embedded-name","minecraft":{"version":"1.21.1","modLoaders":[]},"files":[],"overrides":"overrides"}'; Overrides='overrides/'},
    @{Name='MCBBS'; Marker='mcbbs.packmeta'; Json='{"name":"embedded-name","addons":[{"id":"game","version":"1.21.1"}]}'; Overrides='overrides/'},
    @{Name='MCBBS-manifest'; Marker='manifest.json'; Json='{"name":"embedded-name","addons":[{"id":"game","version":"1.21.1"}]}'; Overrides='overrides/'},
    @{Name='HMCL'; Marker='modpack.json'; Json='{"name":"embedded-name","gameVersion":"1.21.1"}'; Overrides='minecraft/'},
    @{Name='MultiMC'; Marker='mmc-pack.json'; Json='{"components":[{"uid":"net.minecraft","version":"1.21.1"}]}'; Overrides='.minecraft/'}
)
foreach ($case in $cases) {
    $entries = @{}
    $entries[$case.Marker] = $case.Json
    $entries[$case.Overrides + 'mods/shared.jar'] = 'new mod'
    $entries[$case.Overrides + 'mods/new.jar'] = 'added mod'
    $entries[$case.Overrides + 'config/user.cfg'] = 'package config'
    $entries[$case.Overrides + 'options.txt'] = 'package key bindings'
    if ($case.Name -eq 'MultiMC') { $entries['instance.cfg'] = "name=embedded-name`n" }
    $zipPath = Join-Path $OutputDirectory ($case.Name + '.zip')
    New-Pack $zipPath $entries
    foreach ($variant in @('root','folder','nested')) {
        $name = $case.Name + '-' + $variant
        $packageInput = $zipPath
        if ($variant -eq 'folder') {
            $packageInput = Join-Path $OutputDirectory ($name + '.zip')
            $prefixed = @{}
            foreach ($key in $entries.Keys) { $prefixed['wrapper/' + $key] = $entries[$key] }
            New-Pack $packageInput $prefixed
        }
        if ($variant -eq 'nested') {
            $packageInput = Join-Path $OutputDirectory ($name + '.zip')
            $outer = [IO.Compression.ZipFile]::Open($packageInput, [IO.Compression.ZipArchiveMode]::Create)
            try { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($outer, $zipPath, 'wrapper/modpack.zip') | Out-Null } finally { $outer.Dispose() }
        }
        $stage = Join-Path $OutputDirectory ($name + '-stage')
        $loader = $install.Invoke($null, @([string]$packageInput, 'requested-name', $null, $false, [string]$stage, 0))
        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        while ($loader.State.ToString() -eq 'Loading' -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 20 }
        if ($loader.State.ToString() -ne 'Finished') { throw ('Preparation failed for '+$name+': '+$loader.Error) }
        Check ([IO.File]::ReadAllText((Join-Path $stage 'config/user.cfg')) -eq 'package config') ('Pack config missing: '+$name)
        Check ([IO.File]::ReadAllText((Join-Path $stage 'options.txt')) -eq 'package key bindings') ('Pack keys missing: '+$name)
        Check (-not [IO.Directory]::Exists((Join-Path $mc 'versions/embedded-name'))) ('Ignored supplied destination: '+$name)
        $target = Join-Path $mc ('versions/'+$name)
        Write-File (Join-Path $target ($name+'.json')) '{"id":"existing"}'
        Write-File (Join-Path $target 'config/user.cfg') 'user config'
        Write-File (Join-Path $target 'options.txt') 'user key bindings'
        Write-File (Join-Path $target 'mods/shared.jar') 'old mod'
        Write-File (Join-Path $target 'mods/local.jar') 'local-only mod'
        $result = $merge.Invoke($null, @([string]$mc, [string]$target, [string](Join-Path $stage 'mods'), [Threading.CancellationToken]::None))
        Check ([IO.File]::ReadAllText((Join-Path $target 'config/user.cfg')) -eq 'user config') ('Config overwritten: '+$name)
        Check ([IO.File]::ReadAllText((Join-Path $target 'options.txt')) -eq 'user key bindings') ('Keys overwritten: '+$name)
        Check ([IO.File]::ReadAllText((Join-Path $target 'mods/shared.jar')) -eq 'new mod') ('Mod not replaced: '+$name)
        Check ([IO.File]::ReadAllText((Join-Path $target 'mods/local.jar')) -eq 'local-only mod') ('Local mod removed: '+$name)
        Check ([IO.File]::Exists((Join-Path $target 'mods/new.jar'))) ('New mod missing: '+$name)
        Check ([IO.File]::ReadAllText((Join-Path $result.ModsBackup 'shared.jar')) -eq 'old mod') ('Backup missing: '+$name)
        Write-Output ('PASS compiled '+$name+': actual installer prepares overrides, merge preserves user config/keys/local mods, backs up old mods')
    }
}
$recordType = $assembly.GetType('PCL.SduPackRecord', $true)
$item = $assembly.CreateInstance('PCL.SduGameDownload')
$item.Id = 'test-project'; $item.ModifyDate = '2026-10-23T00:00:00Z'
$item.PublishDate = '2026-10-20'
$file = $assembly.CreateInstance('PCL.SduGameDownloadFile')
$file.ModifyDate = '2026-10-23T08:00:00+08:00'; $item.Files.Add($file)
$recordType.GetMethod('Save',$flags).Invoke($null,@([string]$target,$item))
$saved = $recordType.GetMethod('Read',$flags).Invoke($null,@([string]$target))
Check (-not $recordType.GetMethod('HasUpdate',$flags).Invoke($null,@($saved,$item))) 'Equal timestamps trigger a repeated update'
$file.ModifyDate = '2026-10-23T08:00:01+08:00'
Check ($recordType.GetMethod('HasUpdate',$flags).Invoke($null,@($saved,$item))) 'Changed file timestamp was ignored'
$file.ModifyDate = '2026-10-19'; $item.ModifyDate = '2026-10-19'
Check (-not $recordType.GetMethod('HasUpdate',$flags).Invoke($null,@($saved,$item))) 'Older package triggers a downgrade'
$item.Id = 'other-project'; $item.ModifyDate = '2027-01-01'
Check (-not $recordType.GetMethod('HasUpdate',$flags).Invoke($null,@($saved,$item))) 'Another project triggers update'
$legacy = $saved.DeepClone()
$null = $legacy.Remove('projectModified'); $null = $legacy.Remove('fileModified'); $null = $legacy.Remove('published')
$item.Id = 'test-project'
Check ($recordType.GetMethod('HasUpdate',$flags).Invoke($null,@($legacy,$item))) 'Legacy ID-only record is silently marked current'
$item.ModifyDate = 'invalid'; $item.PublishDate = ''; $file.ModifyDate = ''
Check (-not $recordType.GetMethod('HasUpdate',$flags).Invoke($null,@($legacy,$item))) 'Invalid remote timestamp triggers update'
Write-Output 'PASS compiled update records: persisted times, timezone equality, file update, no downgrade or cross-project update'
$confirm = $assembly.GetType('PCL.SduModpackInstaller',$true).GetMethod('ConfirmUpdate',$flags)
$script:askCount = 0
$cancel = [Func[string,int]]{ param($message) $script:askCount++; return 2 }
Check ($confirm.Invoke($null,@([string]$mc,'not-installed',$cancel))) 'Fresh installation unexpectedly requires update confirmation'
Check ($script:askCount -eq 0) 'Fresh installation showed update confirmation'
Check (-not $confirm.Invoke($null,@([string]$mc,[string]$name,$cancel))) 'Cancel was treated as update approval'
Check ($script:askCount -eq 1) 'Existing pack did not ask for update confirmation'
$approve = [Func[string,int]]{ param($message) $script:askCount++; return 1 }
Check ($confirm.Invoke($null,@([string]$mc,[string]$name,$approve))) 'Confirmed update was rejected'
Check ([IO.File]::ReadAllText((Join-Path $target 'config/user.cfg')) -eq 'user config') 'Confirmation touched existing config'
Write-Output 'PASS compiled existing-instance confirmation: fresh install skips prompt, cancel stops, approval continues, existing data unchanged'
