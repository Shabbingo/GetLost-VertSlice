# Changelog

## Unreleased
- Added opt-in relief-aware cliff formation distribution for rock rules.
- Added per-rule cliff sampling density, soft slope/drop thresholds, warped
  patch controls, preview feedback, and generation reporting.
- Added continuous cliff-face panels with dedicated prefab selection,
  contour-aware orientation, non-uniform macro scaling, spacing, and terrain
  embedding.

## 0.5.0
- Added Runtime Streamed storage mode for GameObject foliage entries.
- Added compact TerrainFoliagePlacementData assets.
- Added chunked distance-based TerrainFoliageRuntimeStreamer.
- Added object pooling, hysteresis, refresh throttling, active object caps, and per-refresh change budgets.
- Scene Objects remains available for permanently serialized objects.

# Changelog

## 0.4.0

- Added automatic append-only synchronisation of required Terrain Detail and Terrain Tree prototypes.
- Added support for synchronising the assigned Terrain, additional Terrain chunks, and all active Terrains.
- Added `TerrainFoliagePrototypeLibrary`, using a template TerrainData as the authoritative source for complete prototype settings.
- Added **Synchronise Required Prototypes Now** to the spawner inspector.
- Generation can synchronise prototypes automatically before sampling.
- Added cached prototype matching after synchronisation.
- Existing prototype arrays are never reordered or pruned, preserving painted detail maps and tree prototype indices.
- Added a synchronisation report for missing source prototypes and modified TerrainData assets.

## 0.3.2

- Added beta runtime spline detail clearance with reversible dirty-region updates.
