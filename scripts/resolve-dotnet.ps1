$command = Get-Command dotnet -ErrorAction SilentlyContinue
if ($command) {
    return $command.Source
}

$installedPath = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
if (Test-Path -LiteralPath $installedPath) {
    return $installedPath
}

throw 'The .NET SDK was not found. Install .NET 10 SDK, then try again.'

