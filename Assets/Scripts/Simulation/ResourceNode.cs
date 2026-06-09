// ResourceNode.cs
// Version: 0.5 (added Stone to ResourceType for the Miner / ore slice)
// Purpose: Plain-C# resource node for the TimeCraft prototype. Stores a node's type,
//          cell position, and remaining stock. Passive sim data -- no ticking, no
//          rendering. Agents harvest from it; stock decreases until depleted.
//          No MonoBehaviour, no UnityEngine.
// Location: Assets/Scripts/Simulation/ResourceNode.cs
// Dependencies: System only.
// Events: none.

public enum ResourceType { Wood, Food, Stone }

public class ResourceNode
{
    public ResourceType Type  { get; }
    public int CellX          { get; }
    public int CellZ          { get; }
    public int Amount         { get; private set; }
    public bool Depleted      => Amount <= 0;

    public ResourceNode(ResourceType type, int cellX, int cellZ, int amount)
    {
        Type   = type;
        CellX  = cellX;
        CellZ  = cellZ;
        Amount = amount;
    }

    // Harvest up to 'request' units. Returns the actual amount taken.
    // Harmless to call on a depleted node (returns 0).
    public int Harvest(int request)
    {
        int taken = System.Math.Min(request, Amount);
        Amount -= taken;
        return taken;
    }
}
