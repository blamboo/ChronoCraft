// StructureNode.cs
// Version: 0.8 (added residents: a Dwelling houses up to 2 agents -- per GDD)
// Purpose: Plain-C# build-site data for the TimeCraft prototype. Tracks owning civ,
//          deposited wood, build progress (continuous game-time timer), completion, and
//          occupancy (how many agents call this their home). Passive until an agent calls
//          DepositWood then AdvanceBuild each sim step. No MonoBehaviour, no rendering.
// Location: Assets/Scripts/Simulation/StructureNode.cs
// Dependencies: System; CivId.
// Events: none.

public class StructureNode
{
    public CivId Civ                 { get; }
    public int   CellX               { get; }
    public int   CellZ               { get; }
    public int   WoodRequired        { get; }
    public int   WoodDeposited       { get; private set; }
    public float BuildDurationSeconds { get; }
    public float BuildProgress       { get; private set; } // 0..1
    public bool  HasEnoughWood       => WoodDeposited >= WoodRequired;
    public bool  IsBuilt             => BuildProgress >= 1f;

    // Occupancy: a Dwelling houses up to 2 agents (GDD S10/S11). Agents assign themselves
    // a home via TryAddResident; reproduction (later) triggers on a male+female pair here.
    public int  MaxResidents  => 2;
    public int  ResidentCount { get; private set; }
    public bool HasFreeSlot   => ResidentCount < MaxResidents;

    public StructureNode(CivId civ, int cellX, int cellZ, int woodRequired, float buildDurationSeconds)
    {
        Civ                  = civ;
        CellX                = cellX;
        CellZ                = cellZ;
        WoodRequired         = woodRequired;
        BuildDurationSeconds = buildDurationSeconds;
    }

    // Reserves one resident slot. Caller must only count an agent once (assign when the
    // agent has no home yet). Returns false when the Dwelling is full.
    public bool TryAddResident()
    {
        if (ResidentCount >= MaxResidents) return false;
        ResidentCount++;
        return true;
    }

    public void DepositWood(int amount)
    {
        WoodDeposited = System.Math.Min(WoodDeposited + amount, WoodRequired);
    }

    public void AdvanceBuild(float dt)
    {
        if (!HasEnoughWood || IsBuilt) return;
        BuildProgress = System.Math.Min(1f, BuildProgress + dt / BuildDurationSeconds);
    }
}
