// NeedsSystem.cs
// Version: 0.4 (Prototype v5: Health dynamics + death; needs creep up slowly while asleep
//                (RestingDrainScale) so agents don't wake at night to eat/drink)
// Purpose: Plain-C# system that advances agent survival needs once per logical tick.
//          Owns stat dynamics; the decision controller owns actions and sets Agent.IsResting.
//          Hunger+Thirst rise toward bad; Stamina falls while active and refills while
//          resting at home. Health is now wired (Ch.9.2): a maxed Hunger or Thirst injures
//          the agent, a well-fed agent slowly recovers, and Health 0 is death — logged with
//          its cause (Ch.4) via the sim's single death path. Mental Health/Social are
//          Pre-Alpha (Ch.9 staging) and not simulated here.
// Location: Assets/Scripts/Simulation/NeedsSystem.cs
// Dependencies: UnityEngine (Mathf); Simulation, Agent, DeathCause. Subscribes Simulation.OnTick.
// Events consumed: Simulation.OnTick. Emits Death via Simulation.KillAgent.

using System.Collections.Generic;
using UnityEngine;

public class NeedsSystem
{
    private readonly Simulation sim;
    public float HungerDrainPerTick;
    public float ThirstDrainPerTick;
    public float StaminaDrainPerTick;
    public float StaminaRecoverPerTick;

    // Health dynamics (per tick). Damage applies only at true starvation/dehydration so
    // a fed, watered population stays healthy; recovery applies when both needs are low.
    public float StarvationDamagePerTick  = 0.5f;
    public float DehydrationDamagePerTick = 0.7f;
    public float HealthRecoverPerTick     = 0.2f;
    public float NeedCriticalLevel        = 99f;  // Hunger/Thirst at/above this injures
    public float NeedSafeLevel            = 50f;  // both below this allows recovery
    // Hunger/Thirst rise this much slower while the agent is asleep (resting), so it does
    // not wake in the night to eat or drink. 0 = needs pause entirely during sleep.
    public float RestingDrainScale        = 0.15f;

    private readonly List<Agent> dyingScratch = new List<Agent>();
    private readonly List<DeathCause> causeScratch = new List<DeathCause>();

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
        dyingScratch.Clear();
        causeScratch.Clear();

        var agents = sim.Agents;
        for (int i = 0; i < agents.Count; i++)
        {
            Agent a = agents[i];
            if (!a.IsAlive) continue;

            // Asleep: needs creep up far slower so agents don't wake to eat/drink at night.
            float needScale = a.IsResting ? RestingDrainScale : 1f;
            a.Hunger = Mathf.Min(100f, a.Hunger + HungerDrainPerTick * needScale);
            a.Thirst = Mathf.Min(100f, a.Thirst + ThirstDrainPerTick * needScale);
            if (a.IsResting)
                a.Stamina = Mathf.Min(100f, a.Stamina + StaminaRecoverPerTick);
            else
                a.Stamina = Mathf.Max(0f, a.Stamina - StaminaDrainPerTick);

            // Health: injury from need-failure, slow recovery when sustained.
            if (a.Thirst >= NeedCriticalLevel)
                a.Health = Mathf.Max(0f, a.Health - DehydrationDamagePerTick);
            else if (a.Hunger >= NeedCriticalLevel)
                a.Health = Mathf.Max(0f, a.Health - StarvationDamagePerTick);
            else if (a.Hunger < NeedSafeLevel && a.Thirst < NeedSafeLevel && a.Health < 100f)
                a.Health = Mathf.Min(100f, a.Health + HealthRecoverPerTick);

            if (a.Health <= 0f)
            {
                // Thirst kills faster, so attribute to whichever need is maxed.
                dyingScratch.Add(a);
                causeScratch.Add(a.Thirst >= NeedCriticalLevel ? DeathCause.Dehydration
                                                               : DeathCause.Starvation);
            }
        }

        // Apply deaths after the loop — KillAgent mutates sim.Agents.
        for (int i = 0; i < dyingScratch.Count; i++)
            sim.KillAgent(dyingScratch[i], causeScratch[i]);
    }
}
