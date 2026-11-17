$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
dotnet run --project "$root\src\LightingController.App\LightingController.App.csproj"

