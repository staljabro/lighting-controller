$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = & "$PSScriptRoot\resolve-dotnet.ps1"
& $dotnet run --project "$root\src\LightingController.App\LightingController.App.csproj" --disable-build-servers
exit $LASTEXITCODE

