# Lighting Controller

A Windows-only lighting controller with a compact console workflow inspired by grandMA dot2.

## Current foundation

- WPF desktop application on .NET 10
- QLC+ `.qxf` fixture-profile import
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

The QLC+ fork and the earlier C++ cue-list work remain preserved in Git history and reference branches.

## Projects

- `LightingController.App`: Windows WPF interface
- `LightingController.Core`: programmer, cues, tracking, and merge rules
- `LightingController.Fixtures`: QLC+ fixture-profile reader
- `LightingController.Output`: Art-Net, sACN, and future hardware output
- `LightingController.Persistence`: native show-file persistence

