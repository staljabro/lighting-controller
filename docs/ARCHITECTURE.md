# Lighting Controller Architecture

This project starts as a fork of QLC+ 5 and progressively introduces a cue-list-first workflow.

## Product direction

- Keep QLC+'s fixture library, patching, universes, input/output plugins, and DMX engine.
- Make fixture selection and an attribute-based programmer the primary editing workflow.
- Store new shows as cue lists with tracking, timing, and explicit playback state.
- Keep palettes as reusable references for intensity, colour, position, and beam attributes.
- Import legacy QLC+ `.qxw` projects through a separate conversion pipeline.

## Boundary with legacy QLC+

Legacy QLC+ functions remain readable by the importer, but they do not define the new application's domain model. Import converts fixtures, groups, scenes, chasers, shows, and Virtual Console triggers into a conversion report and one or more native cue lists. Ambiguous or unsupported constructs must be reported instead of silently approximated.

## Initial implementation stages

1. Establish a reproducible upstream QLC+ build and test baseline.
2. Add the native cue-list, cue, programmer, and palette domain models to the engine.
3. Add serialization and round-trip tests for the new project data.
4. Build the cue-list editor and playback controls in the QML UI.
5. Connect programmer output and cue playback to QLC+'s existing DMX engine.
6. Implement `.qxw` analysis, conversion, and validation as a separate importer.

## Compatibility policy

The fork will keep its QLC+ upstream remote so selected engine, fixture, and plugin improvements can be incorporated. New project data will be versioned independently. Import compatibility does not imply that new projects can be opened by stock QLC+.
