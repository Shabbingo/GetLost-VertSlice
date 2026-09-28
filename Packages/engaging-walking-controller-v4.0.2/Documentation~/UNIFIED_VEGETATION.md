# Unified Vegetation Setup (v3.0)

The framework now uses one `VegetationProfile` for both painted Terrain details and trigger volumes.

## Player setup

Add these components to the same GameObject as `WalkingMotor`:

- `VegetationInteractionReceiver`
- `TerrainDetailVegetationProvider` (for painted Terrain Details)
- `FootstepAudioManager` (optional, for rustles)

Run **Tools > Walking Controller > Validate Selected Player** to add and wire the receiver automatically.

Remove the old `TerrainDetailVegetationDetector` after migrating.

## Create a vegetation profile

Create: **Assets > Create > Walking Controller > Vegetation > Vegetation Profile**

Suggested bush values:

- Resistance: 0.45
- Additional Exertion: 0.30
- Audio Profile: a `TerrainSurfaceProfile` containing bush rustle clips

## Map painted Terrain Details by asset

Create: **Assets > Create > Walking Controller > Vegetation > Terrain Detail Mapping**

For each entry:

1. Drag the exact prefab used by a mesh Terrain Detail, or the exact texture used by a grass/detail texture.
2. Assign the matching `VegetationProfile`.

Assign this mapping asset to `TerrainDetailVegetationProvider` on the player.

Mappings use asset references, not detail-layer indices. Reordering detail prototypes or using a different order on another Terrain will not break the setup, provided the same prefab/texture assets are used.

Recommended provider values:

- Sample Radius: 0.75
- Sample Interval: 0.15
- Density For Full Effect: 8

Lower `Density For Full Effect` if sparse bushes barely register. Raise it if light foliage immediately reaches full strength.

## Trigger-volume vegetation

For a manually placed bush or scrub volume:

1. Add a trigger collider.
2. Add `VegetationResistanceVolume`.
3. Assign the same `VegetationProfile` used by painted foliage.
4. Leave Push Strength at 0 unless a physical outward push is desired.

The trigger provider and Terrain Detail provider both feed the same receiver, so movement, exertion and rustle audio remain consistent.
