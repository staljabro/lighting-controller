$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
dotnet build "$root\LightingController.slnx" --nologo

