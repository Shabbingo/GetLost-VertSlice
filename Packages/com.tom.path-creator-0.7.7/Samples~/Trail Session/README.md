# Trail Session Sample

## Scene Setup

Create a GameObject named `Active Trail Session` and add:

```text
PathCreator
TrailData
TrailSession
LineRenderer
```

Assign the player root Transform to `TrailSession > Session Target`.

For a Unity Terrain:

- Set Projection Mode to `AssignedTerrain`.
- Assign the Terrain.
- Start with Point Spacing around `1.75`.
- Start with Final Simplification Tolerance around `0.35`.

## Input System Setup

`TrailSession` does not directly read input. This avoids locking the package to
a particular input setup.

Using `PlayerInput` with Unity Events, connect actions to:

- `TrailSession.BeginSession()`
- `TrailSession.FinishSession()`
- `TrailSession.ResumeSession()`
- `TrailSession.EstablishTrail()`
- `TrailSession.AbandonSession()`

A practical first test is:

```text
R = Begin a new trail session
T = Finish the session and create a candidate
Y = Establish the candidate trail
Backspace = Discard
```

## Expected Flow

```text
Idle
  -> BeginSession
Active
  -> FinishSession
Candidate
  -> EstablishTrail
Established TrailData
```

The temporary LineRenderer disappears after establishment. Terrain painting,
foliage clearance, and final trail mesh activation are intentionally not
triggered yet.
