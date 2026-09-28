# Camera Effects Stack (v4.0)

`CameraEffectsController` is now the only component that writes procedural motion to the Camera Effects Pivot.

## Upgrade existing players

1. Select the player in the Hierarchy.
2. Run **Tools > Walking Controller > Validate Selected Player**.
3. The utility adds `CameraEffectsController`, creates **Camera Effects Pivot**, and places the Camera beneath it.
4. Existing `FootingFeedback` and `WalkingCameraBob` components are connected automatically.

Expected hierarchy:

```
Player
└── Camera/Pitch parent (optional)
    └── Camera Effects Pivot
        └── Camera
```

Mouse look may continue to rotate the Camera or its existing pitch target. The effects stack rotates only **Camera Effects Pivot**, so the two systems no longer fight.

## Included modules

- `WalkingCameraBob`: contributes position bob.
- `FootingFeedback`: contributes low-footing sway and stumble rotation while retaining vignette/audio feedback.

Each module implements `ICameraEffect` and returns a `CameraEffectFrame`. Modules never edit Transform values directly.

## Adding another effect

Create a `MonoBehaviour` implementing `ICameraEffect`, return additive local position and Euler rotation, and place it under the same player hierarchy. Call `CameraEffectsController.RefreshEffects()` after adding modules at runtime.

## Reset behaviour

The controller rebuilds the pivot pose from its neutral position and rotation every LateUpdate. It uses self-centering damped springs and clamps the final offsets. Camera tilt cannot accumulate across frames.
