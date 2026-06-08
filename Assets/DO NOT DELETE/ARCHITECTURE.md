# TimeCraft — Architecture Document

What the code is. Read this and the Prototype GDD at the start of each task.
Update whenever a script is added, changed, or removed.

Engine: Unity 6.3 LTS (6000.3.17f1). Render pipeline: URP. Language: C#.

Guiding architecture principle (from the Prototype GDD): simulation state and logic
live in plain C# (POCOs/structs), decoupled from MonoBehaviours and rendering, to
preserve a later DOTS/ECS migration path and an efficient time-rewind snapshot system.

## File / folder tree

```
Assets/
  Scripts/
    World/
      TerrainGenerator.cs
      GridData.cs
      GridManager.cs
```

## Script responsibilities (one line each)

- TerrainGenerator.cs — MonoBehaviour. Generates a procedural heightfield (layered
  Perlin noise) and builds the display mesh; holds the raw heightfield (`Heights`) and
  exposes `HeightAt(x, z)`, the post-multiplier local Y used by the mesh — the single
  place the height transform lives.
- GridData.cs — Plain C# (GridCell struct + GridData class). One cell per terrain quad,
  storing height, walkability (slope-based), and a reserved occupancy flag; provides
  cell<->local mapping. No MonoBehaviour, no rendering.
- GridManager.cs — MonoBehaviour adapter. Builds a GridData from TerrainGenerator
  heights, owns it (`Grid`), and draws an editor-only gizmo overlay of the cells.

## Interaction map

- GridManager (on the ProceduralTerrain GameObject) reads TerrainGenerator.HeightAt()
  to build GridData, and shares the object transform so the grid aligns with the mesh.
- GridData is standalone plain data; GridManager owns the instance and exposes it via
  `GridManager.Grid`.
- TerrainGenerator: standalone; emits/consumes no events.
- Grid is rebuilt manually for now (TerrainGenerator → Generate, then GridManager →
  Build Grid). Regenerating terrain requires a manual grid rebuild.
- Next planned consumers: pathfinding and building placement will query GridData
  (Walkable, CellToLocal, LocalToCell) and set the Occupied flag.

## Scene wiring

- GameObject "ProceduralTerrain" (Transform at origin): TerrainGenerator + MeshFilter +
  MeshRenderer (TerrainMat) + GridManager.
- Main Camera: orthographic, top-down (position (0,60,0), rotation (90,0,0)).
