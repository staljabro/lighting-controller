$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
dotnet test "$root\LightingController.slnx" --nologo

