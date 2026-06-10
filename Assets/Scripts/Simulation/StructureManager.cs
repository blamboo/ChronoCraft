// StructureManager.cs
// Version: 0.8 (places one Dwelling per 2 agents per civ, spaced near the anchor; animates each)
// Purpose: Unity bridge for prototype structures. Once the Simulation has registered its
//          civs and spawned its agents, places ceil(agents/2) Dwellings per civ on free
//          walkable cells in a small spaced grid near that civ's anchor, marks each cell
//          occupied, and spawns/animates a placeholder cube per site (height/colour follow
//          build progress). Holds no sim logic. Single-cell placeholders for now -- true
//          2x2 footprints, Storage, and town-territory placement are a later slice.
// Location: Assets/Scripts/Simulation/StructureManager.cs
// Dependencies: UnityEngine; System.Collections.Generic; SimulationRunner, GridManager,
//               StructureNode, CivState.
// Events: none.

using System.Collections.Generic;
using UnityEngine;

public class StructureManager : MonoBehaviour
{
    [Header("Scene references")]
    [Tooltip("Drag the Simulation GameObject here.")]
    [SerializeField] private SimulationRunner runner;
    [Tooltip("Drag the ProceduralTerrain GameObject here.")]
    [SerializeField] private GridManager gridManager;

    [Header("Dwellings (one per 2 agents, per civ)")]
    [Tooltip("Wood units delivered before a Dwelling can start building.")]
    [Range(1, 10)]
    [SerializeField] private int woodRequired = 3;
    [Tooltip("Game-seconds to finish a Dwelling once its wood is in.")]
    [Range(1f, 120f)]
    [SerializeField] private float buildDurationSeconds = 20f;
    [Tooltip("Cells between Dwelling sites (also keeps them off each other / resources).")]
    [Range(2, 8)]
    [SerializeField] private int spacing = 3;

    private class Site { public StructureNode Node; public Transform View; public Renderer Renderer; }
    private readonly List<Site> sites = new List<Site>();
    private bool initialized;

    void Update()
    {
        if (!initialized) { TryInitialize(); if (!initialized) return; }

        for (int i = 0; i < sites.Count; i++)
        {
            Site s = sites[i];
            if (s.View == null) continue;

            float t = s.Node.BuildProgress;
            s.View.localScale = new Vector3(0.9f, Mathf.Lerp(0.1f, 1.5f, t), 0.9f);

            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", Color.Lerp(
                new Color(0.55f, 0.55f, 0.55f),   // grey foundation
                new Color(0.65f, 0.45f, 0.25f),   // warm tan (built)
                t));
            s.Renderer.SetPropertyBlock(mpb);
        }
    }

    void TryInitialize()
    {
        if (runner == null || gridManager == null) return;
        GridData   grid = gridManager.Grid;
        Simulation sim  = runner.Sim;
        if (grid == null || sim == null) return;

        // Wait until AgentManager has registered civs and spawned agents.
        if (sim.Civs.Count == 0 || sim.Agents.Count == 0) return;

        foreach (CivState civ in sim.Civs)
        {
            int civAgents = 0;
            for (int i = 0; i < sim.Agents.Count; i++)
                if (sim.Agents[i].Civ == civ.Id) civAgents++;

            int houses = Mathf.Max(1, Mathf.CeilToInt(civAgents / 2f));
            int cols   = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(houses)));

            for (int k = 0; k < houses; k++)
            {
                int col = k % cols;
                int row = k / cols;
                int tx  = civ.AnchorX + (col - (cols - 1) / 2) * spacing;
                int tz  = civ.AnchorZ + row * spacing;

                Vector2Int cell = NearestFreeWalkable(grid, new Vector2Int(tx, tz));

                StructureNode node = sim.AddStructureNode(civ.Id, cell.x, cell.y,
                                                          woodRequired, buildDurationSeconds);
                grid.SetOccupied(cell.x, cell.y, true);

                var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obj.name = $"Dwelling ({civ.Id}) #{k}";
                obj.transform.SetParent(gridManager.transform, worldPositionStays: false);
                Destroy(obj.GetComponent<Collider>());

                Vector3 local = grid.CellToLocal(cell.x, cell.y);
                obj.transform.localPosition = new Vector3(local.x, local.y + 0.05f, local.z);
                obj.transform.localScale    = new Vector3(0.9f, 0.1f, 0.9f);

                sites.Add(new Site { Node = node, View = obj.transform, Renderer = obj.GetComponent<Renderer>() });
            }
        }

        initialized = true;
    }

    // Nearest walkable AND unoccupied cell to c (so sites never share a cell or land on a
    // resource cell). Falls back to nearest walkable, then c.
    Vector2Int NearestFreeWalkable(GridData grid, Vector2Int c)
    {
        c.x = Mathf.Clamp(c.x, 0, grid.Width - 1);
        c.y = Mathf.Clamp(c.y, 0, grid.Depth - 1);
        if (grid.Cells[c.x, c.y].Walkable && !grid.Cells[c.x, c.y].Occupied) return c;

        int maxR = Mathf.Max(grid.Width, grid.Depth);
        for (int r = 1; r <= maxR; r++)
        for (int dz = -r; dz <= r; dz++)
        for (int dx = -r; dx <= r; dx++)
        {
            if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r) continue;
            int x = c.x + dx, z = c.y + dz;
            if (grid.InBounds(x, z) && grid.Cells[x, z].Walkable && !grid.Cells[x, z].Occupied)
                return new Vector2Int(x, z);
        }
        return c;
    }
}
