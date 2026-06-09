# TimeCraft — Architecture Document (v0.6)

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
- Continuous: agent movement (Speed cells/sec) and build/harvest timers. NOT tick-based.
- Discrete: day/stat tick fires every SecondsPerTick = secondsPerDay / ticksPerDay.
  Hunger drains each tick; stats and day clock are tick-driven.
`timeScale` (live) scales real→game time equally for both.
Default: secondsPerDay = 1200 (20 real minutes at timeScale 1).

## Advance order (per fixed step)

1. Agent.Advance(dt)           — continuous movement
2. AgentBehavior.Update(dt)    — state-machine transitions, timers (harvest, build)
3. Tick cadence                — OnTick fires (hunger drain, future stats, day rollover)

## File / folder tree (actual project layout)

```
Assets/
  Scripts/
    World/
      TerrainGenerator.cs
      GridData.cs
      GridManager.cs
      Pathfinder.cs
      Agent.cs
      AgentManager.cs
    Simulation/
      SimulationClock.cs
      Simulation.cs
      SimulationRunner.cs
      ResourceNode.cs
      ResourceManager.cs
      StructureNode.cs
      StructureManager.cs
      AgentBehavior.cs
```

Note: Agent.cs and AgentManager.cs live in World/ per project convention.
GathererBehavior.cs is superseded by AgentBehavior.cs -- delete it from the project.

## Script responsibilities (one line each)

- TerrainGenerator.cs — MonoBehaviour. Procedural heightfield + display mesh; single
  source of truth for terrain height via HeightAt(x,z).
- GridData.cs — Plain C#. One GridCell per quad: height, walkability, occupancy;
  CellToLocal, ContinuousToLocal, SetOccupied.
- GridManager.cs — MonoBehaviour adapter. Builds and owns GridData; editor gizmo overlay.
- Pathfinder.cs — Plain C# static. A* over GridData (4-connected, walkable only).
- Agent.cs — Plain C#. NPC: continuous position (PosX/PosZ), CellX/CellZ, Speed,
  Hunger (float, drained per tick), inventory (Wood/FoodCarried, CarryCapacity).
- AgentManager.cs — MonoBehaviour bridge. Spawns one Agent + AgentBehavior; syncs
  capsule placeholder; exposes state/inventory/hunger to the Inspector.
- SimulationClock.cs — Plain C#. Tick counter; derives Day and TickOfDay.
- Simulation.cs — Plain C#. Sim root; owns Agents, ResourceNodes, StructureNodes,
  AgentBehaviors; Advance(dt) runs the advance order above.
- SimulationRunner.cs — MonoBehaviour bridge. Fixed-step loop; secondsPerDay +
  ticksPerDay (Play start); timeScale (live); exposes Sim.
- ResourceNode.cs — Plain C#. Passive sim data: Type, cell, Amount, Depleted, Harvest().
- ResourceManager.cs — MonoBehaviour bridge. Seed-based scatter; marks cells occupied;
  placeholder primitives (brown cube = wood, green sphere = food).
- StructureNode.cs — Plain C#. Build-site data: WoodRequired, WoodDeposited,
  BuildProgress (0..1 continuous timer), IsBuilt. DepositWood + AdvanceBuild.
- StructureManager.cs — MonoBehaviour bridge. Registers StructureNode in sim; marks cell
  occupied; spawns placeholder cube that animates (flat→tall) with BuildProgress.
- AgentBehavior.cs — Plain C#. Full NPC lifecycle FSM (10 states):
    SeekWood → MoveToWood → HarvestWood (10 s timer) →
    MoveToSite → Building (20 s timer, AdvanceBuild) →
    InHome → SeekFood → MoveToFood → HarvestFood (10 s timer, resets hunger) →
    ReturnHome → InHome (loop).
  Hunger drains each OnTick (tick-based); movement and timers are continuous.
  Replaces GathererBehavior (delete that file).

## Interaction map

- Sim spine: SimulationRunner → Simulation.Advance(FixedStep). OnTick / OnDayChanged
  are the event bus; stat drains and future systems subscribe.
- Agent lifecycle: AgentManager spawns Agent + AgentBehavior. Behavior owns all NPC
  logic; AgentManager only mirrors position and state to Unity.
- Resource loop: AgentBehavior.TrySeekResource → Pathfinder → agent.SetPath → arrive →
  harvestTimer → ResourceNode.Harvest → inventory → TryMoveToSite.
- Build loop: TryMoveToSite → StructureNode.DepositWood → AdvanceBuild each step →
  IsBuilt → InHome. StructureManager reads BuildProgress each frame for visuals.
- Hunger loop: OnTick → agent.Hunger += drain → InHome check → SeekFood → HarvestFood
  → agent.Hunger = 0 → ReturnHome → InHome.
- GridData.SetOccupied used by ResourceManager (resource cells) and StructureManager
  (build site). Pathfinder checks Walkable only so agents can reach occupied cells.

## Scene wiring

- "ProceduralTerrain": TerrainGenerator + MeshFilter + MeshRenderer + GridManager.
- Main Camera: orthographic top-down (0,60,0), rotation (90,0,0).
- "Simulation": SimulationRunner.
- "Agents": AgentManager → runner=Simulation, gridManager=ProceduralTerrain.
- "Resources": ResourceManager → runner=Simulation, gridManager=ProceduralTerrain.
- "Structure": StructureManager → runner=Simulation, gridManager=ProceduralTerrain.
