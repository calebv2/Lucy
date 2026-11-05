param(
    [Parameter(Mandatory=$true)][string]$GamePath,
    [Parameter(Mandatory=$true)][string]$UnityEditorPath,
    [string]$Output=(Join-Path $PSScriptRoot 'bin')
)
$ErrorActionPreference='Stop'
$compiler=Join-Path $UnityEditorPath 'Data\Tools\Roslyn\csc.exe'
$managed=Join-Path $GamePath 'A Township Tale_Data\Managed'
$sync=Join-Path $GamePath 'Mods\SyncLib.dll'
$melon=Join-Path $GamePath 'MelonLoader\net35'
$bundle=Join-Path $PSScriptRoot 'assets\lucy-graphics'
foreach($path in @($compiler,$managed,$sync,$melon,$bundle)){
    if(-not (Test-Path -LiteralPath $path)){throw "Missing build input: $path"}
}
New-Item -ItemType Directory -Path $Output -Force | Out-Null
$dll=Join-Path $Output 'Lucy.dll'
$arguments=@('/nologo','/target:library','/langversion:latest','/nostdlib+','/warnaserror+','/optimize+',('/out:"'+$dll+'"'))
$arguments+=Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object {'/reference:"'+$_.FullName+'"'}
foreach($reference in @((Join-Path $melon 'MelonLoader.dll'),(Join-Path $melon '0Harmony.dll'),$sync)){
    $arguments+='/reference:"'+$reference+'"'
}
$arguments+='/resource:"'+$bundle+'",LucyMould.Graphics'
$arguments+=Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object {'"'+$_.FullName+'"'}
$response=Join-Path $Output 'build.rsp'
Set-Content -LiteralPath $response -Value $arguments
& $compiler ('@'+$response)
if($LASTEXITCODE -ne 0){throw 'Lucy compilation failed.'}
Get-Item -LiteralPath $dll | Select-Object FullName,Length
