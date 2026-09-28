# Engaging Walking Controller v4.0.2

## Slip feedback update

Slip feedback now reads as the player losing support rather than receiving an aim punch. A full slide produces a fast downward body drop, a small forward movement, downward pitch and directional roll, followed by a slower recovery. Minor footing losses use a reduced version of the same motion.

Recommended defaults are built in:

- Vertical dip: `0.18`
- Forward dip: `0.07`
- Pitch down: `9 degrees`
- Roll: `4.5 degrees`
- Drop time: `0.09 seconds`
- Recovery time: `0.45 seconds`

An upgrade-safe Unity 6 package for terrain-focused first-person traversal.

## New in 2.0

- Unified Surface Profile for movement, footsteps, sliding audio and particles
- Distance-based FPS footsteps
- Animation-event footstep mode
- Shuffle-bag clip randomisation
- Walk, sprint and careful-walk volume/cadence differences
- Looping surface-specific slide sounds
- Optional footstep particle prefabs
- Vegetation rustle support

See `Documentation~/AUDIO_SETUP.md` and `Documentation~/SETUP.md`.

## v2.1 Terrain Detail vegetation

Add `TerrainDetailVegetationDetector` to the player and assign a `TerrainDetailVegetationProfile`. Bushes painted with Unity Terrain Details can now smoothly slow movement, increase exertion and play rustles without per-instance colliders. See `Documentation~/TERRAIN_DETAIL_VEGETATION.md`.


## Unified vegetation (v3.0)

See `Documentation~/UNIFIED_VEGETATION.md`. Painted Terrain Details are now mapped by their actual prefab or texture asset and share the same `VegetationProfile` system as trigger volumes.


## Sprint-sensitive sliding (v3.1.0)

`WalkingMotor` now includes **Sprint Slide Increase** under **Uncontrolled Sliding**. Higher values lower the slope angle needed to begin sliding while sprint is held and increase the resulting slide acceleration. A value of `0` disables the sprint bonus; `0.5` is a useful starting point; `1` is intentionally aggressive. Careful walking still applies its separate slide reduction.

## Hidden Footing (v3.2)

The controller now builds and recovers hidden footing based on slope, sprinting, downhill direction, sharp turns and surface traction. See `Documentation~/HIDDEN_FOOTING.md`.

## Hidden footing feedback (v4.0)

Add `FootingFeedback` to the player, or run `Tools > Walking Controller > Validate Selected Player`. It provides subtle camera instability, a runtime-generated peripheral vignette, directional stumble kicks, and optional scuff audio. See `Documentation~/FOOTING_FEEDBACK.md`.

## v4.0 camera architecture

Camera motion is now mixed by `CameraEffectsController`. Run **Tools > Walking Controller > Validate Selected Player** once after upgrading to create the Camera Effects Pivot and migrate footing/bob feedback. See `Documentation~/CAMERA_EFFECTS_STACK.md`.