# Wagon prototype — iteration 1

## Play it

Open **Assets/Scenes/PlayLoop/2_WorldScene.unity** and enter Play Mode.
A simple wooden wagon spawns in a clear spot near the player. It follows the
initial mission-setup teleport until first used; afterward it stays where left.

- Walk between the two bars and press **E** to grab/release.
- Use the normal movement controls to pull, turn, or reverse. Looking still uses
  the existing first-person camera. Jumping is disabled while holding.
- Grabbing opens the gravel gate. **G** toggles it for transport without building.
- Release the wagon to apply its wheel brakes.
- **R**, while released and near the wagon, refills gravel. This is a temporary
  prototype supply interaction, not a finished inventory/refill mechanic.

The runtime hierarchy has **Wagon Prototype** (controller and world state),
with an independent chassis, front axle, four wheel bodies, and two pulling bars.
Select this object during Play Mode to tune load, force, or deposition settings.
The generated geometry is a placeholder, not final art.

## What is implemented

- Free-rolling convex wheel colliders and hinge joints. Front axle steering is
  limited to 48 degrees and axle rocking to 16 degrees. Rear wheel axles stay
  aligned with the chassis.
- Finite pulling force, load-dependent mass and movement speed, and collision-tested
  player travel constrained to the handle position. Small obstacles lift the wheels;
  larger obstacles can stall them, and the chassis can ground out.
- A 120-unit gravel bin, one unit per metre by default, visible load level,
  and a movable rear gate.
- Small live gravel strips behind the rear outlet. Closed gate, empty bin,
  release, unsupported ground, and teleports break deposition continuity.
- Local terrain deformation (15 cm maximum cut / 12 cm fill relative to original
  ground), gravel texture, terrain grass and managed-grass clearance.
- **Trees, spawned objects, and mesh terrain-detail prototypes are preserved.**
  Conservatively preserving mesh details also preserves any small plants represented
  that way. The prototype cannot distinguish those from mesh rocks automatically.
- Repeated passes do not repeatedly excavate ground. Previously covered centreline
  sections do not spend gravel again; overlap handling is approximate at edges.
- Source TerrainData is cloned at runtime. Leaving Play Mode or reloading the
  scene discards the clones instead of changing terrain assets.
- Save version 3 includes wagon position/load and all deposited strips, including
  unfinished work and disconnected sections. Older saves remain readable.
- Survey completion keeps existing scores/results, but no longer constructs
  the walked route or shows its build-loading screen for wagon-era surveys.
  Startup foliage loading remains intact.

## Integration

WagonPrototypeBootstrap installs only in 2_WorldScene; it creates the wagon
and attaches WagonPlayerLink to the existing WalkingMotor. No scene YAML edits
or manual wiring are required. Other scenes can create it explicitly through
WagonPrototypeBootstrap.EnsureCreated().

WalkingMotor still owns CharacterController movement. Its optional displacement
filter prevents the player from escaping a blocked wagon; the wagon receives
a capped physical force at the handles. Existing free walking is unchanged.

WagonGravelSurface accepts a small strip independently of input or the wagon.
This is the extension point for a future shovel depositing short patches.
WagonWorld owns stock consumption, coverage, disconnected strips, and restoration.

The terrain layer index is inherited from the active TrailManager settings
(current project asset: index 4). Terrain without that layer does not consume
gravel or receive a path.

## Deliberately deferred

Wheel loosening, frame damage/repairs, saw and shovel interactions, physical tool
inventory, upgrade purchasing, final sound/animation/art, and exact path-area
accounting. Scoring still measures the existing player survey route, not gravel
coverage. Those systems can be adjusted after testing the hauling loop.

This iteration uses physical friction rather than per-texture wagon traction.
It needs hands-on tuning in the full world; isolated physics checks do not establish
frame-time performance on the project's large terrain and vegetation population.

## Validation

Run **pwsh -File Tools/WagonValidation/Validate.ps1** from the repository to compile
and run the isolated checks. It uses the installed editor matching generated project
files, and leaves the main Unity editor open.

Tools/WagonValidation/WagonValidation.cs is a batch-only Unity validation harness
for an isolated project containing the compiled gameplay assemblies and their
dependencies. It checks handle constraints, terrain edits, repeated-pass stability,
save data, low obstacles, impassable wheel obstacles, and reversing free.

The implementation was compiled against Unity 6000.5.3f1 for WalkingController,
GetLost.Trails and Assembly-CSharp. The isolated validation project and compiler
outputs live under ignored Temp/WagonValidation.


## Runtime performance

Grass clearance removes only the affected instances from existing draw batches;
it no longer rebuilds the whole grass population or recreates materials per strip.
The active-renderer registry replaces scene-wide searches. Full 0.4 m samples carry
their remainder forward instead of splitting nearly every sample into two edits.

Terrain deformation, texture paint, managed grass, terrain details and heightmap
synchronization are combined into six-metre chunks and run as separate stages.
The default 0.08-second stage delay prevents terrain subsystems from stacking on
adjacent frames. Short final sections flush after 2.5 seconds or when the gate
closes. These values are configurable on WagonWorld.

Unity Profiler markers: Wagon.Gravel.Deform, Wagon.Gravel.Paint,
Wagon.Gravel.ManagedGrass, Wagon.Gravel.TerrainDetails and
Wagon.Gravel.SyncHeightmap.

On the isolated 102,400-instance benchmark, median local grass-clearance time
dropped from 166 ms to about 1.3 ms. This measures that operation, not total frame
time in the full world. The validation suite also checks compacted multi-mesh
draw batches, grass restoration and coalesced terrain synchronization.
