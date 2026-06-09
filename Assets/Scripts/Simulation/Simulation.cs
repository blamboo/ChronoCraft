// Simulation.cs
// Version: 0.3 (continuous game-time; movement decoupled from derived day/stat ticks)
// Purpose: Plain-C# root of the TimeCraft prototype simulation. Runs on a single
//          continuous game-time source advanced in fixed steps. Two INDEPENDENT cadences
//          derive from it: continuous agent movement (every step) and a discrete
//          day/stat tick (every SecondsPerTick of game-time) on which hunger and other
//          over-time stats will drain. Contains no MonoBehaviour and no UnityEngine
//          references beyond value types -- the sim/render boundary the locked
//          architecture principle protects.
// Location: Assets/Scripts/Simulation/Simulation.cs
// Dependencies: System (Action); System.Collections.Generic; SimulationClock; Agent.
// Events emitted: OnTick (each derived logical tick); OnDayChanged(int newDay) (rollover).
// Events consumed: none. Advanced externally by SimulationRunner in fixed steps.

using System;
using System.Collections.Generic;

public class Simulation
{
    public SimulationClock Clock { get; }

    // Live agents, owned here as plain sim state. AgentManager spawns into this list.
    public List<Agent> Agents { get; } = new List<Agent>();

    // Game-seconds per logical tick = (seconds per day) / (ticks per day). The day/stat
    // cadence; movement does not use this.
    public double SecondsPerTick { get; }

    public event Action OnTick;
    public event Action<int> OnDayChanged;

    private double tickAccumulator;

    public Simulation(int ticksPerDay, double secondsPerDay)
    {
        Clock = new SimulationClock(ticksPerDay);
        SecondsPerTick = Math.Max(0.0001, secondsPerDay) / Math.Max(1, ticksPerDay);
    }

    public Agent AddAgent(int startX, int startZ)
    {
        var agent = new Agent(startX, startZ);
        Agents.Add(agent);
        return agent;
    }

    // Advances the sim by dt game-seconds. Movement is continuous (every step); logical
    // ticks fire whenever SecondsPerTick of game-time has accumulated (so one large dt
    // can emit several ticks).
    public void Advance(double dt)
    {
        // Continuous systems.
        for (int i = 0; i < Agents.Count; i++)
            Agents[i].Advance((float)dt);

        // Discrete day/stat tick cadence.
        tickAccumulator += dt;
        while (tickAccumulator >= SecondsPerTick)
        {
            tickAccumulator -= SecondsPerTick;
            bool newDay = Clock.Advance();
            OnTick?.Invoke();
            if (newDay)
                OnDayChanged?.Invoke(Clock.Day);
        }
    }
}
