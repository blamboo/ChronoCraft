// ResourceRespawn.cs
// Version: 0.1 (Prototype v5 / v2 Phase C2 — wild-food (and resource) respawn)
// Purpose: Keeps the world from starving once nodes run dry. On an interval it regrows
//          Food (and optionally Wood/Stone) nodes toward a cap, so foragers always have
//          something to eat and the population is sustainable long enough for the lineage,
//          territory, and conflict systems to play out. Without this, finite nodes deplete,
//          agents idle, and — now that Health/death is wired — the whole world starves.
//          This is the prototype stand-in for true farming output (Ch.13) + wild-food
//          regrowth; farms producing food on their own is a later slice.
// Location: Assets/Scripts/Simulation/ResourceRespawn.cs
// Dependencies: Simulation; ResourceNode; ResourceType. No UnityEngine.
// Events: subscribes Simulation.OnDayChanged.

public class ResourceRespawn
{
    private readonly Simulation sim;

    public int IntervalDays   = 1;    // regrow this often
    public int FoodRegen      = 8;    // units added per Food node per interval
    public int WoodRegen      = 4;    // units added per Wood node per interval (0 = off)
    public int StoneRegen     = 0;    // units added per Stone node per interval (0 = off)
    public int FoodCap        = 60;   // max stock a Food node regrows to
    public int WoodCap        = 60;
    public int StoneCap       = 60;

    public ResourceRespawn(Simulation sim)
    {
        this.sim = sim;
        sim.OnDayChanged += OnDay;
    }

    public void Dispose() { sim.OnDayChanged -= OnDay; }

    private void OnDay(int day)
    {
        if (IntervalDays < 1 || day % IntervalDays != 0) return;

        var nodes = sim.ResourceNodes;
        for (int i = 0; i < nodes.Count; i++)
        {
            ResourceNode n = nodes[i];
            switch (n.Type)
            {
                case ResourceType.Food:  n.Regrow(FoodRegen,  FoodCap);  break;
                case ResourceType.Wood:  n.Regrow(WoodRegen,  WoodCap);  break;
                case ResourceType.Stone: n.Regrow(StoneRegen, StoneCap); break;
            }
        }
    }
}
