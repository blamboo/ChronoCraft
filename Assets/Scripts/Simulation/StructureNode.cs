// StructureNode.cs
// Version: 0.9 (added StructureType enum + Type field so agents distinguish Storage from Dwelling)
// Purpose: Plain-C# build-site data for the TimeCraft prototype. Tracks owning civ,
//          deposited wood, build progress (continuous game-time timer), completion, and
//          occupancy (how many agents call this their home). Passive until an agent calls
//          DepositWood then AdvanceBuild each sim step. No MonoBehaviour, no rendering.
// Location: Assets/Scripts/Simulation/StructureNode.cs
// Dependencies: System; CivId.
// Events: none.

public enum StructureType { Dwelling, Storage, Farm }

public class StructureNode
{
    public StructureType Type             { get; }
    public CivId         Civ              { get; }
    public int           CellX            { get; }
    public int           CellZ            { get; }
    public int           WoodRequired     { get; }
    public int           WoodDeposited    { get; private set; }
    public float         BuildDurationSeconds { get; }
    public float         BuildProgress    { get; private set; } // 0..1
    public bool          HasEnoughWood    => WoodDeposited >= WoodRequired;
    public bool          IsBuilt          => BuildProgress >= 1f;

    // Occupancy: a Dwelling houses up to 2 agents (GDD S10/S11).
    public int  MaxResidents  => 2;
    public int  ResidentCount { get; private set; }
    public bool HasFreeSlot   => ResidentCount < MaxResidents;

    public StructureNode(StructureType type, CivId civ, int cellX, int cellZ,
                         int woodRequired, float buildDurationSeconds)
    {
        Type                 = type;
        Civ                  = civ;
        CellX                = cellX;
        CellZ                = cellZ;
        WoodRequired         = woodRequired;
        BuildDurationSeconds = buildDurationSeconds;
    }

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
