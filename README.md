# Lighting Controller

A Windows-only lighting controller with a compact console workflow inspired by grandMA dot2.

## Current foundation

- WPF desktop application on .NET 10
- Native fixture profiles and an integrated fixture library
- Sparse cues with tracking
- Record/update/replace-ready cue-stack domain model
- HTP intensity merging and latest-takes-precedence non-intensity attributes
- No Qt, CMake, MSYS2, Node.js, or native compiler required

## Develop

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), then run:

```powershell
.\scripts\test.ps1
.\scripts\run.ps1
```

Earlier prototype work remains preserved in Git history and reference branches.

## Projects

- `LightingController.App`: Windows WPF interface
- `LightingController.Core`: programmer, cues, tracking, and merge rules
- `LightingController.Fixtures`: native fixture profiles and library
- `LightingController.Output`: Art-Net, sACN, and future hardware output
- `LightingController.Persistence`: native show-file persistence

