// StorageNode.cs
// Version: 0.1
// Purpose: Plain-C# civ-scoped stockpile. Tracks Food/Wood/Stone amounts deposited by
//          gatherers and withdrawn by Builders. Passive sim data -- no ticking, no
//          rendering. Post-Storage phase only: once IsBuilt, gatherers haul here and
//          Builders draw from here.
// Location: Assets/Scripts/Simulation/StorageNode.cs
// Dependencies: CivId.
// Events: none.

public class StorageNode
{
    public CivId Civ   { get; }
    public int   CellX { get; }
    public int   CellZ { get; }

    // Is the physical Storage structure built yet? Set by StructureManager when the
    // linked StructureNode.IsBuilt flips true.
    public bool IsBuilt { get; set; }

    public int Wood  { get; private set; }
    public int Food  { get; private set; }
    public int Stone { get; private set; }

    public StorageNode(CivId civ, int cellX, int cellZ)
    {
        Civ   = civ;
        CellX = cellX;
        CellZ = cellZ;
    }

    // Deposit any resource type.
    public void Deposit(ResourceType type, int amount)
    {
        if (amount <= 0) return;
        switch (type)
        {
            case ResourceType.Wood:  Wood  += amount; break;
            case ResourceType.Food:  Food  += amount; break;
            case ResourceType.Stone: Stone += amount; break;
        }
    }

    // Withdraw up to 'request' units of wood (Builder draws material to build).
    // Returns actual amount taken.
    public int WithdrawWood(int request)
    {
        int taken = System.Math.Min(request, Wood);
        Wood -= taken;
        return taken;
    }
}
