# Lighting Controller Product Definition

## Experience

The application is a compact, touch-friendly lighting console inspired by the grandMA dot2 workflow. It is not intended to copy proprietary artwork or source code. The primary workflow is:

1. Patch fixtures from the bundled QLC+ fixture-definition library.
2. Select fixtures or groups.
3. Modify intensity, position, colour, gobo, and beam attributes in a programmer.
4. Store the programmer into a numbered cue in a cue stack.
5. Run cue stacks from executor-style playback controls.

## Fixture profiles

The existing QLC+ `.qxf` fixture library and `QLCFixtureDefCache` remain the initial profile source. Fixture definitions are imported into the patch without converting them to a second profile format. A future GDTF importer can be added independently.

## Cue stacks

- A cue stores only explicit changes made by the programmer.
- Tracking stacks resolve missing values from earlier cues.
- Existing cues can be edited by replacing their explicit values.
- Programmer changes can update an existing cue without replacing unrelated values.
- Cues support a display number, name, notes, fade-in, fade-out, delay, and optional follow time.
- A missing follow time means that playback waits for an explicit `GO`.

## Playback merge

Intensity parameters are merged HTP so multiple active executors can contribute safely. Position, colour, gobo, beam, and other non-intensity attributes are LTP. QLC+'s channel classification and universe merge engine already implement this distinction and remain the source of truth.

## Initial screens

### Patch

Fixture manufacturer/model/mode browser, universe and address assignment, fixture numbering, and group management.

### Programmer

Fixture and group selection, attribute families, encoder-style controls, preset pools, and a pan/tilt XY control that operates only on the current selection.

### Cue list

Spreadsheet-style cue editor with number, name, fade, delay, follow, tracking state, notes, and record/update/delete actions.

### Playback

Executor tiles with stack label, current/next cue, level master, `GO`, pause, back, flash, and stop/release controls.

## Compatibility

Stock QLC+ projects are read through a dedicated importer. Conversion produces a report for functions that cannot be mapped unambiguously. New project data is versioned independently and is not expected to open in stock QLC+.
