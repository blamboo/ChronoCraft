// StructureManager.cs
// Version: 0.10 (added Storage inventory Inspector read-out; version bumped from 0.9)
// Purpose: Unity bridge for prototype structures. Places ceil(agents/2) Dwellings + 1
//          Storage per civ near each civ's spawn anchor, marks cells occupied, animates
//          placeholder cubes, mirrors StorageNode inventory into the Inspector each frame.
// Location: Assets/Scripts/Simulation/StructureManager.cs
// Dependencies: UnityEngine; System.Collections.Generic; SimulationRunner, GridManager,
//               StructureNode, StorageNode, CivState.
// Events: none.

using System.Collections.Generic;
using UnityEngine;

public class StructureManager : MonoBehaviour
{
    [Header("Scene references")]
    [SerializeField] private SimulationRunner runner;
    [SerializeField] private GridManager gridManager;

    [Header("Dwellings")]
    [Range(1, 10)]   [SerializeField] private int   dwellingWoodRequired  = 3;
    [Range(1f, 120f)][SerializeField] private float dwellingBuildDuration = 20f;
    [Range(2, 8)]    [SerializeField] private int   spacing               = 3;

    [Header("Storage")]
    [Range(1, 20)]   [SerializeField] private int   storageWoodRequired   = 6;
    [Range(1f, 120f)][SerializeField] private float storageBuildDuration  = 30f;

    [Header("Storage inventory (read-only, live)")]
    [SerializeField] private bool civ1StorageBuilt;
    [SerializeField] private int  civ1Wood;
    [SerializeField] private int  civ1Food;
    [SerializeField] private int  civ1Stone;
    [SerializeField] private bool civ2StorageBuilt;
    [SerializeField] private int  civ2Wood;
    [SerializeField] private int  civ2Food;
    [SerializeField] private int  civ2Stone;

    private class Site
    {
        public StructureNode Node;
        public StorageNode   Storage;
        public Transform     View;
        public Renderer      Rend;
    }

    private readonly List<Site>        sites       = new List<Site>();
    private readonly List<StorageNode> storageNodes = new List<StorageNode>();
    private bool initialized;

    void Update()
    {
        if (!initialized) { TryInitialize(); if (!initialized) return; }
        AnimateSites();
        MirrorStorageReadout();
    }

    void AnimateSites()
    {
        for (int i = 0; i < sites.Count; i++)
        {
            Site s = sites[i];
            if (s.View == null) continue;

            float t = s.Node.BuildProgress;
            s.View.localScale = new Vector3(0.9f, Mathf.Lerp(0.1f, 1.5f, t), 0.9f);

            var mpb = new MaterialPropertyBlock();
            bool isStorage = s.Node.Type == StructureType.Storage;
            mpb.SetColor("_BaseColor", Color.Lerp(
                new Color(0.55f, 0.55f, 0.55f),
                isStorage ? new Color(0.3f, 0.6f, 0.3f)
                          : new Color(0.65f, 0.45f, 0.25f),
                t));
            s.Rend.SetPropertyBlock(mpb);

            if (s.Storage != null && !s.Storage.IsBuilt && s.Node.IsBuilt)
                s.Storage.IsBuilt = true;
        }
    }

    void MirrorStorageReadout()
    {
        foreach (var st in storageNodes)
        {
            if (st.Civ == CivId.Civ1)
            {
                civ1StorageBuilt = st.IsBuilt;
                civ1Wood  = st.Wood;
                civ1Food  = st.Food;
                civ1Stone = st.Stone;
            }
            else if (st.Civ == CivId.Civ2)
            {
                civ2StorageBuilt = st.IsBuilt;
                civ2Wood  = st.Wood;
                civ2Food  = st.Food;
                civ2Stone = st.Stone;
            }
        }
    }

    void TryInitialize()
    {
        if (runner == null || gridManager == null) return;
        GridData   grid = gridManager.Grid;
        Simulation sim  = runner.Sim;
        if (grid == null || sim == null) return;
        if (sim.Civs.Count == 0 || sim.Agents.Count == 0) return;

        foreach (CivState civ in sim.Civs)
        {
            int civAgents = 0;
            for (int i = 0; i < sim.Agents.Count; i++)
                if (sim.Agents[i].Civ == civ.Id) civAgents++;

            int sx = civ.AnchorX;
            int sz = civ.AnchorZ - spacing;
            Vector2Int storageCell = NearestFreeWalkable(grid, new Vector2Int(sx, sz));

            StructureNode storageNode = sim.AddStructureNode(
                StructureType.Storage, civ.Id,
                storageCell.x, storageCell.y,
                storageWoodRequired, storageBuildDuration);
            grid.SetOccupied(storageCell.x, storageCell.y, true);

            StorageNode storageData = sim.AddStorageNode(civ.Id, storageCell.x, storageCell.y);
            storageNodes.Add(storageData);

            SpawnCube(grid, $"Storage ({civ.Id})", storageCell, out Transform stView, out Renderer stRend);
            sites.Add(new Site { Node = storageNode, Storage = storageData,
                                 View = stView, Rend = stRend });

            int houses = Mathf.Max(1, Mathf.CeilToInt(civAgents / 2f));
            int cols   = Mathf.Max(1, Mathf.CeilToInt(Mathf.Sqrt(houses)));

            for (int k = 0; k < houses; k++)
            {
                int col = k % cols;
                int row = k / cols;
                int tx  = civ.AnchorX + (col - (cols - 1) / 2) * spacing;
                int tz  = civ.AnchorZ + row * spacing;

                Vector2Int cell = NearestFreeWalkable(grid, new Vector2Int(tx, tz));

                StructureNode node = sim.AddStructureNode(
                    StructureType.Dwelling, civ.Id,
                    cell.x, cell.y,
                    dwellingWoodRequired, dwellingBuildDuration);
                grid.SetOccupied(cell.x, cell.y, true);

                SpawnCube(grid, $"Dwelling ({civ.Id}) #{k}", cell, out Transform dView, out Renderer dRend);
                sites.Add(new Site { Node = node, Storage = null, View = dView, Rend = dRend });
            }
        }

        initialized = true;
    }

    void SpawnCube(GridData grid, string objName, Vector2Int cell,
                   out Transform tf, out Renderer rend)
    {
        var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.name = objName;
        obj.transform.SetParent(gridManager.transform, worldPositionStays: false);
        Destroy(obj.GetComponent<Collider>());
        Vector3 local = grid.CellToLocal(cell.x, cell.y);
        obj.transform.localPosition = new Vector3(local.x, local.y + 0.05f, local.z);
        obj.transform.localScale    = new Vector3(0.9f, 0.1f, 0.9f);
        tf   = obj.transform;
        rend = obj.GetComponent<Renderer>();
    }

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
