# Terrain Deformation Setup

Terrain deformation is applied when the candidate trail is established.

## TrailManager

Enable:

- Apply Terrain Deformation
- Apply Effects Immediately

## Established Trail Prefab

`TerrainTrailDeformer` is optional because TrailManager adds it automatically.
Adding it to the prefab exposes the settings before Play Mode.

Suggested starting values:

- Bed Half Width: automatically matched to half of TrailMeshBuilder Width
- Shoulder Width: 1.5
- Strength: 1
- Bed Height Offset: -0.02
- Maximum Cut Depth: 1.5
- Maximum Fill Height: 1.5
- Path Sample Spacing: 0.5
- Path Resolution: 2
- Allow Runtime Deformation: enabled

The centre of the trail follows the original terrain height along the route.
Across the width of the trail it is flattened, then blended smoothly back into
the untouched ground through the shoulder area.

The deformation automatically affects every active Unity Terrain chunk crossed
by the path. No Terrain reference is required.
