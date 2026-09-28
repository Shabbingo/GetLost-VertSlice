# Tom's Path Creator

## Version 0.3.0

This release adds runtime player-route session.

## Main Components

```text
Recorded Trail GameObject
├── PathCreator
├── TrailData
├── TrailSession
└── LineRenderer
```

Existing visual and world-modification components may remain on the same object,
but should not be activated as part of establishment until that pipeline is
added.

## Recording Workflow

```text
StartRecording()
    ↓
Player movement sampled into temporary points
    ↓
PauseRecording() / ContinueRecording()
    ↓
StopRecording()
    ↓
TrailData becomes Candidate
    ↓
EstablishCandidate() or DiscardRecording()
```

## TrailSession Features

- Records any assigned Transform at runtime.
- Does not depend on a specific Unity input package.
- Uses configurable horizontal point spacing.
- Collapses nearly straight points while session.
- Performs optional Ramer-Douglas-Peucker final simplification.
- Projects points directly to an assigned Terrain or through physics layers.
- Updates `PathCreator` while walking.
- Displays a lightweight temporary `LineRenderer`.
- Enforces a configurable maximum-point safety limit.
- Updates `TrailData` lifecycle states.

## Suggested Starting Values

```text
Point Spacing: 1.75 m
Minimum Turn Angle: 4 degrees
Maximum Point Count: 4096
Projection Mode: Assigned Terrain
Final Simplification Tolerance: 0.35 m
Preview Width: 0.08 m
```

## Input

Call the recorder's public methods from your own controls:

```csharp
recorder.StartRecording();
recorder.PauseRecording();
recorder.ContinueRecording();
recorder.StopRecording();
recorder.EstablishCandidate();
recorder.DiscardRecording();
```

This package deliberately does not read keyboard or gamepad state itself.

## Current Boundary

Establishing a candidate currently changes `TrailData` to `Established` and
hides the temporary preview. It does not yet trigger:

- permanent mesh reveal
- terrain painting
- foliage clearance
- terrain sculpting
- rewards or popularity

Those will be coordinated by the next establishment pipeline.
