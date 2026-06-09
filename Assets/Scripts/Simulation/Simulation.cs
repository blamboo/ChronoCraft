// Simulation.cs
// Purpose: Plain-C# root of the TimeCraft prototype simulation. Owns world state
//          (currently just the clock; agents / resources / structures will live here
//          next) and advances it one fixed logical tick at a time. Contains no
//          MonoBehaviour and no UnityEngine references -- this is the sim/render
//          boundary the locked architecture principle protects. Future systems
//          subscribe to its tick events instead of running their own Update loops.
// Location: Assets/Scripts/Simulation/Simulation.cs
// Dependencies: System (for Action); SimulationClock.
// Events emitted: OnTick (every logical tick); OnDayChanged(int newDay) (on day rollover).
// Events consumed: none. Driven externally by SimulationRunner at a real-time cadence.

using System;

public class Simulation
{
    public SimulationClock Clock { get; }

    public event Action OnTick;
    public event Action<int> OnDayChanged;

    public Simulation(int ticksPerDay)
    {
        Clock = new SimulationClock(ticksPerDay);
    }

    // Advances the entire simulation by one fixed logical tick.
    // Order: advance time, notify per-tick subscribers, then notify day rollover.
    public void Tick()
    {
        bool newDay = Clock.Advance();
        OnTick?.Invoke();
        if (newDay)
            OnDayChanged?.Invoke(Clock.Day);
    }
}
