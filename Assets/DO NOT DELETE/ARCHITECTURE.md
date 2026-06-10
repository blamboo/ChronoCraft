# TimeCraft — Architecture Document (v0.16)

What the code is. Read this and the Prototype GDD at the start of each task.
Update whenever a script is added, changed, or removed.

Engine: Unity 6.3 LTS (6000.3.17f1). Render pipeline: URP. Language: C#.

Guiding architecture principle: simulation state and logic live in plain C# (POCOs/
structs), decoupled from MonoBehaviours and rendering, to preserve a later DOTS/ECS
migration path and an efficient time-rewind snapshot system.

Versioning: this title carries the doc version. Each script header carries a
`// Version:` line bumped when that script changes.

Changes in v0.16: one Dwelling per 2 agents (housing). StructureNode tracks residents
(max 2, TryAddResident); Agent gains Home; StructureManager places ceil(agents/2)
Dwellings per civ in a spaced grid near the anchor; AgentBehavior lazily claims the
nearest own-civ Dwelling with a free slot and builds / shelters / sleeps at THAT home
(no longer the first built structure). Single-cell placeholders for now -- true 2x2
footprints, Storage, and town-territory placement are a later slice.

Changes in v0.15: day/night schedule + Stamina rest (Phase B, B3). SimulationClock.IsNight
splits each day in half. NeedsSystem now drains Stamina while active and refills it while
Agent.IsResting (home-only). AgentBehavior gains a Resting intent (priority 3, below
Drink/Eat, above Work): rest at home at night or when exhausted, with wake hysteresis; Work
no longer runs at night. Health/death still deferred (would starve everyone out before
farms). Next: Phase C -- jobs, town-planner, farming + wild-food respawn.

Changes in v0.14: needs + decision controller (Phase B, B2). Agent gains the four needs
(Hunger/Thirst/Stamina/Health). NeedsSystem drains Hunger+Thirst each tick. AgentBehavior
is rewritten from the rigid FSM into a continuous decision controller that picks an intent
by priority each step -- Drinking > Eating > Working -- so survival preempts work and the
freeze is gone; it drinks at the lake (GridData.TryFindNearestDrinkPoint) and idles at home
once the civ's structure is built (no more wood over-gathering). Stamina/Health dynamics,
day-night rest (B3), and the job system + farming (Phase C) are still to come.

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
authored against 450 ticks/day (hunger/thirst/stamina drains in AgentManager). Night is
the second half of the day: SimulationClock.IsNight is true for TickOfDay >= 225.

## Advance order (per fixed step)

1. Agent.Advance(dt)           — continuous movement
2. AgentBehavior.Update(dt)    — state-machine transitions, timers (harvest, build)
3. Tick cadence                — OnTick fires; NeedsSystem drains Hunger+Thirst+Stamina
                                 (recovers Stamina if resting), day rollover

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
      NeedsSystem.cs
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
  TryGetWalkableNeighbor (stand-on cell for mining), TryFindNearestDrinkPoint (Thirst),
  CellToLocal, ContinuousToLocal, LocalToCell.
- GridManager.cs — MonoBehaviour adapter. Builds/owns GridData (forwards waterLevel);
  Scene gizmo overlay (green walkable / red unwalkable / blue water); RebuildWaterPlane
  context menu spawns a water plane at waterLevel (edit-mode + Play; Game-view visible).
- Pathfinder.cs — Plain C# static. A* over GridData (4-connected, walkable only; water
  and hills are unwalkable so paths route around them). [Diagonal: see deferred-tech.]
- Agent.cs — Plain C#. NPC: Civ, position, CellX/CellZ, Speed, four needs (Hunger/Thirst
  rise toward bad; Stamina/Health are reserves), IsResting flag, Home (assigned Dwelling),
  inventory.
- AgentManager.cs — MonoBehaviour bridge. Registers each civ's spawn anchor in the sim,
  spawns agentsPerCiv agents for Civ1 and Civ2 at opposite edges (ring-out from each
  anchor), tints capsules by civ, attaches an AgentBehavior + an AgentView to each, syncs
  all capsules, creates the single NeedsSystem, and shows Day/Night in its read-out.
- AgentView.cs — MonoBehaviour, debug instrument. Attached per capsule; mirrors its
  agent's live state (civ, Action/intent, four needs, inventory, cell) into the Inspector.
- Civ.cs — Plain C#. CivId enum (None/Civ1/Civ2) + CivState (per-civ record with spawn
  anchor). Civ identity carried by Agent and StructureNode.
- SimulationClock.cs — Plain C#. Tick counter; derives Day, TickOfDay, IsNight (2nd half).
- Simulation.cs — Plain C#. Sim root; owns Civs (registry + RegisterCiv), Agents,
  ResourceNodes, StructureNodes, AgentBehaviors; AddStructureNode takes a CivId;
  Advance(dt) runs the advance order above.
- SimulationRunner.cs — MonoBehaviour bridge. Fixed-step loop; secondsPerDay +
  ticksPerDay (Play start); timeScale (live); exposes Sim.
- NeedsSystem.cs — Plain C#. Subscribes OnTick; per agent raises Hunger+Thirst and, by
  IsResting, drains or recovers Stamina (home-only). Health dynamics deferred.
- ResourceNode.cs — Plain C#. Passive sim data: Type (Wood/Food/Stone), cell, Amount,
  Depleted, Harvest(); single-agent reservation (ClaimedBy, TryClaim, Release).
- ResourceManager.cs — MonoBehaviour bridge. Seed-based scatter: Wood/Food on walkable
  cells (marked occupied), Stone on reachable unwalkable hill cells; placeholder
  primitives (brown cube=wood, green sphere=food, grey cube=stone).
- StructureNode.cs — Plain C#. Build-site data: Civ (owner), WoodRequired, WoodDeposited,
  BuildProgress (0..1 continuous timer), IsBuilt; occupancy (ResidentCount, MaxResidents=2,
  HasFreeSlot, TryAddResident). DepositWood + AdvanceBuild.
- StructureManager.cs — MonoBehaviour bridge. Once civs + agents exist, places
  ceil(civAgents/2) Dwellings per civ in a spaced grid near the anchor (free walkable
  cells, marked occupied), and spawns/animates a placeholder cube per site (flat→tall).
- AgentBehavior.cs — Plain C#. Per-agent decision controller. Each step ChooseIntent()
  picks by priority: Drinking (Thirst>=thr) > Eating (Hunger>=thr) > Resting (night, or
  Stamina exhausted, with wake hysteresis) > Working. Survival preempts all; Work never
  runs at night. Drinking → nearest drink point → drink. Eating → claim nearest food node →
  eat. Resting → go to OWN home → set IsResting (Stamina recovers). Working → gather wood →
  build OWN Dwelling → idle at home. Each agent claims the nearest own-civ Dwelling with a
  free slot (2 max) as Home. Exposes Action + Intent for
  AgentView. Mining/farming still to come (Phase C).

## Interaction map

- Sim spine: SimulationRunner → Simulation.Advance(FixedStep). OnTick / OnDayChanged
  are the event bus; stat drains and future systems subscribe.
- Agent lifecycle: AgentManager registers civ anchors, then spawns 12 agents per civ
  (Civ1/Civ2), each with its own AgentBehavior. Behavior owns all NPC logic; AgentManager
  mirrors positions. Resource nodes are shared/contested; structures are civ-scoped.
- Resource loop: AgentBehavior.TrySeekResource picks the nearest UNCLAIMED node →
  ResourceNode.TryClaim → Pathfinder → agent.SetPath → arrive → harvestTimer →
  ResourceNode.Harvest → ReleaseNode → inventory → TryMoveToSite. One agent per node.
- Work loop (lowest priority): each agent claims its own Dwelling (2 per house) → gathers
  wood → builds THAT Dwelling → once IsBuilt idles/shelters/sleeps there. No wood is
  gathered once its house is built. StructureManager reads BuildProgress for visuals.
- Needs/decision: NeedsSystem raises Hunger+Thirst (and drains/recovers Stamina by
  IsResting) each tick. AgentBehavior re-chooses an intent every step: thirsty → drink;
  else hungry → eat; else night/exhausted → rest at home (Stamina recovers); else Work.
  Survival preempts all; Work is daytime-only.
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
