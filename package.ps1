param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$packageRoot = Join-Path $PSScriptRoot 'dist\RemoteDispatch'
$mainAssembly = Join-Path $PSScriptRoot "bin\$Configuration\net48\RemoteDispatch.dll"
$mpAssembly = Join-Path $PSScriptRoot "Multiplayer\bin\$Configuration\net48\RemoteDispatch.Multiplayer.dll"
if (!(Test-Path -LiteralPath $mainAssembly) -or !(Test-Path -LiteralPath $mpAssembly)) {
    throw 'Build Multiplayer\RemoteDispatch.Multiplayer.csproj before packaging.'
}
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
Copy-Item -LiteralPath $mainAssembly, $mpAssembly -Destination $packageRoot
foreach ($packageFile in @('info.json', 'README.md', 'LICENSE')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $packageFile) -Destination $packageRoot
}
$packageVersion = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'info.json') -Raw | ConvertFrom-Json).Version
$packageZip = Join-Path (Split-Path -Parent $PSScriptRoot) "RemoteDispatch-Signals-$packageVersion.zip"
Compress-Archive -LiteralPath (Join-Path $PSScriptRoot 'dist\RemoteDispatch') -DestinationPath $packageZip -Force
Write-Output $packageZip
