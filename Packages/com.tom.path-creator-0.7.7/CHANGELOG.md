# Changelog

## [0.7.7] - 2026-07-24

### Fixed

- Terrain detail clearing now uses the actual dimensions returned by
  `TerrainData.GetDetailLayer`, preventing out-of-range indexing.
- Runtime detail backups now capture the same actual rectangle that is modified.
- Generated foliage scanning now traverses nested hierarchies recursively
  instead of assuming instances are direct grandchildren of the Terrain.
- Renderer-bearing nested foliage objects can now be detected and disabled.

## [0.7.6] - 2026-07-24

### Fixed

- Terrain detail clearance now calculates its affected detail-map rectangle
  directly in detail-pixel space.
- Removed unreliable world-bounds clipping that could produce an empty detail
  patch even when a trail crossed the Terrain.
- Added a one-pixel safety margin around the clearance radius to avoid leaving
  grass at rectangle edges.

## [0.7.5] - 2026-07-24

### Added

- Automatic Play Mode restoration for Unity Terrain trees.
- Rectangular per-layer backups for Terrain detail foliage.
- Restoration of generated foliage GameObjects disabled by trail clearance.
- `Restore Foliage After Play Mode` option on `TerrainFoliageClearance`.

### Changed

- Detail clearance now reads, modifies, and writes only the affected rectangle
  rather than each complete detail layer.
- Runtime generated-object `Destroy` mode falls back to `Disable` while
  restoration is enabled, because destroyed objects cannot be reconstructed.
- Terrain height and foliage restoration now share the same Play Mode exit hook.

## [0.7.4] - 2026-07-24

### Fixed

- Terrain height patches now restore explicitly on
  `PlayModeStateChange.ExitingPlayMode`.
- Restoration no longer relies only on hidden GameObject destruction callbacks.
- Added an application-quit fallback for runtime sessions.
- Improved compatibility with Enter Play Mode Options and domain reload
  configurations.

## [0.7.3] - 2026-07-24

### Changed

- Runtime terrain backups now store only the heightmap rectangles affected by
  trail deformation instead of copying complete Terrain heightmaps.
- Overlapping patches are restored in reverse order so the original terrain
  state is recovered correctly.
- Terrain backups now use `TerrainData` references directly and no longer call
  the deprecated `Object.GetInstanceID()` API.

### Improved

- Significantly lower backup memory use for trails that affect only a small
  portion of a large Terrain.
- Faster backup capture and restoration for typical trail-sized edits.

## [0.7.2] - 2026-07-24

### Added

- Shared runtime Terrain heightmap backup registry.
- Automatic restoration of deformed Terrain chunks when Play Mode ends.
- Backup deduplication when multiple trails modify the same TerrainData.
- `Restore Terrain After Play Mode` option on `TerrainTrailDeformer`.
- Manual `Restore Runtime Terrain Backups` component context command.

### Notes

- The current backup stores heightmaps only because trail deformation currently
  modifies only Terrain heights.
- Terrain painting and foliage systems retain their existing behaviour.

## [0.7.1] - 2026-07-24

### Fixed

- Fixed `TerrainTrailDeformer` compilation error caused by converting a `CentreSample` directly into a `Vector3` during heightmap bounds calculation.

## [0.7.0] - 2026-07-24

### Added

- `TerrainTrailDeformer` for runtime trail-bed cutting and filling.
- Automatic deformation across multiple active Terrain chunks.
- Configurable bed width, shoulder blend, cut depth, fill height, strength,
  sampling distance, and vertical offset.
- TrailManager terrain-deformation toggle.

### Changed

- Terrain deformation runs before permanent mesh projection and foliage clearing.
- TrailManager automatically adds a deformer when the established-trail prefab
  does not contain one.
- The deformer automatically matches its bed width to TrailMeshBuilder width.

## [0.6.0] - 2026-07-24

### Added

- Automatic Unity Terrain chunk detection.
- Trail meshes can project across multiple adjacent Terrain tiles.
- Runtime foliage clearance now affects every Terrain chunk overlapped by a trail.
- `TrailTerrainUtility` for terrain lookup and terrain-path overlap queries.
- `AutoTerrainChunks` projection mode.

### Changed

- New TrailMeshBuilder components default to automatic terrain chunk projection.
- TrailManager automatically configures established trails for multi-terrain projection and clearance.

## [0.5.2] - 2026-07-24

### Fixed

- Rebuilt the Unity Package Manager archive using regular files only.
- Removed hard-link tar entries that caused `TAR_ENTRY_ERROR` during installation.

## [0.5.1] - 2026-07-24

### Fixed

- Added missing Unity `.meta` files for `package.json` and all other package assets.
- Prevents immutable-package warnings when Unity imports the package.

## [0.5.0] - 2026-07-24

### Added

- `TrailManager` for converting the active candidate into a permanent trail.
- Optional established-trail prefab workflow.
- Automatic `Trail 001`, `Trail 002`, etc. naming.
- `TrailSession.TrailEstablished` event.
- `TrailSession.ResetForNextSession()`.
- `PathCreator.ClearPath()`.
- Automatic permanent mesh rebuild and optional runtime foliage clearance.
- Trail Manager setup guide in the Trail Session sample.

### Changed

- Establishing a candidate can now produce a permanent child beneath the
  configured `Established Trails` root.
- Abandoning or completing a managed session clears the temporary path.

## [0.4.0] - 2026-07-24

### Renamed

- `TrailRecorder` is now `TrailSession`.
- `TrailRecordingState` is now `TrailSessionState`.
- The active state is now named `Active` rather than `Recording`.
- The sample hierarchy now uses `Active Trail Session`.

### Gameplay API

- `BeginSession()`
- `PauseSession()`
- `ResumeSession()`
- `FinishSession()`
- `EstablishTrail()`
- `AbandonSession()`
- `AssignSessionTarget()`
- `LoadSessionPoints()`

### Migration

- The original Unity script GUID is preserved so existing component references
  can migrate with the package update.
- Renamed serialized fields use `FormerlySerializedAs`.
- Old public method and property names remain as obsolete compatibility wrappers
  for existing scripts and UnityEvent bindings.

### Changed

- Inspector labels and Play Mode controls now use player-facing session language.
- Package version increased to 0.4.0.

## [0.3.0] - 2026-07-24

### Added

- `TrailRecorder` runtime component.
- Runtime route recording from any assigned Transform.
- Start, pause, continue, stop, establish, and discard methods.
- Temporary and candidate recording states.
- Horizontal-distance sampling.
- Near-straight-point collapsing.
- Final Ramer-Douglas-Peucker route simplification.
- Assigned-Terrain and physics-layer projection.
- Optional temporary `LineRenderer` preview.
- Maximum-point runtime safety limit.
- `PathCreator.SetPathFromWorldPoints`.
- `PathCreator.SetOrAppendAnchorWorldPosition`.
- Trail recording sample setup guide.
- Custom TrailRecorder inspector with Play Mode test controls.

### Changed

- Recorded routes now drive `TrailData` lifecycle states.
- Package version increased to 0.3.0.

## [0.2.0] - 2026-07-24

### Added

- `TrailData` runtime component.
- Persistent unique trail ID.
- Trail name, description, and player-created flag.
- Lifecycle states: Recording, Candidate, Established, and Retired.
- Automatic path measurement refresh through `PathCreator.PathChanged`.
- Length, horizontal length, elevation gain/loss, elevation range, average
  grade, and maximum grade statistics.
- Visit, popularity, wear, establishment-progress, and future quality-score
  fields.
- Runtime methods for establishing trails and registering visits.
- Custom inspector with measurement summary and lifecycle shortcuts.

### Architecture

- `TrailData` is non-destructive and does not alter terrain, foliage, or mesh
  content.
- Future trail recording, wear, painting, carving, rewards, saving, and
  multiplayer systems can share this single source of truth.

## [0.1.0] - 2026-07-24

### Changed

- Generated foliage clearance is now purpose-built for:
  `Generated Foliage → Foliage Rule → Generated Instance`.
- Removed collider-based generated foliage discovery.
- Generated instances are now iterated directly beneath each foliage-rule holder.
- Rule and instance names no longer matter.

### Added

- Renderer-bounds-centre testing for better placement accuracy.
- Transform-position fallback for generated objects without renderers.
- Inspector preview count for generated instances inside the clearance radius.

### Safety

- The Generated Foliage root is never modified.
- Foliage-rule holders are never modified.
- No unrelated scene objects are scanned.

## [0.0.10] - 2026-07-24

### Fixed

- Generated foliage clearance now supports foliage-rule holder objects.
- Each generated foliage instance is evaluated independently.
- The first collider under a rule no longer decides the result for every instance.
- Foliage-rule holder objects can no longer be disabled or destroyed.

### Changed

- Collider ownership now resolves to the generated instance directly beneath a foliage-rule holder.

## [0.0.9] - 2026-07-24

### Changed

- Scene-object clearance now searches only beneath the assigned Terrain's `Generated foliage` child.
- General scene-wide collider scanning has been removed.
- Layers and tags are no longer required for generated foliage clearance.
- Child colliders resolve to their top-level generated foliage object.

### Added

- Automatic recursive lookup for `Generated foliage`.
- Manual generated foliage root assignment.
- Inspector button to locate the generated foliage root.

### Safety

- Objects outside the generated foliage container cannot be cleared.
- Terrain, path, trail, and unrelated scene objects remain protected.

## [0.0.8] - 2026-07-24

### Fixed

- Terrain objects and TerrainColliders can no longer be disabled or destroyed.
- Assigned path and package component objects are now always protected.

### Changed

- Scene Object Layers now defaults to an empty mask for safety.
- Scene GameObjects must be explicitly opted into clearance.

### Added

- Inspector preview count for scene objects matching the current filters.
- Central safety filtering for all scene-object clearance operations.

## [0.0.7] - 2026-07-24

### Fixed

- Terrain detail clearing now scans the full detail map reliably.
- Detail-cell world positions now use cell centres for more accurate clearance.

### Added

- Optional scene GameObject clearance.
- Disable or Destroy modes for scene objects.
- Included scene-object layer mask.
- Ignored object layer mask.
- Ignored tag list.
- Vertical tolerance for scene-object clearance.
- Optional inactive-object searching.
- Backup and restoration for disabled GameObjects.
- Inspector controls for restoring disabled objects independently.

## [0.0.6] - 2026-07-24

### Fixed

- Trail meshes no longer climb over tree, rock, or prop colliders when using Assigned Terrain projection.
- Terrain projection now uses `Terrain.SampleHeight` directly.

### Added

- Projection modes: None, Assigned Terrain, and Physics Layers.
- Dedicated Terrain assignment on `TrailMeshBuilder`.
- Inspector warnings for unsafe or incomplete projection setup.

## [0.0.5] - 2026-07-24

### Added

- `TerrainFoliageClearance` component.
- Unity Terrain tree removal around paths.
- Unity Terrain detail removal and optional edge falloff.
- Configurable clearance radius and sample spacing.
- Terrain foliage backup, refresh, restore and clear controls.
- Optional runtime clearing.
- Scene-view clearance gizmos.
- Custom foliage-clearance inspector.

## [0.0.4] - 2026-07-24

### Added

- `TrailMeshBuilder` runtime component.
- Adjustable trail width and path sampling.
- UV tiling based on travelled distance.
- Optional projection onto terrain and other colliders.
- Vertical trail offset to reduce z-fighting.
- Automatic editor and runtime rebuilding controls.
- Manual mesh rebuild and clear buttons.
- Custom Trail Mesh Builder inspector.

## [0.0.3] - 2026-07-24

### Fixed

- Removed an invalid leading character from `PathCreator.cs`.
- Removed an invalid leading character from `PathCreatorEditor.cs`.
- Removed the unnecessary `Samples~.meta` file that caused Unity to create a phantom folder.

## [0.0.2] - 2026-07-24

### Added

- Free, aligned, mirrored and automatic anchor handle modes.
- Scene-view anchor selection.
- Insert anchor after the selected anchor.
- Delete selected anchor.
- Closed-loop path support.
- Approximate total path length.
- World-space path bounds.
- Stable sampled-path cache.
- Improved undo and redo handling.
- Selected-anchor inspector controls.

### Changed

- Path data is now stored as anchors with explicit incoming and outgoing handles.
- Scene editing is clearer and scales better for longer trails.

## [0.0.1] - 2026-07-24

### Added

- Initial Unity package structure.
- Cubic Bézier path evaluation.
- Runtime path sampling.
- Evenly spaced point generation.
- Scene-view point editing.
- Gizmo path preview.
