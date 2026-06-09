# TimeCraft — Architecture Document (v0.3)

What the code is. Read this and the Prototype GDD at the start of each task.
Update whenever a script is added, changed, or removed.

Engine: Unity 6.3 LTS (6000.3.17f1). Render pipeline: URP. Language: C#.

Guiding architecture principle (from the Prototype GDD): simulation state and logic
live in plain C# (POCOs/structs), decoupled from MonoBehaviours and rendering, to
preserve a later DOTS/ECS migration path and an efficient time-rewind snapshot system.

Versioning: this title carries the doc version. Each script's header carries a
`// Version:` line bumped when that script changes.

## Time model (v0.3)

One continuous game-time source, advanced in FIXED steps (1/60 game-second) by
SimulationRunner via a real-time accumulator (frame-rate independent, deterministic).
Two independent cadences derive from it:
- Continuous agent movement — advanced every fixed step; each agent moves at its own
  Speed (cells/game-second). NOT tied to the tick rate.
- Discrete day/stat tick — fires every SecondsPerTick = secondsPerDay / ticksPerDay of
  game-time; carries hunger and other over-time stat drains (none implemented yet).
`timeScale` is a live global multiplier (real -> game time) that speeds the whole sim
equally. Default day length: secondsPerDay = 1200 (20 real minutes) at timeScale 1.

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
```

## Script responsibilities (one line each)

- TerrainGenerator.cs — MonoBehaviour. Generates a procedural heightfield (layered
  Perlin noise) and builds the display mesh; holds `Heights` and exposes `HeightAt(x, z)`
  — the single place the height transform lives.
- GridData.cs — Plain C# (GridCell struct + GridData class). One cell per terrain quad
  (height, walkability, occupancy); cell<->local mapping plus `ContinuousToLocal(gx, gz)`
  for smooth movement. No MonoBehaviour, no rendering.
- GridManager.cs — MonoBehaviour adapter. Builds a GridData from TerrainGenerator
  heights, owns it (`Grid`), and draws an editor-only gizmo overlay of the cells.
- Pathfinder.cs — Plain C# static utility. A* over GridData (4-connected, walkable cells
  only); `FindPath(grid, start, goal)` returns the cell path or null. Stateless.
- SimulationClock.cs — Plain C#. Counts logical ticks and derives `Day` (1-based) and
  `TickOfDay` (0-based). `Advance()` steps one tick, reports day rollover. No Unity types.
- Simulation.cs — Plain C#. Root of the sim; owns the SimulationClock and the `Agents`
  list. `Advance(double dt)` moves agents continuously and fires `OnTick` / `OnDayChanged`
  on the derived tick cadence. `AddAgent(x, z)` spawns an agent. Exposes `SecondsPerTick`.
  No MonoBehaviour, no UnityEngine.
- SimulationRunner.cs — MonoBehaviour bridge (the only Unity-aware pacing piece). Owns a
  Simulation, converts real time to fixed game-time steps (1/60 s), exposes inspector
  controls (secondsPerDay + ticksPerDay at Play start; timeScale live; running pause) and
  live read-outs. Exposes `Sim`. `Step One Second` context menu advances 1 game-second.
- Agent.cs — Plain C#. One NPC: continuous cell-space position (`PosX`, `PosZ`; rounded
  `CellX`/`CellZ`), a `Speed` (cells/sec), and a path it walks via `Advance(dt)`.
  Deliberately "dumb"; `SetPath()` is driven by higher-level behaviour. No MonoBehaviour.
- AgentManager.cs — MonoBehaviour bridge. Lazily spawns one Agent into the Simulation
  (setting its Speed), spawns a placeholder capsule, and each frame snaps it to the
  agent's continuous position (smooth, no teleport). Holds a TEMPORARY start<->target
  patrol as test scaffolding. No sim logic.

## Interaction map

- GridManager (on ProceduralTerrain) reads TerrainGenerator.HeightAt() to build GridData,
  owns it (`GridManager.Grid`), shares the object transform. GridData is standalone data.
- TerrainGenerator: standalone; emits/consumes no events. Grid is rebuilt manually
  (TerrainGenerator → Generate, then GridManager → Build Grid).
- Simulation spine: SimulationRunner (Unity) → Simulation.Advance(FixedStep) repeatedly
  per frame. Simulation owns SimulationClock + Agents; movement advances every step,
  ticks fire on the derived cadence. Simulation emits OnTick / OnDayChanged; future
  systems (needs, gathering, construction) subscribe rather than running Update loops.
  Simulation/SimulationClock/Agent hold no Unity references beyond value types.
- Agent navigation: AgentManager reads GridManager.Grid + SimulationRunner.Sim (lazy
  init, start-order safe), spawns an Agent (Simulation.AddAgent, sets Speed), calls
  Pathfinder.FindPath to set its path. The Agent advances continuously each sim step;
  AgentManager mirrors Agent.PosX/PosZ to the placeholder via GridData.ContinuousToLocal
  + the terrain transform every frame.
- Next planned consumers: resource nodes and building placement will query/set GridData
  (Walkable, Occupied) and supply path targets to agents in place of the patrol; stat
  systems (hunger) will subscribe to Simulation.OnTick.

## Scene wiring

- GameObject "ProceduralTerrain" (Transform at origin): TerrainGenerator + MeshFilter +
  MeshRenderer (TerrainMat) + GridManager.
- Main Camera: orthographic, top-down (position (0,60,0), rotation (90,0,0)).
- GameObject "Simulation" (empty): SimulationRunner. Drives the clock; no spatial role.
- GameObject "Agents" (empty): AgentManager, with `runner` → the Simulation object and
  `gridManager` → the ProceduralTerrain object. Spawns/syncs the placeholder agent.
