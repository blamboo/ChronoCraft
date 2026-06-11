// Agent.cs
// Version: 0.12 (added JobRole field for Phase C job system)
// Purpose: Plain-C# simulation agent (NPC). Continuous position, speed-based movement,
//          civ identity, job role, resource inventory, and survival needs.
// Location: Assets/Scripts/World/Agent.cs
// Dependencies: System.Collections.Generic; UnityEngine (Vector2Int/Mathf only); CivId; JobRole.
// Events: none.

using System.Collections.Generic;
using UnityEngine;

public enum JobRole { Logger, Farmer, Miner, Builder }

public class Agent
{
    public CivId   Civ = CivId.None;
    public JobRole Job = JobRole.Logger;

    public float Speed = 3f;

    public float PosX { get; private set; }
    public float PosZ { get; private set; }
    public int CellX => Mathf.RoundToInt(PosX);
    public int CellZ => Mathf.RoundToInt(PosZ);

    // ── Needs (0..100) ──────────────────────────────────────────────────────
    public float Hunger  = 0f;
    public float Thirst  = 0f;
    public float Stamina = 100f;
    public float Health  = 100f;

    public bool IsResting = false;

    public StructureNode Home;

    // ── Inventory ─────────────────────────────────────────────────────────────
    public int CarryCapacity = 3;
    public int WoodCarried   { get; private set; }
    public int FoodCarried   { get; private set; }
    public int StoneCarried  { get; private set; }
    public bool InventoryFull => (WoodCarried + FoodCarried + StoneCarried) >= CarryCapacity;

    public void AddResource(ResourceType type, int amount)
    {
        switch (type)
        {
            case ResourceType.Wood:  WoodCarried  += amount; break;
            case ResourceType.Food:  FoodCarried  += amount; break;
            case ResourceType.Stone: StoneCarried += amount; break;
        }
    }

    public void ClearInventory() { WoodCarried = 0; FoodCarried = 0; StoneCarried = 0; }

    // ── Path / movement ───────────────────────────────────────────────────────
    private List<Vector2Int> path;
    private int nextIndex;

    public Agent(int startX, int startZ) { PosX = startX; PosZ = startZ; }

    public bool HasPath => path != null && nextIndex < path.Count;

    public void SetPath(List<Vector2Int> newPath)
    {
        path = newPath;
        if (path != null && path.Count > 0)
        {
            PosX = path[0].x; PosZ = path[0].y; nextIndex = 1;
        }
        else nextIndex = 0;
    }

    public void Advance(float dt)
    {
        if (!HasPath) return;
        float budget = Speed * dt;
        while (budget > 0f && HasPath)
        {
            Vector2Int target = path[nextIndex];
            float dx = target.x - PosX, dz = target.y - PosZ;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            if (dist <= budget) { PosX = target.x; PosZ = target.y; budget -= dist; nextIndex++; }
            else { float inv = budget / dist; PosX += dx * inv; PosZ += dz * inv; budget = 0f; }
        }
    }
}
