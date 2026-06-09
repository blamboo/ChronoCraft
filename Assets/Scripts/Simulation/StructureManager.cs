// StructureManager.cs
// Version: 0.7 (one structure per civ, placed near each civ's spawn anchor; animates each)
// Purpose: Unity bridge for prototype structures. Once the Simulation has registered its
//          civs (AgentManager does this on spawn), places ONE StructureNode per civ on a
//          free walkable cell near that civ's anchor, marks the cell occupied, and spawns
//          a placeholder cube per site whose height/colour animate with build progress.
//          Holds no sim logic -- it mirrors StructureNode state into the scene.
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

    [Header("Build site (one per civ, placed near each civ's spawn anchor)")]
    [Tooltip("Wood units the agents must deliver before construction can begin.")]
    [Range(1, 10)]
    [SerializeField] private int woodRequired = 3;
    [Tooltip("Game-seconds to complete construction once wood is deposited.")]
    [Range(1f, 120f)]
    [SerializeField] private float buildDurationSeconds = 20f;

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
            float yScale = Mathf.Lerp(0.1f, 1.5f, t);                 // flat slab -> house height
            s.View.localScale = new Vector3(0.9f, yScale, 0.9f);

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

        // Wait until AgentManager has registered the civs (one structure per civ).
        if (sim.Civs.Count == 0) return;

        foreach (CivState civ in sim.Civs)
        {
            Vector2Int cell = NearestFreeWalkable(grid, new Vector2Int(civ.AnchorX, civ.AnchorZ));

            StructureNode node = sim.AddStructureNode(civ.Id, cell.x, cell.y,
                                                      woodRequired, buildDurationSeconds);
            grid.SetOccupied(cell.x, cell.y, true);

            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = $"Structure ({civ.Id})";
            obj.transform.SetParent(gridManager.transform, worldPositionStays: false);
            Destroy(obj.GetComponent<Collider>());

            Vector3 local = grid.CellToLocal(cell.x, cell.y);
            obj.transform.localPosition = new Vector3(local.x, local.y + 0.05f, local.z);
            obj.transform.localScale    = new Vector3(0.9f, 0.1f, 0.9f);

            sites.Add(new Site { Node = node, View = obj.transform, Renderer = obj.GetComponent<Renderer>() });
        }

        initialized = true;
    }

    // Nearest walkable AND unoccupied cell to c (so two civ structures never share a cell
    // and never land on a resource cell). Falls back to nearest walkable, then c.
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
