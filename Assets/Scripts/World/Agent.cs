// Agent.cs
// Version: 0.7 (added StoneCarried; AddResource/inventory now cover Wood/Food/Stone)
// Purpose: Plain-C# simulation agent (NPC). Continuous position, speed-based movement,
//          resource inventory, and a hunger float drained each tick by AgentBehavior.
//          Pure sim state; no MonoBehaviour, no rendering.
// Location: Assets/Scripts/World/Agent.cs
// Dependencies: System.Collections.Generic; UnityEngine (Vector2Int/Mathf only).
// Events: none. Advanced by Simulation.Advance(dt) each fixed sim step.

using System.Collections.Generic;
using UnityEngine;

public class Agent
{
    // Movement speed in cells per game-second. NOT tied to tick rate.
    public float Speed = 3f;

    // Continuous position in cell space.
    public float PosX { get; private set; }
    public float PosZ { get; private set; }

    // Nearest logical cell for systems that need a discrete address.
    public int CellX => Mathf.RoundToInt(PosX);
    public int CellZ => Mathf.RoundToInt(PosZ);

    // ── Hunger ────────────────────────────────────────────────────────────────
    // Drained each logical tick by AgentBehavior. Range 0..100.
    public float Hunger = 0f;

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

    public Agent(int startX, int startZ)
    {
        PosX = startX;
        PosZ = startZ;
    }

    public bool HasPath => path != null && nextIndex < path.Count;

    public void SetPath(List<Vector2Int> newPath)
    {
        path = newPath;
        if (path != null && path.Count > 0)
        {
            PosX      = path[0].x;
            PosZ      = path[0].y;
            nextIndex = 1;
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
            float dx   = target.x - PosX;
            float dz   = target.y - PosZ;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            if (dist <= budget)
            {
                PosX = target.x; PosZ = target.y;
                budget -= dist; nextIndex++;
            }
            else
            {
                float inv = budget / dist;
                PosX += dx * inv; PosZ += dz * inv;
                budget = 0f;
            }
        }
    }
}
