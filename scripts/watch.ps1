$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = & "$PSScriptRoot\resolve-dotnet.ps1"
& $dotnet watch --project "$root\src\LightingController.App\LightingController.App.csproj" run
exit $LASTEXITCODE

