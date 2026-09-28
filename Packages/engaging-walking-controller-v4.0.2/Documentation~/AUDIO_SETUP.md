# Footstep Audio Setup

## Upgrade from 1.2.1

Install this package using Package Manager > Add package from tarball. The package ID and all existing movement component/field names are unchanged, so existing scene and prefab references are retained. New audio fields simply appear on your existing Surface Profile assets.

## Player setup

1. Select the player GameObject containing `WalkingMotor`.
2. Add `FootstepAudioManager`.
3. It creates two AudioSources automatically if they are missing.
4. Leave Trigger Mode on **Distance Fallback** for an FPS controller without visible leg animations.
5. For a full-body animated controller, select **Animation Events** and add `LeftFootstep` / `RightFootstep` events at foot contact frames.

## Surface setup

Open each existing `TerrainSurfaceProfile` asset and fill in:

- Footstep Clips
- Footstep Volume
- Minimum/Maximum Pitch
- Step Distance Multiplier
- Optional Slide Loop
- Optional Footstep Particle Prefab

The same surface asset already used for movement now controls audio and particles too.

## Meshes

Place `TerrainSurface` on bridges, rocks, floors or other mesh colliders and assign the same Surface Profile used for that material.

## Terrain

Your existing `TerrainLayerSurfaceMap` continues to map painted Terrain Layers to Surface Profiles. No additional audio raycast or map is required.

## Vegetation

Add `VegetationResistanceVolume` to a trigger around dense bushes. Assign a Surface Profile containing Vegetation Rustle Clips to its Vegetation Audio Profile field.
