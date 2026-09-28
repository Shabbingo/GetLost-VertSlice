# Multi-Terrain Setup

No Terrain references are required on the established trail prefab.

For permanent trails:

- TrailMeshBuilder Projection Mode should be Auto Terrain Chunks.
- TerrainFoliageClearance Auto Detect Terrain Chunks should be enabled.
- TerrainFoliageClearance Allow Runtime Clearing must be enabled.
- All Terrain chunks must be active when the trail is established.

TrailManager enforces the automatic terrain settings when it creates a trail.

A trail may cross any number of adjacent Terrain chunks. Each generated mesh
vertex is projected onto the chunk beneath it, and foliage clearance is applied
to every overlapped chunk.
