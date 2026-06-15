// Agent.cs
// Version: 0.15 (Prototype v5: Explorer job role; Nudge for the separation pass; stable Id,
//                Lineage identity, combat skills, conscription + CombatCooldown, StepToward)
// Purpose: Plain-C# simulation agent (NPC). Continuous position, speed-based movement,
//          civ identity, job role, resource inventory, survival needs, and life-cycle
//          identity (Ch.9/11): a stable Id (True Log actor key), Sex and LifeStage for
//          reproduction/aging, parent ids for lineage, and a small inheritable skill set.
// Location: Assets/Scripts/World/Agent.cs
// Dependencies: System.Collections.Generic; UnityEngine (Vector2Int/Mathf only); CivId; JobRole.
// Events: none.

using System.Collections.Generic;
using UnityEngine;

public enum JobRole   { Logger, Farmer, Miner, Builder, Explorer }
public enum Sex       { Male, Female }
public enum LifeStage { Child, Adult, Elder }

public class Agent
{
    // Stable identity — the True Log actor key and the lineage node id (Ch.4/11).
    public int Id;

    public CivId   Civ = CivId.None;
    public JobRole Job = JobRole.Logger;

    public float Speed = 3f;

    // ── Life cycle (Ch.9/11) ────────────────────────────────────────────────────
    public Sex       Sex     = Sex.Male;
    public LifeStage Stage   = LifeStage.Adult;   // founders spawn as adults
    public float     AgeDays = 0f;                // advanced once per game-day
    public int       BirthTick;                   // when this agent was born
    public int       MotherId;                    // 0 = founder (no recorded parent)
    public int       FatherId;
    public bool      IsAlive = true;              // cleared on death (guards double-kill)
    public bool      IsAdult => Stage == LifeStage.Adult;

    // Inheritable skills (Ch.9.3 subset): averaged from parents at birth (Ch.11.1),
    // grown through use (combat raises SkillCombat). The full 16-skill set is Pre-Alpha.
    public float SkillFarming = 20f;
    public float SkillCombat  = 20f;

    // Combat reserves used by the Conflict system (Ch.25). Conscripted agents are driven
    // by the army/raid layer and skip the civilian decision brain while mustered.
    public bool Conscripted = false;
    // Set when struck (game-seconds); while > 0 a civilian stands its ground and fights
    // back instead of wandering off to eat/drink/work. Decays in AgentBehavior.
    public float CombatCooldown = 0f;

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

    // Straight-line move toward a continuous target (cell units), Speed*dt per call.
    // Pure C# (no Pathfinder/Vector types) so simulation systems — the raid/army layer —
    // can drive an agent directly. Returns true once the target is reached. Clears any
    // existing path so behavior and the driving system do not fight over position.
    public bool StepToward(float targetX, float targetZ, float dt)
    {
        path = null; nextIndex = 0;
        float dx = targetX - PosX, dz = targetZ - PosZ;
        float dist = (float)System.Math.Sqrt(dx * dx + dz * dz);
        if (dist <= 1e-4f) { PosX = targetX; PosZ = targetZ; return true; }
        float budget = Speed * dt;
        if (budget >= dist) { PosX = targetX; PosZ = targetZ; return true; }
        float inv = budget / dist;
        PosX += dx * inv; PosZ += dz * inv;
        return false;
    }

    // Small positional shove used by the separation pass to keep agents off the same cell.
    public void Nudge(float dx, float dz) { PosX += dx; PosZ += dz; }
}
