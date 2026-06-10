// TerritorySystem.cs
// Version: 0.1
// Purpose: Assigns starting territory to each civ (a rectangular block of cells
//          centred on its spawn anchor). Town territory (inner buildable zone) is
//          a smaller concentric block inside civ territory. Both are stamped into
//          GridData.Owner at startup; Explorer-driven expansion is Phase D.
// Location: Assets/Scripts/Simulation/TerritorySystem.cs
// Dependencies: GridData (Owner field), Civ (CivId, CivState), Simulation (Civs registry).
// Events: none emitted or consumed.

using UnityEngine;
using System.Collections.Generic;

public class TerritorySystem
{
    // ── Inspector-exposed sizes (set via TerritoryManager MonoBehaviour) ──────────
    // Civ territory half-extent: cells in each direction from the anchor.
    // GDD §8: starting civ territory covers the town zone + a growth margin.
    public int CivTerritoryRadius   = 24;   // total civ block = (2r+1)^2
    // Town territory half-extent: must be large enough for 1 Storage (4x4) +
    // 6 Dwellings (2x2 each, 2-cell spacing). Default 12 fits that comfortably.
    public int TownTerritoryRadius  = 12;

    // Per-civ town-territory cell list (for Builder job placement in Phase C).
    private readonly Dictionary<CivId, HashSet<Vector2Int>> _townCells = new();

    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Stamps starting territory into GridData for every registered civ.
    /// Call once after GridData is built and civs are registered.
    /// </summary>
    public void InitialiseStartTerritories(GridData grid, IEnumerable<CivState> civs)
    {
        _townCells.Clear();

        foreach (var civ in civs)
        {
            if (civ.Id == CivId.None) continue;

            // AnchorX/AnchorZ are already grid-cell coordinates (set by AgentManager).
            var anchor = new Vector2Int(civ.AnchorX, civ.AnchorZ);

            _townCells[civ.Id] = new HashSet<Vector2Int>();

            StampTerritory(grid, anchor, CivTerritoryRadius,  civ.Id, isTown: false);
            StampTerritory(grid, anchor, TownTerritoryRadius,  civ.Id, isTown: true);
        }
    }

    /// <summary>
    /// Returns all town-territory cells for a civ (used by StructureManager / Builder).
    /// </summary>
    public IReadOnlyCollection<Vector2Int> GetTownCells(CivId civ)
    {
        return _townCells.TryGetValue(civ, out var set) ? set : System.Array.Empty<Vector2Int>();
    }

    /// <summary>
    /// True if the cell is within this civ's town territory (Builder-placeable zone).
    /// </summary>
    public bool IsInTownTerritory(CivId civ, int x, int z)
    {
        return _townCells.TryGetValue(civ, out var set) && set.Contains(new Vector2Int(x, z));
    }

    // ── Private helpers ───────────────────────────────────────────────────────────

    private void StampTerritory(GridData grid, Vector2Int anchor, int radius,
                                CivId owner, bool isTown)
    {
        int xMin = Mathf.Max(0,          anchor.x - radius);
        int xMax = Mathf.Min(grid.Width  - 1, anchor.x + radius);
        int zMin = Mathf.Max(0,          anchor.y - radius);
        int zMax = Mathf.Min(grid.Depth  - 1, anchor.y + radius);

        for (int z = zMin; z <= zMax; z++)
        for (int x = xMin; x <= xMax; x++)
        {
            grid.SetOwner(x, z, owner);
            if (isTown) _townCells[owner].Add(new Vector2Int(x, z));
        }
    }
}
