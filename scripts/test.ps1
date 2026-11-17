$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = & "$PSScriptRoot\resolve-dotnet.ps1"
& $dotnet test "$root\LightingController.slnx" --nologo --disable-build-servers -m:1 -p:NuGetAudit=false
exit $LASTEXITCODE

