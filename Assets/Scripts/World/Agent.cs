// Agent.cs
// Version: 0.5 (added inventory: WoodCarried, FoodCarried, CarryCapacity)
// Purpose: Plain-C# simulation agent (NPC) for the TimeCraft prototype. Holds a
//          continuous position in cell space, walks its path at Speed (cells/sec),
//          and now carries a small resource inventory. Pure sim state.
// Location: Assets/Scripts/Simulation/Agent.cs
// Dependencies: System.Collections.Generic; UnityEngine for Vector2Int/Mathf only.
// Events: none. Advanced by Simulation.Advance(dt) each fixed sim step.

using System.Collections.Generic;
using UnityEngine;

public class Agent
{
    // Movement speed in cells per game-second. NOT tied to the tick/day rate.
    public float Speed = 3f;

    // Continuous position in cell space.
    public float PosX { get; private set; }
    public float PosZ { get; private set; }

    // Nearest logical cell for systems that need a discrete cell.
    public int CellX => Mathf.RoundToInt(PosX);
    public int CellZ => Mathf.RoundToInt(PosZ);

    // ── Inventory ──────────────────────────────────────────────────────────────
    // Max total units the agent can carry before needing to drop off.
    public int CarryCapacity = 3;
    public int WoodCarried { get; private set; }
    public int FoodCarried { get; private set; }
    public bool InventoryFull => (WoodCarried + FoodCarried) >= CarryCapacity;

    public void AddResource(ResourceType type, int amount)
    {
        if (type == ResourceType.Wood) WoodCarried += amount;
        else                           FoodCarried += amount;
    }

    // Clears carried resources. Temporarily used by GathererBehavior until a real
    // drop-off destination exists (construction slice).
    public void ClearInventory()
    {
        WoodCarried = 0;
        FoodCarried = 0;
    }

    // ── Path / movement ────────────────────────────────────────────────────────
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
        else
        {
            nextIndex = 0;
        }
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
                PosX   = target.x;
                PosZ   = target.y;
                budget -= dist;
                nextIndex++;
            }
            else
            {
                float inv = budget / dist;
                PosX  += dx * inv;
                PosZ  += dz * inv;
                budget = 0f;
            }
        }
    }
}
