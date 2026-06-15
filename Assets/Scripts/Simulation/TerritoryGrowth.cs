// TerritoryGrowth.cs
// Version: 0.3 (Prototype v5 / v2 Phase D — contiguous expansion; single-pass scan for
//                performance; self-seeds from the civ anchor, GDD Ch.8)
// Purpose: Living borders. On an interval each civ claims a ring of neutral, walkable cells
//          adjacent to land it already owns (contiguous growth), logged as a Town-tier
//          event so the map's ownership history is queryable. A civ with no start territory
//          self-seeds a small block around its anchor first. One grid pass per interval
//          builds every civ's frontier and owned-count together (previously several full
//          scans per civ — a per-day spike on large maps).
//          Deferred (Ch.8/Phase D): Explorer-directed claiming and capture of an enemy's
//          owned cells short of conquest (CombatSystem flips territory at conquest only).
// Location: Assets/Scripts/Simulation/TerritoryGrowth.cs
// Dependencies: System.Collections.Generic; UnityEngine (Vector2Int); Simulation; GridData;
//               CivId. Uses GridData's int-based Owner/Cells API.
// Events: subscribes Simulation.OnDayChanged. Records TerritoryClaimed in the True Log.

using System.Collections.Generic;
using UnityEngine;

public class TerritoryGrowth
{
    private readonly Simulation sim;
    private readonly GridData   grid;

    public int ExpandIntervalDays = 2;   // claim a ring this often
    public int ClaimPerInterval   = 48;  // max cells claimed per civ per interval
    public int SeedRadius         = 3;   // half-extent of the self-seed block (cells)

    private readonly Dictionary<CivId, int> owned = new Dictionary<CivId, int>();
    private readonly Dictionary<CivId, List<Vector2Int>> frontier =
        new Dictionary<CivId, List<Vector2Int>>();

    public TerritoryGrowth(Simulation sim, GridData grid)
    {
        this.sim = sim;
        this.grid = grid;
        sim.OnDayChanged += OnDay;
    }

    public void Dispose() { sim.OnDayChanged -= OnDay; }

    private void OnDay(int day)
    {
        if (sim.Ended || grid == null) return;
        if (ExpandIntervalDays < 1 || day % ExpandIntervalDays != 0) return;

        // One pass over the grid builds owned counts + per-civ frontier candidates.
        owned.Clear();
        foreach (var kv in frontier) kv.Value.Clear();

        for (int z = 0; z < grid.Depth; z++)
        for (int x = 0; x < grid.Width; x++)
        {
            CivId o = grid.Cells[x, z].Owner;
            if (o != CivId.None)
            {
                int c; owned.TryGetValue(o, out c); owned[o] = c + 1;
                continue;
            }
            if (!grid.Cells[x, z].Walkable) continue;

            CivId touch = TouchedCiv(x, z);          // a neutral cell on someone's border
            if (touch != CivId.None) FrontierOf(touch).Add(new Vector2Int(x, z));
        }

        foreach (CivState civ in sim.Civs)
        {
            if (civ.Id == CivId.None || sim.CountLivingAgents(civ.Id) == 0) continue;

            int have; owned.TryGetValue(civ.Id, out have);
            if (have == 0) { SeedBlock(civ); continue; }   // no start territory -> seed, grow next time

            List<Vector2Int> front = FrontierOf(civ.Id);
            int claim = front.Count < ClaimPerInterval ? front.Count : ClaimPerInterval;
            if (claim <= 0) continue;
            for (int i = 0; i < claim; i++)
                grid.SetOwner(front[i].x, front[i].y, civ.Id);

            sim.Log.Record(EventType.TerritoryClaimed, EventSignificance.Town,
                civ.Id + " expands its borders, claiming " + claim + " cells (now " + (have + claim) + ").",
                civA: civ.Id, amount: claim);
        }
    }

    private void SeedBlock(CivState civ)
    {
        int claimed = 0;
        for (int z = civ.AnchorZ - SeedRadius; z <= civ.AnchorZ + SeedRadius; z++)
        for (int x = civ.AnchorX - SeedRadius; x <= civ.AnchorX + SeedRadius; x++)
            if (grid.InBounds(x, z) && grid.Cells[x, z].Owner == CivId.None)
            {
                grid.SetOwner(x, z, civ.Id);
                claimed++;
            }
        if (claimed > 0)
            sim.Log.Record(EventType.TerritoryClaimed, EventSignificance.Town,
                civ.Id + " stakes out its homeland (" + claimed + " cells).",
                civA: civ.Id, amount: claimed);
    }

    // The civ owning a 4-neighbour of (x,z), or None. First found wins (contested borders
    // resolve to a single owner — fine for the prototype).
    private CivId TouchedCiv(int x, int z)
    {
        CivId c;
        if ((c = OwnerAt(x + 1, z)) != CivId.None) return c;
        if ((c = OwnerAt(x - 1, z)) != CivId.None) return c;
        if ((c = OwnerAt(x, z + 1)) != CivId.None) return c;
        if ((c = OwnerAt(x, z - 1)) != CivId.None) return c;
        return CivId.None;
    }

    private CivId OwnerAt(int x, int z)
        => grid.InBounds(x, z) ? grid.Cells[x, z].Owner : CivId.None;

    private List<Vector2Int> FrontierOf(CivId civ)
    {
        List<Vector2Int> list;
        if (!frontier.TryGetValue(civ, out list)) { list = new List<Vector2Int>(); frontier[civ] = list; }
        return list;
    }
}
