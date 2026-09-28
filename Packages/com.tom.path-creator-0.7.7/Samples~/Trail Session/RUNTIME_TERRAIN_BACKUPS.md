# Runtime Terrain Backups

`TerrainTrailDeformer` can test terrain deformation in Play Mode without leaving
permanent heightmap changes in the project.

Enable:

- Allow Runtime Deformation
- Restore Terrain After Play Mode

Before each deformation, the package captures only the rectangular heightmap
patch that is about to change. It does not copy the complete Terrain heightmap.

Multiple trails may overlap. Patches are restored in reverse capture order so
the final result still returns to the terrain state that existed before the
first trail modified it.

When Play Mode ends, every captured patch is restored automatically.

## Manual Restore

Open the component context menu on any `TerrainTrailDeformer` and select:

`Restore Runtime Terrain Backups`

This restores all modified Terrain patches without exiting Play Mode.

## Current Scope

Only heightmap data is backed up because the deformation system currently
changes only terrain heights. Tree, detail, and texture restoration remain
separate concerns.

## Editor Restoration

In the Unity Editor, restoration now happens during
`PlayModeStateChange.ExitingPlayMode`, before Play Mode teardown completes.
This avoids relying on object destruction order and works more reliably with
Enter Play Mode Options.

## Foliage Restoration

On `TerrainFoliageClearance`, enable:

- Allow Runtime Clearing
- Restore Foliage After Play Mode

The system backs up:

- the original Terrain tree array once per TerrainData
- only affected detail-map rectangles for each detail layer
- the active state of generated foliage GameObjects disabled by clearance

All of these are restored with the terrain height patches before Play Mode exits.

When generated foliage clearing is set to `Destroy`, runtime clearance
automatically uses `Disable` while restoration is enabled. Destroyed GameObjects
cannot be restored safely.
