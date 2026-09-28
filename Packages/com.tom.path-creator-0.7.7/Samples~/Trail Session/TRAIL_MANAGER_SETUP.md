# Trail Manager Setup

Recommended scene hierarchy:

```text
Trail System                    [TrailManager]
├── Active Trail Session        [PathCreator, TrailData, TrailSession, LineRenderer]
└── Established Trails
```

## TrailManager references

- **Active Session:** drag `Active Trail Session` here.
- **Established Trails Root:** drag the `Established Trails` transform here.
- **Established Trail Prefab:** strongly recommended. Create a prefab containing:
  - `PathCreator`
  - `TrailData`
  - `TrailMeshBuilder`
  - `MeshFilter`
  - `MeshRenderer`
  - optional `TerrainFoliageClearance`

Configure the prefab's material, trail width, terrain, projection, and runtime
foliage settings exactly as you want every permanent trail to appear.

## Runtime flow

1. Begin the trail session.
2. Walk the route.
3. Finish the session to create a Candidate.
4. Establish the trail.
5. TrailManager creates `Trail 001` below `Established Trails`.
6. The active session resets to Idle for the next route.

Without a prefab, TrailManager creates a basic fallback trail with a mesh builder,
but it will use Unity's default renderer material and will not include foliage
clearance. A prefab is the intended production workflow.
