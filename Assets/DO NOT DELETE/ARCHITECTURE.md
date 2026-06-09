# TimeCraft — Architecture Document (v0.9)

What the code is. Read this and the Prototype GDD at the start of each task.
Update whenever a script is added, changed, or removed.

Engine: Unity 6.3 LTS (6000.3.17f1). Render pipeline: URP. Language: C#.

Guiding architecture principle: simulation state and logic live in plain C# (POCOs/
structs), decoupled from MonoBehaviours and rendering, to preserve a later DOTS/ECS
migration path and an efficient time-rewind snapshot system.

Versioning: this title carries the doc version. Each script header carries a
`// Version:` line bumped when that script changes.

Changes in v0.9: expanded the deferred movement note (natural routing / any-angle
smoothing) — no code change this turn.

Changes in v0.8: Stone/ore added. ResourceType gains Stone; ResourceManager scatters
stone nodes on unwalkable hill cells (not water) that have a walkable neighbour, so a
future Miner can stand adjacent to harvest (same access pattern as drinking beside
water). GridData.TryGetWalkableNeighbor provides that shared stand-on lookup.
Agent gains StoneCarried. No brain change yet — the single agent does not mine.
(v0.7 added water: IsWater cells, central basin, water plane, IsWaterAdjacent.)

## Deferred technical polish (not design — code behaviour; revisit when convenient)

- Smooth vertical movement: agents snap Y per cell because GridData.ContinuousToLocal
  samples the nearest cell's height (no interpolation). On hills this reads as the agent
  teleporting up/down. Fix = bilinear height sampling across the four surrounding cells.
- Diagonal + natural routing: Pathfinder is 4-connected, so paths are axis-only and hug
  obstacle corners in a staircase that reads as the agent "bumping" into an obstacle before
  turning. Two parts: (1) 8-connected movement (diagonal cost √2; block corner-cutting
  through unwalkable cells); (2) any-angle path smoothing / string-pulling (or Theta*) so
  routes round obstacles early instead of hugging them. Note: A* already plans the whole
  path up front, so this is a path-geometry/quality fix, not reactive obstacle detection.

## Time model

One continuous game-time source, advanced in FIXED steps (1/60 game-second) by
SimulationRunner. Two independent cadences:
- Continuous: agent movement (Speed cells/sec) and build/harvest timers. NOT tick-based.
- Discrete: day/stat tick fires every SecondsPerTick = secondsPerDay / ticksPerDay.
  Hunger drains each tick; stats and day clock are tick-driven.
`timeScale` (live) scales real→game time equally for both.
Current runner default: secondsPerDay = 1200, ticksPerDay = 24. NOTE: GDD S13 targets
1350 / 450; reconcile when per-tick stat rates are authored (Phase B / NeedsSystem).

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

## Script responsibilities (one line each)

- TerrainGenerator.cs — MonoBehaviour. Procedural heightfield (+ central basin) and
  display mesh; single source of truth for terrain height via HeightAt(x,z); exposes
  waterLevel (local-Y of the water surface).
- GridData.cs — Plain C#. One GridCell per quad: Height, Walkable, Occupied, IsWater;
  Build (water + slope classification), SetOccupied, IsWaterAdjacent (drink points),
  TryGetWalkableNeighbor (stand-on cell for mining/drinking), CellToLocal,
  ContinuousToLocal, LocalToCell.
- GridManager.cs — MonoBehaviour adapter. Builds/owns GridData (forwards waterLevel);
  Scene gizmo overlay (green walkable / red unwalkable / blue water); RebuildWaterPlane
  context menu spawns a water plane at waterLevel (edit-mode + Play; Game-view visible).
- Pathfinder.cs — Plain C# static. A* over GridData (4-connected, walkable only; water
  and hills are unwalkable so paths route around them). [Diagonal: see deferred-tech.]
- Agent.cs — Plain C#. NPC: continuous position (PosX/PosZ), CellX/CellZ, Speed,
  Hunger (per tick), inventory (Wood/Food/Stone Carried, CarryCapacity, InventoryFull).
- AgentManager.cs — MonoBehaviour bridge. Spawns one Agent + AgentBehavior; syncs
  capsule placeholder; exposes state/inventory/hunger to the Inspector.
- SimulationClock.cs — Plain C#. Tick counter; derives Day and TickOfDay.
- Simulation.cs — Plain C#. Sim root; owns Agents, ResourceNodes, StructureNodes,
  AgentBehaviors; Advance(dt) runs the advance order above.
- SimulationRunner.cs — MonoBehaviour bridge. Fixed-step loop; secondsPerDay +
  ticksPerDay (Play start); timeScale (live); exposes Sim.
- ResourceNode.cs — Plain C#. Passive sim data: Type (Wood/Food/Stone), cell, Amount,
  Depleted, Harvest().
- ResourceManager.cs — MonoBehaviour bridge. Seed-based scatter: Wood/Food on walkable
  cells (marked occupied), Stone on reachable unwalkable hill cells; placeholder
  primitives (brown cube=wood, green sphere=food, grey cube=stone).
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
  NOTE: this monolithic FSM is the v1 brain; Phase B replaces its fixed ordering with
  the GDD S7 needs/decision-priority model. Does not mine (no Miner job yet).

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
- Water/hills: GridData.Build flags IsWater (unwalkable); hill cells are unwalkable by
  slope. Pathfinder routes around both. TryGetWalkableNeighbor / IsWaterAdjacent give the
  adjacent stand-on cell the future Thirst need and Miner job will harvest from.

## Scene wiring

- "ProceduralTerrain": TerrainGenerator + MeshFilter + MeshRenderer + GridManager.
  Spawns a "WaterPlane" child (Rebuild Water Plane context menu / on Play).
- Main Camera: orthographic top-down (0,60,0), rotation (90,0,0).
- "Simulation": SimulationRunner.
- "Agents": AgentManager → runner=Simulation, gridManager=ProceduralTerrain.
- "Resources": ResourceManager → runner=Simulation, gridManager=ProceduralTerrain.
- "Structure": StructureManager → runner=Simulation, gridManager=ProceduralTerrain.
