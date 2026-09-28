# GetLost Distant Forest v1.2.0

This update is aimed specifically at the "sharp sticker trees over a foggy mountain" look.

## What changed

- Source-tree foliage now samples a softer mip at distance.
- Alpha-tested leaf edges use derivative-based screen-space softening plus dithered coverage instead of one hard clip line.
- A small Alpha Cutoff Offset can fill tiny holes that become distracting at long range.
- The far-tree shader now responds to URP ambient + main-light colour so it sits in the same lighting world as the terrain.
- The far-tree shader directly reads the Get Lost custom fog globals and applies the same distance fog, height fog, colour preservation and sun scattering math.
- Scene View preview still works in Edit Mode.

## Upgrade

Overwrite the files from v1.1.0 with the files in this package. Keeping the existing folder/meta files is preferable if you already have references in the scene.

## Recommended starting values for the screenshot you sent

On your Distant Forest Profile:

- Texture Mip Bias: 1.0
- Edge Softness: 2.0 to 3.0
- Alpha Cutoff Offset: -0.04 to -0.08
- Scene Lighting Strength: 0.45 to 0.65
- Custom Fog Integration: 1.0
- Atmosphere Tint Strength: reduce to 0.05 to 0.15 now that the actual fog is integrated
- Brightness: around 1.0

If the distant forest becomes *more fogged* than the terrain, lower Custom Fog Integration toward 0.6-0.8. If it still sits dark and crisp on top of the mountain, raise it toward 1.2.

## Tuning order

1. Set Atmosphere Tint Strength low (0.05-0.15).
2. Match the forest to the terrain using Custom Fog Integration.
3. Raise Texture Mip Bias until leaf detail stops sparkling/sharpening at distance.
4. Raise Edge Softness until the skyline stops looking cut out.
5. Use Alpha Cutoff Offset only as a small finishing adjustment.

The source tree meshes are still the actual low-LOD meshes from each Terrain tree prototype; this version only changes how those meshes are shaded at long range.
