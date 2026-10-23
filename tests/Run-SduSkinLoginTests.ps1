param([string]$DotNetPath = 'dotnet')
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$output = Join-Path $PSScriptRoot ('bin\skin-login-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($output) | Out-Null
function Extract([string]$source, [string]$pattern) {
    $matchesFound = [regex]::Matches($source,$pattern,[Text.RegularExpressions.RegexOptions]::Multiline -bor [Text.RegularExpressions.RegexOptions]::Singleline)
    if ($matchesFound.Count -eq 0) { throw "Missing production source: $pattern" }
    return ($matchesFound | ForEach-Object { $_.Value }) -join "`r`n"
}
$launch = Get-Content (Join-Path $repository 'Plain Craft Launcher 2\Modules\Minecraft\ModLaunch.vb') -Raw -Encoding UTF8
$runtime = 'Public Module ActualLoginRuntime' + "`r`n"
$runtime += Extract $launch '^    Public Enum McLoginType\b.*?^    End Enum'
$runtime += "`r`n" + (Extract $launch '^    Public (?:MustInherit )?Class (?:McLoginData|McLoginServer)\b.*?^    End Class')
$runtime += "`r`n" + (Extract $launch '^    Public Function (?:McLoginInput|McLoginAble|McLoginName)\(.*?^    End Function')
$runtime += "`r`nEnd Module`r`n"
$minecraft = Get-Content (Join-Path $repository 'Plain Craft Launcher 2\Modules\Minecraft\ModMinecraft.vb') -Raw -Encoding UTF8
$runtime += "Public Module ActualInstanceSelection`r`n    Private _McInstanceSelected As McInstance`r`n" + (Extract $minecraft '^    Public Property McInstanceSelected As McInstance.*?^    End Property') + "`r`nEnd Module`r`n"
foreach ($page in @('PageLoginAuth','PageLoginAuthSkin')) {
    $source = Get-Content (Join-Path $repository "Plain Craft Launcher 2\Pages\PageLaunch\$page.xaml.vb") -Raw -Encoding UTF8
    $runtime += "Partial Public Class $page`r`n" + (Extract $source '^    Public Shared Function (?:GetLoginData|IsVaild)\(.*?^    End Function') + "`r`nEnd Class`r`n"
}
$runtimeFile = Join-Path $output 'ActualLoginRuntime.vb'
[IO.File]::WriteAllText($runtimeFile,$runtime,[Text.UTF8Encoding]::new($false))
Push-Location $repository
try {
    $version = (& $DotNetPath --version).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Cannot locate .NET SDK' }
    $sdkLine = @(& $DotNetPath --list-sdks) | Where-Object { $_.StartsWith($version + ' ') } | Select-Object -First 1
    if ($sdkLine -notmatch '\[(.+)\]') { throw "Cannot locate SDK compiler: $version" }
    $compiler = Join-Path $Matches[1] "$version\Roslyn\bincore\vbc.dll"
    $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
    $vbRuntime = Get-ChildItem (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\Microsoft.VisualBasic') -Filter Microsoft.VisualBasic.dll -Recurse | Select-Object -First 1 -ExpandProperty FullName
    $executable = Join-Path $output 'SduSkinLoginTests.exe'
    $arguments = @($compiler,'/nologo','/noconfig','/nostdlib','/target:exe',"/out:$executable","/vbruntime:$vbRuntime",'/optioninfer+','/optionstrict-',
        '/imports:System,System.Collections.Generic,System.Linq,System.Threading,Microsoft.VisualBasic',
        "/r:$framework\mscorlib.dll","/r:$framework\System.dll","/r:$framework\System.Core.dll",$runtimeFile,
        (Join-Path $repository 'Plain Craft Launcher 2\Modules\Sdu\SduSkinLogin.vb'),(Join-Path $PSScriptRoot 'SduSkinLoginTests.vb'))
    & $DotNetPath @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Skin-login test compilation failed' }
    [IO.File]::Copy((Join-Path $repository 'Plain Craft Launcher 2\App.config'), ($executable + '.config'), $true)
    & $executable
    if ($LASTEXITCODE -ne 0) { throw 'Skin-login tests failed' }
} finally {
    Pop-Location
}
