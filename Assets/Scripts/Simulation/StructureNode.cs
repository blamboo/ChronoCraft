// StructureNode.cs
// Version: 0.7 (added Civ: each structure belongs to one civilization)
// Purpose: Plain-C# build-site data for the TimeCraft prototype. Tracks owning civ,
//          deposited wood, build progress (continuous game-time timer), and completion.
//          Passive until an agent calls DepositWood then AdvanceBuild each sim step.
//          No MonoBehaviour, no rendering.
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

    public StructureNode(CivId civ, int cellX, int cellZ, int woodRequired, float buildDurationSeconds)
    {
        Civ                  = civ;
        CellX                = cellX;
        CellZ                = cellZ;
        WoodRequired         = woodRequired;
        BuildDurationSeconds = buildDurationSeconds;
    }

    // Call once when the agent arrives at the site to transfer carried wood.
    public void DepositWood(int amount)
    {
        WoodDeposited = System.Math.Min(WoodDeposited + amount, WoodRequired);
    }

    // Called every sim fixed step while an agent is actively building.
    public void AdvanceBuild(float dt)
    {
        if (!HasEnoughWood || IsBuilt) return;
        BuildProgress = System.Math.Min(1f, BuildProgress + dt / BuildDurationSeconds);
    }
}
