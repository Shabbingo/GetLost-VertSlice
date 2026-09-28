# Engaging Walking Controller — Setup

## 1. Install

In Unity, open **Window > Package Manager**.

Choose **+ > Add package from disk...** and select this package's `package.json` file.

## 2. Create the player hierarchy

Recommended hierarchy:

```text
Player
├── CameraPivot
│   └── Main Camera
```

On `Player`, add:

- CharacterController
- WalkingInputReader
- GroundProbe
- TerrainSurfaceResolver
- ExertionController
- WalkingMotor

On `CameraPivot`, add:

- FirstPersonLook
- WalkingCameraBob (optional)

Suggested CharacterController values:

- Height: 1.8
- Radius: 0.32
- Center Y: 0.9
- Step Offset: 0.35
- Slope Limit: 50
- Skin Width: 0.04

Assign the Main Camera transform to `WalkingMotor > Camera Transform`.
Assign Player to `FirstPersonLook > Yaw Target`.
Assign CameraPivot to `FirstPersonLook > Pitch Target`.

## 3. Input Actions

Create an Input Actions asset with a `Player` action map.

### Move

- Action Type: Value
- Control Type: Vector2
- Add a `2D Vector` composite
- Up: W
- Down: S
- Left: A
- Right: D
- Optional: Gamepad left stick

### Look

- Action Type: Value
- Control Type: Vector2
- Bindings:
  - Mouse delta
  - Gamepad right stick

### Sprint

- Action Type: Button
- Bindings:
  - Left Shift
  - Gamepad left stick press

### CarefulWalk

- Action Type: Button
- Bindings:
  - Left Ctrl
  - Gamepad left shoulder

### Jump

- Action Type: Button
- Bindings:
  - Space
  - Gamepad south button

Drag each action from the Input Actions asset into its matching field on `WalkingInputReader`.

## 4. Terrain surface modifiers

Create profiles from:

**Assets > Create > Walking Controller > Surface Profile**

Suggested starting values:

| Surface | Speed | Acceleration | Traction | Exertion | Slide |
|---|---:|---:|---:|---:|---:|
| Trail | 1.10 | 1.10 | 1.15 | 0.85 | 0.70 |
| Grass | 1.00 | 1.00 | 1.00 | 1.00 | 1.00 |
| Mud | 0.72 | 0.65 | 0.55 | 1.30 | 1.25 |
| Loose rock | 0.88 | 0.85 | 0.62 | 1.15 | 1.55 |
| Shallow water | 0.62 | 0.55 | 0.80 | 1.35 | 0.80 |
| Dense scrub | 0.55 | 0.60 | 0.90 | 1.50 | 0.90 |

Create a mapping asset from:

**Assets > Create > Walking Controller > Terrain Layer Surface Map**

Add each TerrainLayer used by your terrain and pair it with a Surface Profile. Assign the map to `TerrainSurfaceResolver`.

For mesh colliders or separate path objects, add `TerrainSurface` directly to the object and assign a profile.

## 5. Dense bushes

Add a trigger collider around a bush cluster and add `VegetationResistanceVolume`.

This produces resistance without making every leaf collider physical. Adjust the volume rather than placing colliders on individual branches.

## 6. First tuning pass

Test one small hillside containing:

- A winding trail
- A steep direct route
- Mud or loose rock
- Dense scrub
- A shallow creek

Tune until the winding trail is easier but longer, while the direct route is faster only when the player accepts more exertion and reduced control.

## Uncontrolled incline sliding

`WalkingMotor` now keeps slide velocity separate from normal movement and external impulses.
When the ground angle reaches the active slide threshold:

- `IsSliding` becomes true.
- Sprinting and jumping are disabled.
- Normal steering is reduced by `Slide Control`.
- The player accelerates down the slope until reaching `Maximum Slide Speed`.
- Slide velocity fades after reaching safe ground according to `Slide Recovery`.

Recommended starting values on `WalkingMotor`:

- Slide Start Angle: 38-45
- Slide Acceleration: 10-14
- Maximum Slide Speed: 8-10
- Slide Control: 0.05-0.15
- Slide Recovery: 8-12

For a slippery surface profile such as loose scree or mud:

- Traction: 0.35-0.65
- Downhill Slide Multiplier: 1.25-2.0
- Override Slide Start Angle: enabled
- Slide Start Angle: 25-35

For a stable trail:

- Traction: 1.0-1.25
- Downhill Slide Multiplier: 0
