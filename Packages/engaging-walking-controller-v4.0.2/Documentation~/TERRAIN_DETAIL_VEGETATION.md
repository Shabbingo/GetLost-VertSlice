# Terrain Detail Vegetation Setup

This system lets Unity Terrain Details slow the player and play bush rustles without placing colliders on every plant.

## 1. Make the painted bush visual-only

Use a prefab with renderers/LOD only. Remove Collider, Rigidbody and VegetationResistanceVolume from the prefab used by Paint Details.

## 2. Create the mapping asset

In the Project window choose:

`Create > Walking Controller > Terrain Detail Vegetation Profile`

Add one rule per interactive Terrain detail layer.

- Detail Layer Index: the order of the prototype in Terrain > Paint Details, starting at 0.
- Resistance: contribution at full density. Start around 0.25 for ferns, 0.45 for bushes and 0.7 for dense scrub.
- Additional Exertion: extra exertion at full density.
- Vegetation Audio Profile: a Surface Profile containing rustle clips.

## 3. Add the detector

Add `TerrainDetailVegetationDetector` to the same player object as `WalkingMotor`.

Assign the mapping asset. Terrain Override can remain empty; the detector finds the active Terrain beneath the player.

Recommended starting values:

- Sample Radius: 0.75
- Sample Interval: 0.15
- Density For Full Effect: 8
- Minimum Speed Multiplier: 0.55
- Enter Smoothing: 8
- Exit Smoothing: 5

## 4. Tune density

Watch `Current Strength` in Play Mode. If it remains very low inside thick foliage, reduce Density For Full Effect. If light grass reaches full strength, increase it.

The detector blends all configured detail layers, slows the WalkingMotor, increases exertion, and sends continuous rustle intensity to FootstepAudioManager.

## Terrain Trees

Use Terrain Details for bushes and low scrub. Use Terrain Trees or ordinary GameObjects for large trunks that need solid collision. Do not use trigger colliders on painted Terrain vegetation.
