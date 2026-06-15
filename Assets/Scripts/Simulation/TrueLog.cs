// TrueLog.cs
// Version: 0.1 (Prototype v5 "History Log v1, data only", GDD Ch.4)
// Purpose: The world's append-only narrative record. Systems Record() events (births,
//          deaths, battles, buildings, territory flips) as agents act; anyone can query
//          them by type, civ, significance, agent, or time, walk the causal graph, or
//          render a chronicle. This is the substrate the later quest, knowledge, and
//          rewind layers (Ch.5/6/36) read — built now, data only.
//          NOTE: this is the narrative log, NOT the save/rewind snapshot system (Ch.4.5).
// Location: Assets/Scripts/Simulation/TrueLog.cs
// Dependencies: System; System.Collections.Generic; System.Text; SimulationClock;
//               WorldEvent; CivId. No UnityEngine (snapshot-friendly).
// Events: OnRecord(WorldEvent) — raised after each append, for live history UIs.

using System;
using System.Collections.Generic;
using System.Text;

public class TrueLog
{
    private readonly SimulationClock clock;
    private readonly List<WorldEvent> events = new List<WorldEvent>();
    private int nextId = 1;

    public IReadOnlyList<WorldEvent> Events => events;
    public int Count => events.Count;

    public event Action<WorldEvent> OnRecord;

    public TrueLog(SimulationClock clock) { this.clock = clock; }

    // Append an event. Tick/Day are stamped from the clock so callers never pass time.
    public WorldEvent Record(EventType type, EventSignificance significance, string summary,
                             CivId civA = CivId.None, CivId civB = CivId.None,
                             int actorId = 0, int subjectId = 0,
                             int cellX = -1, int cellZ = -1,
                             int amount = 0, DeathCause cause = DeathCause.Unknown,
                             int causeEventId = 0)
    {
        var e = new WorldEvent
        {
            Id           = nextId++,
            Tick         = clock != null ? clock.TotalTicks : 0,
            Day          = clock != null ? clock.Day : 0,
            Type         = type,
            Significance = significance,
            CivA         = civA,
            CivB         = civB,
            ActorId      = actorId,
            SubjectId    = subjectId,
            CellX        = cellX,
            CellZ        = cellZ,
            Amount       = amount,
            Cause        = cause,
            CauseEventId = causeEventId,
            OriginTier   = 0,
            Summary      = summary
        };
        events.Add(e);
        OnRecord?.Invoke(e);
        return e;
    }

    // ── Queries (Ch.4.2 "querying knowledge") ────────────────────────────────────
    public IEnumerable<WorldEvent> OfType(EventType t)
    { for (int i = 0; i < events.Count; i++) if (events[i].Type == t) yield return events[i]; }

    public IEnumerable<WorldEvent> ByCiv(CivId c)
    { for (int i = 0; i < events.Count; i++) if (events[i].CivA == c || events[i].CivB == c) yield return events[i]; }

    public IEnumerable<WorldEvent> AtLeast(EventSignificance s)
    { for (int i = 0; i < events.Count; i++) if (events[i].Significance >= s) yield return events[i]; }

    // Every event an agent was party to — the seed of a personal history / Lineage page.
    public IEnumerable<WorldEvent> ForAgent(int agentId)
    { for (int i = 0; i < events.Count; i++) if (events[i].ActorId == agentId || events[i].SubjectId == agentId) yield return events[i]; }

    public IEnumerable<WorldEvent> Since(long tick)
    { for (int i = 0; i < events.Count; i++) if (events[i].Tick >= tick) yield return events[i]; }

    public int CountOf(EventType t)
    { int n = 0; for (int i = 0; i < events.Count; i++) if (events[i].Type == t) n++; return n; }

    public WorldEvent FindById(int id)
    { for (int i = 0; i < events.Count; i++) if (events[i].Id == id) return events[i]; return null; }

    // The causal chain behind an event (Ch.4.4): follow CauseEventId back to the root,
    // so "why did this happen?" is a graph traversal. Guarded against bad links/cycles.
    public List<WorldEvent> CausalChain(WorldEvent e)
    {
        var chain = new List<WorldEvent>();
        WorldEvent cur = e;
        int guard = 0;
        while (cur != null && guard++ < 4096)
        {
            chain.Add(cur);
            if (cur.CauseEventId == 0) break;
            cur = FindById(cur.CauseEventId);
        }
        return chain;
    }

    // A readable history of the world's significant events. Town-tier and up by default —
    // personal events (most births/deaths) are queryable but compress out of the headline
    // narrative, per the significance rule (Ch.4.3).
    public string Chronicle(EventSignificance minSignificance = EventSignificance.Town)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < events.Count; i++)
        {
            WorldEvent e = events[i];
            if (e.Significance >= minSignificance)
                sb.Append("Day ").Append(e.Day).Append(": ").Append(e.Summary).Append('\n');
        }
        return sb.ToString();
    }
}
