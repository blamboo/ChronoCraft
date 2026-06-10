// Agent.cs
// Version: 0.11 (added Home: the agent's assigned Dwelling -- built/sheltered/slept in)
// Purpose: Plain-C# simulation agent (NPC). Continuous position, speed-based movement,
//          civ identity, resource inventory, and survival needs. Needs are driven by
//          NeedsSystem (per tick); actions are chosen by AgentBehavior (the decision
//          controller). Pure sim state; no MonoBehaviour, no rendering.
//          Need conventions (0..100): Hunger and Thirst RISE toward bad (0 = satisfied,
//          100 = starving/parched). Stamina and Health are reserves that FALL toward bad
//          (100 = full, 0 = spent/dead). Stamina/Health are wired in a later slice.
// Location: Assets/Scripts/World/Agent.cs
// Dependencies: System.Collections.Generic; UnityEngine (Vector2Int/Mathf only); CivId.
// Events: none. Advanced by Simulation.Advance(dt); needs drained by NeedsSystem.

using System.Collections.Generic;
using UnityEngine;

public class Agent
{
    public CivId Civ = CivId.None;

    // Movement speed in cells per game-second. NOT tied to tick rate.
    public float Speed = 3f;

    public float PosX { get; private set; }
    public float PosZ { get; private set; }
    public int CellX => Mathf.RoundToInt(PosX);
    public int CellZ => Mathf.RoundToInt(PosZ);

    // ── Needs (0..100) ──────────────────────────────────────────────────────
    public float Hunger  = 0f;    // rises toward bad; eating resets to 0
    public float Thirst  = 0f;    // rises toward bad; drinking resets to 0
    public float Stamina = 100f;  // falls toward bad (wired later: B3 rest)
    public float Health  = 100f;  // falls toward bad (wired later)

    // Set true by AgentBehavior while resting at home; NeedsSystem recovers Stamina then.
    public bool IsResting = false;

    // The Dwelling this agent has claimed as home (max 2 agents per Dwelling). Assigned
    // lazily by AgentBehavior; the agent builds, shelters, and sleeps here.
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
