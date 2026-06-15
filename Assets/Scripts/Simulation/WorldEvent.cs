// WorldEvent.cs
// Version: 0.1 (schema v1 — Prototype v5 "History Log v1, data only", GDD Ch.4)
// Purpose: Plain-C# event record for the True Log. Schema v1 captures the births,
//          deaths, battles, buildings, and territory flips the prototype produces:
//          who (actors + civ roles), what (outcome payload), where (cell), when (tick/day),
//          how significant, and the precipitating event (causal-graph seed, Ch.4.4).
//          Deferred to Pre-Alpha (Ch.4 staging): the witness set and full cause-link
//          authoring. OriginTier is carried but never surfaced (Article IX).
// Location: Assets/Scripts/Simulation/WorldEvent.cs
// Dependencies: CivId. No UnityEngine (snapshot-friendly).
// Events: none.

// The versioned event-type registry (Ch.4.2). Each type has a default significance and
// a cause-link rule (authored where the event is recorded).
public enum EventType
{
    Founding,            // a civilization is established (world-tier)
    Birth,               // an agent is born (the Lineage front end, Ch.11)
    Death,               // an agent dies — see DeathCause (Ch.9.2 / 11.3 / 25)
    StructureCompleted,  // a building finishes (Ch.15)
    TerritoryClaimed,    // neutral land claimed by contiguous expansion (Ch.8, Phase D)
    TerritoryCaptured,   // owned land flips to a conquering civ (Ch.28)
    BattleFought,        // a melee engagement is joined (Ch.25)
    AgentCaptured,       // a downed enemy is taken captive instead of killed (Ch.25.2)
    CivConquered         // a civilization reaches its conquest end-state (Ch.28)
}

// Significance drives retention and compression (Ch.4.3) and what the chronicle surfaces.
public enum EventSignificance { Personal, Town, Civilization, World }

// Cause of a Death event (the "what" payload for deaths).
public enum DeathCause { Unknown, Starvation, Dehydration, OldAge, Combat }

public class WorldEvent
{
    public int               Id;            // sequential, assigned by the TrueLog
    public long              Tick;          // stamped from the SimulationClock
    public int               Day;           // 1-based day, stamped from the clock
    public EventType         Type;
    public EventSignificance Significance;

    // ── Actors (Ch.4.2 "who"): agent + civ ids with implied roles ────────────────
    public CivId CivA;        // primary civ: instigator / owner / parent civ / victor
    public CivId CivB;        // secondary civ: victim / rival / conquered
    public int   ActorId;     // primary agent (parent, builder, killer, victor). 0 = none
    public int   SubjectId;   // secondary agent (child, victim, captive).        0 = none

    // ── Location (cells; -1 = not located) ───────────────────────────────────────
    public int CellX = -1;
    public int CellZ = -1;

    // ── Outcome payload (Ch.4.2 "what") ──────────────────────────────────────────
    public int        Amount;        // generic scalar: casualties, cells flipped, units…
    public DeathCause Cause;         // for Death events

    // ── Causal graph (Ch.4.4): the precipitating event, 0 = root ─────────────────
    public int CauseEventId;

    // ── Internal bookkeeping (Ch.4.2): origin tier, never surfaced (Article IX) ──
    public int OriginTier;           // prototype is all Tier 0

    public string Summary = "";      // human-readable line for the chronicle / UI

    public override string ToString() => "Day " + Day + ": " + Summary;
}
