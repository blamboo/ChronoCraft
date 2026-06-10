// NeedsSystem.cs
// Version: 0.2 (added Stamina: drains while active, recovers while resting at home)
// Purpose: Plain-C# system that advances agent survival needs once per logical tick.
//          Owns stat dynamics; the decision controller owns actions and sets Agent.IsResting.
//          B2 wired Hunger+Thirst (rise toward bad). B3 wires Stamina (a reserve: falls
//          while active, refills while the agent rests at home). Health is still deferred.
// Location: Assets/Scripts/Simulation/NeedsSystem.cs
// Dependencies: UnityEngine (Mathf); Simulation, Agent. Subscribes Simulation.OnTick.
// Events consumed: Simulation.OnTick.

using UnityEngine;

public class NeedsSystem
{
    private readonly Simulation sim;
    public float HungerDrainPerTick;
    public float ThirstDrainPerTick;
    public float StaminaDrainPerTick;
    public float StaminaRecoverPerTick;

    public NeedsSystem(Simulation sim, float hungerDrain, float thirstDrain,
                       float staminaDrain, float staminaRecover)
    {
        this.sim = sim;
        HungerDrainPerTick    = hungerDrain;
        ThirstDrainPerTick    = thirstDrain;
        StaminaDrainPerTick   = staminaDrain;
        StaminaRecoverPerTick = staminaRecover;
        sim.OnTick += OnTick;
    }

    public void Dispose() { sim.OnTick -= OnTick; }

    void OnTick()
    {
        var agents = sim.Agents;
        for (int i = 0; i < agents.Count; i++)
        {
            Agent a = agents[i];
            a.Hunger = Mathf.Min(100f, a.Hunger + HungerDrainPerTick);
            a.Thirst = Mathf.Min(100f, a.Thirst + ThirstDrainPerTick);
            if (a.IsResting)
                a.Stamina = Mathf.Min(100f, a.Stamina + StaminaRecoverPerTick);
            else
                a.Stamina = Mathf.Max(0f, a.Stamina - StaminaDrainPerTick);
            // Health dynamics deferred (would start starvation deaths before farms exist).
        }
    }
}
