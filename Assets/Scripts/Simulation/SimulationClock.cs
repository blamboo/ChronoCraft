// SimulationClock.cs
// Purpose: Plain-C# logical clock for the TimeCraft prototype simulation. Counts
//          logical ticks and derives the current day and tick-of-day. The single
//          source of truth for in-game time. Deliberately contains no Unity types so
//          the simulation stays decoupled and snapshot-friendly (architecture
//          principle: sim state in plain C#, DOTS / time-rewind path preserved).
// Location: Assets/Scripts/Simulation/SimulationClock.cs
// Dependencies: System only. No UnityEngine.
// Events: none. Advanced once per logical tick by Simulation.Tick().

public class SimulationClock
{
    public int TicksPerDay { get; }
    public long TotalTicks { get; private set; }

    // Day is 1-based (the sim starts on Day 1); TickOfDay is 0-based within the day.
    public int Day => (int)(TotalTicks / TicksPerDay) + 1;
    public int TickOfDay => (int)(TotalTicks % TicksPerDay);

    public SimulationClock(int ticksPerDay)
    {
        TicksPerDay = System.Math.Max(1, ticksPerDay); // guard against divide-by-zero
        TotalTicks = 0;
    }

    // Advances exactly one logical tick. Returns true if this tick rolled into a new day.
    public bool Advance()
    {
        long dayBefore = TotalTicks / TicksPerDay;
        TotalTicks++;
        long dayAfter = TotalTicks / TicksPerDay;
        return dayAfter != dayBefore;
    }
}
