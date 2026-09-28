# Terrain Foliage Spawner 0.5.0

## Relief-aware cliff formations

Enable **Use Cliff Formation Distribution** on a rock rule to replace uniform
slope scattering with deterministic rock bands. Candidates are weighted by:

- a soft minimum-to-full-density slope range;
- the largest downhill terrain drop within the relief sample radius; and
- a warped world-space patch field that creates dense formations and open gaps.

**Samples Per Cell** increases only that rule's placement density, leaving the
spawner's global spacing unchanged for trees and ordinary foliage. The normal
terrain-layer, hard slope, spawn chance, exclusion, and path tests still apply.

Recommended starting values are 38/60 degrees for minimum/full slope, a 10 m
relief radius, 4/12 m for minimum/full height drop, a 45 m patch size, and two
samples per cell. Use the scene placement preview to inspect the resulting
density: orange-to-green points are increasingly suitable and cyan points fail
the cliff test.

For landmark escarpments, also enable **Build Continuous Cliff Faces** and mark
one or more GameObject prefab entries as **Use For Cliff Faces**. The generator
then selects only those structural meshes, spaces them independently of small
rocks, keeps them upright, turns them toward the steepest downhill direction,
and applies wide/tall/deep scale ranges with a configurable hillside embed.
Structural panels should normally use **Scene Objects** so their silhouettes
and collision never disappear when runtime foliage streams.

## Auto-find details and trees from foliage rules

Assign your Terrain Foliage Rules to the spawner, then click **Auto Find Details and Trees From Rules** under Prototype Synchronisation. It collects unique, positive-weight **Terrain Detail** and **Terrain Tree** entries and appends missing prototypes to the assigned/target terrains. GameObject and Managed Instanced Grass entries keep their existing output modes and do not need Terrain prototypes.

**Auto Find Prototypes From Rules** is enabled by default. Together with **Synchronise Prototypes Before Generation**, this also happens automatically when generating foliage. The explicit auto-find button works even when the automatic option is disabled.

Existing Terrain prototypes and their indices are preserved. Settings are copied from the prototype library first, then the assigned terrain if fallback is allowed, then other target terrains. If no settings exist, the spawner creates mesh-instanced detail defaults (0.8–1.2 width/height) or a tree prototype from the rule's prefab. Invalid detail prefabs and trees without renderers are reported. A prototype library is optional; use it when you want specific detail sizes, colors, or tree settings instead of defaults.

The action registers prototypes only; click **Generate Foliage** to place them. Repeated auto-find runs skip existing entries. The summary reports how many unique rule prefabs were found and how many prototypes were added.

## Runtime-streamed rocks and GameObjects
1. Create **Assets > Create > Thomas > Terrain Foliage Placement Data**.
2. Assign it to **Runtime Placement Data** on the spawner.
3. In a rule's GameObject entry, set **Storage = Runtime Streamed**.
4. Generate foliage. The transforms are baked into the compact asset instead of creating thousands of scene objects.
5. The spawner creates/configures a `TerrainFoliageRuntimeStreamer`. At runtime it defaults to `Camera.main`, spatially indexes placements into chunks, pools instances, and only activates nearby objects.

Recommended first settings: activation 45 m, deactivation 55 m, chunk size 40 m, refresh 0.2 s, max active 300-500.

The first beta streams real GameObjects near the player. It does not yet GPU-instance the far-distance band.

# Terrain Foliage Spawner 0.4.0

Procedural Unity Terrain foliage painting for GameObjects, Terrain Details, and Terrain Trees.

## Multi-terrain prototype synchronisation

A foliage rule can now request a Detail or Tree prefab once and the spawner can append the matching prototype to every target Terrain before generation.

The synchroniser is intentionally append-only:

- existing prototypes are preserved;
- prototype indices are never reordered;
- no prototype is automatically deleted;
- duplicate prefab references are skipped.

## Recommended setup

For custom prototype settings (optional with rule auto-find enabled):

1. Choose one TerrainData asset as your prototype template.
2. Add and configure all grass, detail, tree, bush, and rock prototypes on that template Terrain.
3. Create **Assets > Create > Thomas > Terrain Foliage Prototype Library**.
4. Assign the template TerrainData to the library.
5. Assign the library to the Terrain Foliage Spawner.
6. Enable **Automatically Find Active Terrains**, or populate **Prototype Target Terrains** manually.
7. Press **Synchronise Required Prototypes Now**, or leave **Synchronise Prototypes Before Generation** enabled.

Only prototypes actually requested by enabled rule entries with a positive weight are copied.

## Source fallback

When **Allow Spawner Terrain Fallback** is enabled in the library, the spawner's assigned TerrainData can supply a prototype that is absent from the template. This is useful while migrating an existing scene, but a complete template TerrainData is safer for a large world.

## Important

Prototype synchronisation modifies TerrainData assets. Keep TerrainData in source control. The inspector records Undo and marks changed assets dirty, but version-control backups remain recommended.

## Existing features

- terrain-layer and slope filtering;
- weighted prefab entries;
- dense Terrain Detail patches;
- Terrain Tree output with LOD-capable prefabs;
- GameObject output;
- exclusion volumes and path masks;
- Unity Splines clearance;
- batched detail-map writes;
- validation and generation estimates.
