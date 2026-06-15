// ResourceNode.cs
// Version: 0.7 (Prototype v5: Regrow for wild-food/resource respawn)
// Purpose: Plain-C# resource node for the TimeCraft prototype. Stores a node's type,
//          cell position, remaining stock, and a single-agent reservation so that at
//          most one agent gathers from a node at a time (prevents clumping). Passive sim
//          data -- no ticking, no rendering. No MonoBehaviour, no UnityEngine.
// Location: Assets/Scripts/Simulation/ResourceNode.cs
// Dependencies: System; references Agent (plain C#) for the reservation holder.
// Events: none.

public enum ResourceType { Wood, Food, Stone }

public class ResourceNode
{
    public ResourceType Type  { get; }
    public int CellX          { get; }
    public int CellZ          { get; }
    public int Amount         { get; private set; }
    public bool Depleted      => Amount <= 0;

    // Reservation: one agent at a time. Null = free. AgentBehavior claims a node when it
    // targets it and releases it when it finishes or abandons it.
    public Agent ClaimedBy    { get; private set; }
    public bool IsClaimed     => ClaimedBy != null;

    public ResourceNode(ResourceType type, int cellX, int cellZ, int amount)
    {
        Type   = type;
        CellX  = cellX;
        CellZ  = cellZ;
        Amount = amount;
    }

    // Claims the node for agent 'a'. Succeeds if free or already held by 'a'.
    public bool TryClaim(Agent a)
    {
        if (ClaimedBy == null || ClaimedBy == a) { ClaimedBy = a; return true; }
        return false;
    }

    // Releases the claim only if 'a' currently holds it (safe to call unconditionally).
    public void Release(Agent a)
    {
        if (ClaimedBy == a) ClaimedBy = null;
    }

    // Harvest up to 'request' units. Returns the actual amount taken.
    // Harmless to call on a depleted node (returns 0).
    public int Harvest(int request)
    {
        int taken = System.Math.Min(request, Amount);
        Amount -= taken;
        return taken;
    }

    // Regrow stock toward a cap (wild-food / resource respawn, Phase C2). Re-flips a
    // depleted node back to harvestable so the world does not starve once nodes run dry.
    public void Regrow(int amount, int cap)
    {
        if (amount <= 0 || Amount >= cap) return;
        Amount = System.Math.Min(cap, Amount + amount);
    }
}
