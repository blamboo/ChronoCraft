# TimeCraft — Architecture Document (v0.13)

What the code is. Read this and the Prototype GDD at the start of each task.
Update whenever a script is added, changed, or removed.

Engine: Unity 6.3 LTS (6000.3.17f1). Render pipeline: URP. Language: C#.

Guiding architecture principle: simulation state and logic live in plain C# (POCOs/
structs), decoupled from MonoBehaviours and rendering, to preserve a later DOTS/ECS
migration path and an efficient time-rewind snapshot system.

Versioning: this title carries the doc version. Each script header carries a
`// Version:` line bumped when that script changes.

Changes in v0.13: per-agent debug read-out (Phase B, B1). AgentView mirrors each
agent's live state (civ, current action, hunger, inventory, cell) into the Inspector;
AgentManager attaches and binds one to each capsule at spawn. Instrument only -- no
behavior change. Next: B2 = NeedsSystem + DecisionSystem (continuous need-driven brain).

Changes in v0.12: per-civ structures (A3b complete). StructureNode carries a CivId;
Simulation gains a civ registry (Civs / RegisterCiv) seeded by AgentManager with each
civ's spawn anchor; StructureManager places one structure per civ near its anchor and
animates each; AgentBehavior builds/shelters only at its OWN civ's structure. The two
civs no longer share a structure -- each forms its own camp.

Changes in v0.11: resource reservation. ResourceNode gains ClaimedBy / TryClaim /
Release; AgentBehavior targets the nearest UNCLAIMED node, reserves it, and releases it
when done, so only one agent gathers a node at a time (no resource clumping).

Changes in v0.10: multi-civ agents. CivId added; AgentManager now spawns agentsPerCiv
agents for Civ1 and Civ2 at opposite edges, tinted by civ; the v1 hunger-drain default
was retuned (10 -> 0.25/tick) for ticksPerDay 450. TRANSITIONAL (A3a): both civs still
run the v1 brain against the single shared structure and shared resource nodes, so they
clump at one structure; per-civ structures are A3b.

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
Runner set to secondsPerDay = 1350, ticksPerDay = 450 (GDD S13). Per-tick rates are
authored against 450 ticks/day: hunger-drain default is 0.25/tick (AgentManager). Night
begins at TickOfDay >= 225 once the schedule (Phase B) is wired.

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
      AgentView.cs
    Simulation/
      Civ.cs
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
- Agent.cs — Plain C#. NPC: Civ (CivId), continuous position (PosX/PosZ), CellX/CellZ,
  Speed, Hunger (per tick), inventory (Wood/Food/Stone Carried, CarryCapacity).
- AgentManager.cs — MonoBehaviour bridge. Registers each civ's spawn anchor in the sim,
  spawns agentsPerCiv agents for Civ1 and Civ2 at opposite edges (ring-out from each
  anchor), tints capsules by civ, attaches an AgentBehavior + an AgentView to each, syncs
  all capsules.
- AgentView.cs — MonoBehaviour, debug instrument. Attached per capsule; mirrors its
  agent's live state (civ, action, hunger, inventory, cell) into the Inspector. Read-only.
- Civ.cs — Plain C#. CivId enum (None/Civ1/Civ2) + CivState (per-civ record with spawn
  anchor). Civ identity carried by Agent and StructureNode.
- SimulationClock.cs — Plain C#. Tick counter; derives Day and TickOfDay.
- Simulation.cs — Plain C#. Sim root; owns Civs (registry + RegisterCiv), Agents,
  ResourceNodes, StructureNodes, AgentBehaviors; AddStructureNode takes a CivId;
  Advance(dt) runs the advance order above.
- SimulationRunner.cs — MonoBehaviour bridge. Fixed-step loop; secondsPerDay +
  ticksPerDay (Play start); timeScale (live); exposes Sim.
- ResourceNode.cs — Plain C#. Passive sim data: Type (Wood/Food/Stone), cell, Amount,
  Depleted, Harvest(); single-agent reservation (ClaimedBy, TryClaim, Release).
- ResourceManager.cs — MonoBehaviour bridge. Seed-based scatter: Wood/Food on walkable
  cells (marked occupied), Stone on reachable unwalkable hill cells; placeholder
  primitives (brown cube=wood, green sphere=food, grey cube=stone).
- StructureNode.cs — Plain C#. Build-site data: Civ (owner), WoodRequired, WoodDeposited,
  BuildProgress (0..1 continuous timer), IsBuilt. DepositWood + AdvanceBuild.
- StructureManager.cs — MonoBehaviour bridge. Once sim.Civs is populated, places ONE
  StructureNode per civ on a free walkable cell near that civ's anchor, marks it occupied,
  and spawns/animates a placeholder cube per site (flat→tall with BuildProgress).
- AgentBehavior.cs — Plain C#. Full NPC lifecycle FSM (10 states):
    SeekWood → MoveToWood → HarvestWood (10 s timer) →
    MoveToSite → Building (20 s timer, AdvanceBuild) →
    InHome → SeekFood → MoveToFood → HarvestFood (10 s timer, resets hunger) →
    ReturnHome → InHome (loop).
  Hunger drains each OnTick (tick-based); movement and timers are continuous.
  Structure lookups are civ-scoped (builds/shelters at agent.Civ's own structure).
  NOTE: this monolithic FSM is the v1 brain; Phase B replaces its fixed ordering with
  the GDD S7 needs/decision-priority model. Does not mine (no Miner job yet).

## Interaction map

- Sim spine: SimulationRunner → Simulation.Advance(FixedStep). OnTick / OnDayChanged
  are the event bus; stat drains and future systems subscribe.
- Agent lifecycle: AgentManager registers civ anchors, then spawns 12 agents per civ
  (Civ1/Civ2), each with its own AgentBehavior. Behavior owns all NPC logic; AgentManager
  mirrors positions. Resource nodes are shared/contested; structures are civ-scoped.
- Resource loop: AgentBehavior.TrySeekResource picks the nearest UNCLAIMED node →
  ResourceNode.TryClaim → Pathfinder → agent.SetPath → arrive → harvestTimer →
  ResourceNode.Harvest → ReleaseNode → inventory → TryMoveToSite. One agent per node.
- Build loop: TryMoveToSite finds the agent's OWN-civ unbuilt structure → DepositWood →
  AdvanceBuild each step → IsBuilt → InHome (own-civ built structure). StructureManager
  reads each site's BuildProgress for visuals.
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
  Spawns one structure cube per civ at runtime (placed near each civ's spawn anchor).
