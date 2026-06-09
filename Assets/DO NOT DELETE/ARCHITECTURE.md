# TimeCraft — Architecture Document (v0.4)

What the code is. Read this and the Prototype GDD at the start of each task.
Update whenever a script is added, changed, or removed.

Engine: Unity 6.3 LTS (6000.3.17f1). Render pipeline: URP. Language: C#.

Guiding architecture principle: simulation state and logic live in plain C# (POCOs/
structs), decoupled from MonoBehaviours and rendering, to preserve a later DOTS/ECS
migration path and an efficient time-rewind snapshot system.

Versioning: this title carries the doc version. Each script header carries a
`// Version:` line bumped when that script changes.

## Time model

One continuous game-time source, advanced in FIXED steps (1/60 game-second) by
SimulationRunner. Two independent cadences:
- Continuous agent movement — Speed (cells/game-second) per agent; NOT tied to ticks.
- Discrete day/stat tick — fires every SecondsPerTick = secondsPerDay / ticksPerDay.
`timeScale` (live) scales real→game time equally for both. Default: secondsPerDay = 1200
(20 real minutes) at timeScale 1.

## File / folder tree

```
Assets/
  Scripts/
    World/
      TerrainGenerator.cs
      GridData.cs
      GridManager.cs
      Pathfinder.cs
    Simulation/
      SimulationClock.cs
      Simulation.cs
      SimulationRunner.cs
      Agent.cs
      AgentManager.cs
      ResourceNode.cs
      ResourceManager.cs
```

## Script responsibilities (one line each)

- TerrainGenerator.cs — MonoBehaviour. Procedural heightfield (layered Perlin noise) +
  display mesh; exposes `HeightAt(x,z)` — the single place the height transform lives.
- GridData.cs — Plain C# (GridCell struct + GridData class). One cell per quad: height,
  walkability, occupancy; cell<->local mapping; `ContinuousToLocal` for smooth movement;
  `SetOccupied` for clean struct mutation.
- GridManager.cs — MonoBehaviour adapter. Builds GridData from terrain heights, owns it
  (`Grid`), draws editor-only gizmo overlay.
- Pathfinder.cs — Plain C# static utility. A* over GridData (4-connected, walkable cells
  only). Stateless.
- SimulationClock.cs — Plain C#. Tick counter; derives `Day` and `TickOfDay`.
- Simulation.cs — Plain C#. Sim root; owns `Agents` and `ResourceNodes`; `Advance(dt)`
  moves agents continuously and fires OnTick/OnDayChanged on the derived cadence.
- SimulationRunner.cs — MonoBehaviour bridge. Fixed-step loop; `secondsPerDay` +
  `ticksPerDay` (Play start); `timeScale` (live); `running` pause; exposes `Sim`.
- Agent.cs — Plain C#. NPC: continuous cell-space position (`PosX`/`PosZ`), rounded
  `CellX`/`CellZ`, `Speed` (cells/sec), path walked via `Advance(dt)`.
- AgentManager.cs — MonoBehaviour bridge. Spawns one Agent, sets its Speed, syncs a
  placeholder capsule to the agent's continuous position each frame. Temporary patrol
  scaffolding until the job system replaces it.
- ResourceNode.cs — Plain C#. Passive sim data: `Type` (Wood/Food), cell position,
  `Amount`, `Depleted` flag, `Harvest(int)`. No ticking; no rendering.
- ResourceManager.cs — MonoBehaviour bridge. Scatters seed-based ResourceNodes onto
  walkable, unoccupied cells; marks those cells occupied; spawns placeholder primitives
  (brown cubes = wood, green spheres = food). No sim logic.

## Interaction map

- GridManager reads TerrainGenerator.HeightAt() to build GridData. Grid is rebuilt
  manually (TerrainGenerator → Generate, then GridManager → Build Grid).
- Simulation spine: SimulationRunner → Simulation.Advance(FixedStep) per frame.
  Simulation owns SimulationClock + Agents + ResourceNodes. OnTick / OnDayChanged are
  the event bus; future stat systems subscribe instead of running their own Update loops.
- Agent navigation: AgentManager reads GridManager.Grid + SimulationRunner.Sim (lazy
  init, start-order safe). Movement mirrors Agent.PosX/PosZ via ContinuousToLocal.
- Resource placement: ResourceManager reads GridManager.Grid + SimulationRunner.Sim
  (same lazy-init pattern). Calls Simulation.AddResourceNode and GridData.SetOccupied.
  Pathfinder still only checks Walkable, not Occupied, so agents can path through
  resource cells (and will harvest on arrival in the next slice).
- Next slice: gathering behaviour — agent detects nearest needed ResourceNode, paths to
  its cell, harvests over ticks (OnTick), carries stock toward a build site.

## Scene wiring

- "ProceduralTerrain": TerrainGenerator + MeshFilter + MeshRenderer (TerrainMat) +
  GridManager.
- Main Camera: orthographic, top-down (0,60,0), rotation (90,0,0).
- "Simulation" (empty): SimulationRunner.
- "Agents" (empty): AgentManager → runner=Simulation, gridManager=ProceduralTerrain.
- "Resources" (empty): ResourceManager → runner=Simulation, gridManager=ProceduralTerrain.
