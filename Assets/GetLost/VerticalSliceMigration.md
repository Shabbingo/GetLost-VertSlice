# Vertical Slice Migration

Migrated from `Get-Lost-Prototype` on 28 September 2026.

## Included systems

- Engaging Walking Controller 4.0.2
- Terrain Foliage Spawner 0.5.0
- Tom's Path Creator 0.7.7
- Get Lost terrain generator, design markers, and erosion
- Managed grass, terrain-detail, and foliage interaction
- Nature Renderer and the Get Lost distant-forest renderer
- Wagon physics and gravel-path deposition
- Source foliage rules, terrain surface profiles, and referenced art assets

Generated terrain worlds and generated foliage-streaming chunk assets were intentionally excluded.

## Integration changes

The wagon no longer depends on the prototype save manager, player-tool manager,
menu context manager, or full legacy TrailManager. `WagonWorld` owns gravel-path
orchestration and uses the extracted terrain deformation and foliage-clearance helpers.

`Assets/Editor/VerticalSliceSetup.cs` creates a clean player prefab at
`Assets/GetLost/VerticalSlice/VerticalSlicePlayer.prefab`. It runs once after a
successful script reload and is also available from:

`Get Lost > Vertical Slice > Rebuild Player Prefab`

## First scene setup

1. Generate terrain from `Get Lost > Terrain Generator`.
2. Place `VerticalSlicePlayer.prefab` above the terrain.
3. Enter Play Mode. The wagon bootstrap creates a wagon near the player once a
   suitable terrain position is available.
4. Use `E` to take or release the handles, `G` to open or close the gravel gate,
   and `R` near the wagon to refill the prototype gravel supply.
