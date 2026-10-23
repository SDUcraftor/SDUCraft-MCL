$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$output = Join-Path $PSScriptRoot ('bin\isolation-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($output) | Out-Null
$source = Get-Content (Join-Path $repository 'Plain Craft Launcher 2\Modules\Minecraft\ModMinecraft.vb') -Raw -Encoding UTF8
$property = [regex]::Match($source, '(?ms)^        Public ReadOnly Property PathIndie As String\r?\n.*?^        End Property')
if (-not $property.Success) { throw 'Cannot locate production PathIndie property' }
$runtime = "Partial Public Class McInstance`r`n" + $property.Value + "`r`nEnd Class`r`n"
$runtimePath = Join-Path $output 'ActualIsolation.vb'
[IO.File]::WriteAllText($runtimePath, $runtime, [Text.UTF8Encoding]::new($false))
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'vbc.exe'
$vbRuntime = Get-ChildItem (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\Microsoft.VisualBasic') -Filter Microsoft.VisualBasic.dll -Recurse | Select-Object -First 1 -ExpandProperty FullName
$executable = Join-Path $output 'SduIsolationTests.exe'
& $compiler /nologo /noconfig /nostdlib /target:exe /optioninfer+ /optionstrict+ "/out:$executable" "/vbruntime:$vbRuntime" "/r:$framework\mscorlib.dll" "/r:$framework\System.dll" "/r:$framework\System.Core.dll" $runtimePath (Join-Path $PSScriptRoot 'SduIsolationTests.vb')
if ($LASTEXITCODE -ne 0) { throw 'Isolation regression compilation failed' }
& $executable
if ($LASTEXITCODE -ne 0) { throw 'Isolation regression failed' }
