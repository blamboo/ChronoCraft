# TimeCraft — Architecture Document (v0.18.4)

What the code is. Read this and the Prototype GDD at the start of each task.
Update whenever a script is added, changed, or removed.

Changes in v0.18.4: presentation + housing pass (Unity bridges; no sim-logic change).
ResourceManager v0.6: keeps a (node -> placeholder) map and hides a node's view while it is
depleted, showing it again when ResourceRespawn regrows it (resources visibly disappear and
return). StructureManager v0.11: a structure's placeholder stays hidden until construction
actually starts (BuildProgress > 0) — no more visible empty foundations; and it now tops up
each civ's dwellings at runtime (every ~2s) so a growing population has homes — Builders
construct the new sites and AgentBehavior claims the free slots for newborns. Harness 20/20
unchanged (these are MonoBehaviour bridges, not part of the headless build).

Changes in v0.18.3: GDD-aligned territory & war model + anti-overlap (playtest pass 3).
Explorer job role (Agent v0.15, AgentBehavior v0.18): explorers roam and claim a radius-5
area of UNCLAIMED, walkable land for their civ as they go (Ch.8/Phase D) — this replaces the
passive ring expansion (TerritoryGrowth now OFF by default in AgentManager). CombatSystem
v0.4: war is no longer a timer — it ignites when an agent stands on another civ's territory
(an incursion), and the owner musters in response (Phase D->E); once at war both sides keep
mustering until conquest. SeparationSystem.cs (NEW): a light O(n^2) boids-style pass that
nudges apart agents closer than ~0.9 cells, so soldiers/foragers/drinkers don't stack on one
cell. AgentManager v0.17: spawns explorers (default 2/civ), creates SeparationSystem, leaves
auto territory-growth off. Still pending (next pass): resource nodes hide on depletion and
reappear on respawn; build foundations hidden until construction starts; homes built for
newborns (StructureManager/ResourceManager bridges). Harness: 20/20.

Changes in v0.18.2: second in-Unity playtest pass (perf + combat readability).
Pathfinder v0.3: binary-heap open set (was an O(open) linear scan) — large-map A* is now
cheap, fixing lag from army marching and civilian pathing. CombatSystem v0.3: melee now
resolves on a steady game-time cadence (CombatTickSeconds) driven from OnStep instead of the
sparse day-tick (the runner uses ticksPerDay=24, so OnTick was ~50s apart — fights were
glacial and units wandered off mid-battle); struck civilians get a CombatCooldown and stand
their ground; attackers spread to different sides of a target (no stacking); A* re-paths are
throttled to destination changes. Agent v0.14: CombatCooldown. AgentBehavior v0.17: stand-
and-fight while CombatCooldown>0. NeedsSystem v0.4: Hunger/Thirst creep up far slower while
asleep (RestingDrainScale) so agents don't wake at night to drink. TerritoryGrowth v0.3:
one grid pass per interval (was up to 4 full scans/civ — a day-boundary spike on big maps);
default interval 2 days. AgentManager v0.16: disables Debug.Log stack-trace capture for the
chronicle (the main editor lag spike at high time scale), exposes the territory interval.
Harness: 19/19 (adds binary-heap pathfinding + barrier-routing checks).

Changes in v0.18.1: in-Unity playtest fixes. ResourceRespawn.cs (NEW) regrows food/wood
so the world stops starving once nodes deplete (Phase C2 stand-in). CombatSystem v0.2 now
marches war parties through the A* Pathfinder (no more walking through water/hills) and
stops one walkable cell short of the target instead of overlapping it. TerritoryGrowth v0.2
self-seeds a block around each civ anchor if no start territory was stamped, so borders
always expand and conquest has land to flip. AgentManager v0.15 creates ResourceRespawn,
adds a History-Log->Console toggle (logHistoryToConsole), and exposes lineage/conflict/
respawn tunables in the Inspector. ResourceNode v0.7 gains Regrow. Harness: 16/16 checks.

Engine: Unity 6.3 LTS (6000.3.17f1). Render pipeline: URP. Language: C#.

Guiding architecture principle: simulation state and logic live in plain C# (POCOs/
structs), decoupled from MonoBehaviours and rendering, to preserve a later DOTS/ECS
migration path and an efficient time-rewind snapshot system.

Versioning: this title carries the doc version. Each script header carries a
`// Version:` line bumped when that script changes.

Changes in v0.18: "The Living Chronicle" slice — Prototype v5's History Log (data only)
plus the v2 Phase D/E/F + v3 health/death event-producers that fill it. The world now
lives, reproduces, fights, and reaches a conquest end-state, writing its own history.
NEW pure-C# systems: WorldEvent.cs (event schema v1: type/significance/actors/cell/cause)
and TrueLog.cs (append-only, queryable record + causal graph + Chronicle) — GDD Ch.4;
LineageSystem.cs (aging, life stages, pairing→gestation→birth with skill-averaging, death
by old age) — Ch.11/Phase F; CombatSystem.cs (war-party muster, melee by skill/stamina,
rout, conquest end-state with territory flip) — Ch.25/28/Phase E; TerritoryGrowth.cs
(contiguous neutral expansion) — Ch.8/Phase D. CHANGED: Simulation (owns TrueLog; OnStep/
OnAgentBorn/OnAgentDied/OnEnded hooks; agent Ids; central KillAgent; DeclareConquest);
Agent (Id, Sex/LifeStage/AgeDays/parents, SkillFarming/SkillCombat, Conscripted, StepToward);
NeedsSystem (Health dynamics + starvation/dehydration death); StructureNode (RemoveResident,
CompletionLogged latch); AgentBehavior (OwnerAgent, conscription stand-down, logs
StructureCompleted); AgentManager (founder life-cycle identity; creates the three systems;
spawns/despawns views on birth/death; logs Foundings). Verified headlessly under Mono via
Tools/SimHarness (see "Headless verification" below): 13/13 checks, deterministic.
Deferred to Pre-Alpha+ (per chapter staging): the full event schema (witnesses, full
cause-link authoring, compression), dispositions/traits/estate inheritance, capture/Jail +
captive fates, surrender negotiation, gate/wall siege, Pathfinder-routed marching, and the
v4-dependent half of v5 (player possession, dialogue, quests, reputation-toward-player).

Changes in v0.17: Phase A4 + C1 scripts added to file tree; bug fix in AgentBehavior.
StorageNode.cs (new): plain C#, civ-scoped stockpile (Food/Wood/Stone); Deposit/Withdraw.
TerritorySystem.cs (new): plain C#, stamps initial civ + town territory blocks into
GridData.Owner; GetTownCells / IsInTownTerritory. TerritoryManager.cs (new): MonoBehaviour
bridge; Inspector overlays (Off/Territory/Walkable) rendered in Game view as Scene gizmos.
AgentBehavior.cs → v0.15: noFoodCooldown (real-time float, reset in Abandon) replaced with
noFoodTicks (tick-based int, NOT reset in Abandon) — food block survives drink interrupts;
drink-point scan cached per agent (rescan only after 3-cell movement) — eliminates O(W×D)
lag spike on food depletion. AgentManager.cs: subscribes sim.OnTick to decrement noFoodTicks
on all behaviors. AgentView.cs: adds homeCell + homeBuilt Inspector read-outs.

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

1. Agent.Advance(dt)           — continuous movement (path-following)
2. AgentBehavior.Update(dt)    — state-machine transitions, timers (harvest, build);
                                 conscripted agents stand down (driven by CombatSystem)
3. OnStep(dt) fires            — continuous-cadence systems: CombatSystem marches war parties
4. Tick cadence                — OnTick fires; NeedsSystem drains Hunger+Thirst+Stamina and
                                 applies Health/death; CombatSystem resolves melee + checks
                                 conquest; then OnDayChanged on rollover (LineageSystem ages/
                                 reproduces; CombatSystem musters; TerritoryGrowth expands)

Life-cycle spine: births route LineageSystem → Simulation.EmitAgentBorn → OnAgentBorn (the
bridge attaches a brain + view). Deaths route every cause → Simulation.KillAgent (logs the
Death event, frees the home, removes the agent + behavior, fires OnAgentDied). Conquest
routes CombatSystem → Simulation.DeclareConquest → OnEnded.

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
      StorageNode.cs
      TerritorySystem.cs
      TerritoryManager.cs
      AgentBehavior.cs
      WorldEvent.cs        (v0.18 — True Log event schema)
      TrueLog.cs           (v0.18 — the History Log)
      LineageSystem.cs     (v0.18 — birth/aging/death)
      CombatSystem.cs      (v0.18 — war + conquest)
      TerritoryGrowth.cs   (v0.18 — border expansion)
      ResourceRespawn.cs   (v0.18.1 — wild-food/resource respawn)
      SeparationSystem.cs  (v0.18.3 — anti-overlap nudge)
Tools/
  SimHarness/              (NOT a Unity asset — headless verification, runs under Mono)
    UnityShim.cs           (minimal Vector2Int/Vector3/Mathf so the sim compiles w/o the editor)
    Program.cs             (builds a two-civ world, runs the systems, asserts on the True Log)
    build.sh               (mcs compile + run)
```

Note: Agent.cs and AgentManager.cs live in World/ per project convention. Tools/ lives at
the repo root, OUTSIDE Assets/, so the Unity compiler never sees the shim.

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
- StorageNode.cs — Plain C#. Civ-scoped stockpile of Food/Wood/Stone; Deposit(type,
  amount) and WithdrawWood(amount); IsBuilt flipped by StructureManager when the Storage
  structure completes.
- TerritorySystem.cs — Plain C#. Stamps initial civ territory and town territory blocks
  into GridData.Owner at sim start; GetTownCells(CivId) and IsInTownTerritory(CivId,x,z)
  for placement queries. Explorer-driven claiming and contiguous capture deferred (Phase D).
- TerritoryManager.cs — MonoBehaviour bridge. Inspector: CivTerritoryRadius, TownTerritoryRadius,
  OverlayMode enum (Off/Territory/Walkable). Calls TerritorySystem.InitialiseStartTerritories;
  Scene gizmo draws colored quads per cell; overlay visible in Game view.
- StructureNode.cs — Plain C#. Build-site data: Type (Dwelling/Storage/Farm), Civ (owner),
  WoodRequired, WoodDeposited, BuildProgress (0..1 continuous timer), IsBuilt; occupancy
  (ResidentCount, MaxResidents=2, HasFreeSlot, TryAddResident). DepositWood + AdvanceBuild.
- StructureManager.cs — MonoBehaviour bridge. Places 1 Storage + ceil(agents/2) Dwellings
  per civ near their anchor (free walkable cells, marked occupied); animates placeholder
  cubes; flips StorageNode.IsBuilt when the Storage structure completes. Inspector read-out:
  per-civ StorageBuilt/Wood/Food/Stone (live).
- AgentBehavior.cs — Plain C#. Per-agent decision controller. Each step ChooseIntent()
  picks by priority: Drinking (Thirst>=thr) > Eating (Hunger>=thr, food not blocked) >
  Resting (night or Stamina exhausted, with wake hysteresis) > Working. noFoodTicks
  (tick-based, NOT reset in Abandon) suppresses hunger intent when food is depleted so rest
  wins at night; drink-point cached per agent (rescan on 3-cell movement). OnTick()
  decrements noFoodTicks (called via AgentManager→sim.OnTick subscription). Resting → go
  to OWN home → set IsResting (Stamina recovers). Working → gather/haul/build by Job role.
  Each agent claims nearest own-civ Dwelling with free slot as Home. Exposes Action +
  Intent for AgentView. Job roles: Logger/Farmer/Miner/Builder (Farmer/Miner/Builder
  behavior stubs; full farming Phase C2).
- AgentManager.cs — MonoBehaviour bridge. Registers civ anchors, spawns agentsPerCiv
  agents per civ, tints by civ, attaches AgentBehavior + AgentView, assigns Job roles,
  creates NeedsSystem, subscribes sim.OnTick to decrement noFoodTicks on all behaviors.
- AgentView.cs — MonoBehaviour, debug instrument. Mirrors live agent state into Inspector:
  civ, Action/intent, four needs, inventory, cell, homeCell, homeBuilt.

## v0.18 — the Living Chronicle (new + changed)

New (all plain C#, no UnityEngine, snapshot-friendly):
- WorldEvent.cs — True Log event record, schema v1 (Ch.4.2): EventType (Founding/Birth/
  Death/StructureCompleted/TerritoryClaimed/TerritoryCaptured/BattleFought/AgentCaptured/
  CivConquered), EventSignificance (Personal/Town/Civilization/World), DeathCause, civ+agent
  actor ids with roles, cell, Amount payload, CauseEventId (causal-graph seed), OriginTier
  (never surfaced), Summary.
- TrueLog.cs — append-only world history (Ch.4). Record() stamps tick/day from the clock;
  queries OfType/ByCiv/AtLeast/ForAgent/Since/CountOf/FindById; CausalChain() walks
  CauseEventId to the root (Ch.4.4); Chronicle(minSignificance) renders the headline history
  (Town+ by default — personal events compress out, Ch.4.3). OnRecord for live UIs. NOT the
  save/rewind system (Ch.4.5). Owned by Simulation as `Log`.
- LineageSystem.cs — the life cycle (Ch.11, Phase F). On each day: ages everyone; Child→
  Adult→Elder transitions; pairs fertile adults → gestation → birth (skills averaged from
  parents + jitter, Ch.11.1) via Simulation, logging Birth; rolls death by old age past the
  expectancy threshold. Seeded RNG (deterministic). Pop cap per civ.
- CombatSystem.cs — the war (Ch.25/28, Phase E + F end-state). Musters a war party at the
  rival (OnDayChanged), marches it (OnStep, StepToward), resolves melee by skill+stamina with
  rout (OnTick); combat deaths route through Simulation.KillAgent citing the muster battle;
  on a civ reaching zero agents declares conquest, flips the loser's territory (Territory
  Captured), logs CivConquered. Seeded RNG. Uses GridData's int Owner API only.
- TerritoryGrowth.cs — contiguous border expansion (Ch.8, Phase D). On an interval each civ
  claims a ring of neutral, walkable, frontier cells and logs TerritoryClaimed (Town).
  v0.2: self-seeds a small block around the civ anchor if no start territory exists.
- ResourceRespawn.cs — wild-food/resource respawn (Phase C2 stand-in). On an interval
  regrows Food (and optionally Wood/Stone) nodes toward a cap so foragers never run out and
  the population is sustainable. Without it, finite nodes deplete and the world starves now
  that Health/death is wired. (Real farming output is a later slice.)
- CombatSystem v0.2 — marching uses the A* Pathfinder (routes around water/hills) and a
  raider stops at a walkable cell beside its target; combat damage stays tick-quantized.

Changed:
- Simulation.cs — now owns the TrueLog; adds OnStep (continuous), OnAgentBorn/OnAgentDied,
  OnEnded, and Ended/WinningCiv; assigns agent Ids in AddAgent; KillAgent (the one death
  path: log + free home + remove agent/behavior + fire); DeclareConquest; CountLivingAgents.
- Agent.cs — adds Id (log/lineage key), Sex, LifeStage, AgeDays, BirthTick, Mother/FatherId,
  IsAlive, SkillFarming/SkillCombat, Conscripted, and StepToward (pure-C# direct move for
  the raid layer).
- NeedsSystem.cs — adds Health dynamics: maxed Hunger/Thirst injure, sustained low needs
  recover, Health 0 → Simulation.KillAgent with the right DeathCause (Ch.9.2).
- StructureNode.cs — RemoveResident (death frees a home slot); CompletionLogged latch (one
  History Log entry per finished building even with several builders on it).
- AgentBehavior.cs — OwnerAgent accessor (for KillAgent); conscripted agents stand down so
  the war layer drives them; logs StructureCompleted when a build finishes (latched).
- AgentManager.cs — gives founders life-cycle identity (Sex/Stage/Age/SkillCombat); creates
  the Lineage/Combat/TerritoryGrowth systems (Inspector toggles + seeds); spawns a view+brain
  on OnAgentBorn and destroys it on OnAgentDied; logs civ Foundings; Inspector read-out of
  log size + war state.

## Headless verification

The simulation is plain C# decoupled from MonoBehaviours, so it runs without the editor.
`Tools/SimHarness/build.sh` compiles the sim + world logic files against a tiny UnityEngine
shim (Vector2Int/Vector3/Mathf) using Mono's `mcs` and runs a two-civ soak: it builds a flat
grid, wires Needs+Health/Lineage/Combat/TerritoryGrowth plus the civilian brain, and asserts
the True Log captured the full v5 event set (births, deaths incl. age + combat + starvation,
battles, buildings, territory flips, conquest) with intact schema and a working causal chain.
Current status: 13/13 checks, deterministic across runs (~50 days to conquest, ~114 events).
The shim lives outside Assets/ so Unity never compiles it.

## Interaction map

- Sim spine: SimulationRunner → Simulation.Advance(FixedStep). OnTick / OnDayChanged
  are the event bus; stat drains and future systems subscribe. AgentManager subscribes
  OnTick to decrement noFoodTicks on all behaviors.
- Agent lifecycle: AgentManager registers civ anchors, spawns 12 agents per civ
  (Civ1/Civ2), assigns Job roles, each with its own AgentBehavior. Behavior owns all NPC
  logic; AgentManager mirrors positions. Resource nodes are shared/contested; structures
  and storage are civ-scoped.
- Resource loop: AgentBehavior.TrySeekResource picks nearest UNCLAIMED node →
  ResourceNode.TryClaim → Pathfinder → agent.SetPath → arrive → harvestTimer →
  ResourceNode.Harvest → ReleaseNode → inventory → haul to StorageNode.
- Work loop (lowest priority): Loggers/Farmers/Miners gather and haul to Storage.
  Builders withdraw wood from Storage → walk to site → deposit → build. Each agent claims
  its own Dwelling (2 per house) for rest/reproduction. StructureManager reads
  BuildProgress for visuals and flips StorageNode.IsBuilt when Storage completes.
- Needs/decision: NeedsSystem raises Hunger+Thirst (and drains/recovers Stamina by
  IsResting) each tick. AgentBehavior re-chooses intent every step: thirsty → drink
  (cached drink point); food not blocked and hungry → eat; night/exhausted → rest at home
  (Stamina recovers); else Work. noFoodTicks (not reset in Abandon) keeps food suppressed
  across drink interrupts so rest wins at night when food is depleted.
- Territory: TerritoryManager calls TerritorySystem.InitialiseStartTerritories on start,
  stamping GridData.Owner for each civ's home + town block. Overlay toggle renders in
  Game view. Explorer claiming and contiguous capture deferred (Phase D).
- GridData.SetOccupied used by ResourceManager (resource cells) and StructureManager
  (build sites). Pathfinder checks Walkable only so agents can reach occupied cells.
- Water/hills: GridData.Build flags IsWater (unwalkable); hill cells unwalkable by slope.
  Pathfinder routes around both. TryGetWalkableNeighbor / IsWaterAdjacent give the
  adjacent stand-on cell for Thirst and future Miner job.

## Scene wiring

- "ProceduralTerrain": TerrainGenerator + MeshFilter + MeshRenderer + GridManager.
  Spawns a "WaterPlane" child (Rebuild Water Plane context menu / on Play).
- Main Camera: orthographic top-down (0,60,0), rotation (90,0,0).
- "Simulation": SimulationRunner.
- "Agents": AgentManager → runner=Simulation, gridManager=ProceduralTerrain.
- "Resources": ResourceManager → runner=Simulation, gridManager=ProceduralTerrain.
- "Structure": StructureManager → runner=Simulation, gridManager=ProceduralTerrain.
  Spawns Storage + Dwelling cubes per civ at runtime near each civ's spawn anchor.
- "Territory": TerritoryManager → runner=Simulation, gridManager=ProceduralTerrain.
  Reads GridData.Owner; renders colored cell overlay in Game view.
