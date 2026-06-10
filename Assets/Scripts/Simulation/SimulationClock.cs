// SimulationClock.cs
// Version: 0.2 (added IsNight: second half of each day is night)
// Purpose: Plain-C# logical clock. Counts ticks; derives Day, TickOfDay, and IsNight.
//          Single source of truth for in-game time. No Unity types (snapshot-friendly).
//          Day splits in two: first half = day (work), second half = night (rest), per
//          GDD S7. IsNight is read by the decision controller to gate work/rest.
// Location: Assets/Scripts/Simulation/SimulationClock.cs
// Dependencies: System only. No UnityEngine.
// Events: none. Advanced once per logical tick by Simulation.

public class SimulationClock
{
    public int TicksPerDay { get; }
    public long TotalTicks { get; private set; }

    public int Day       => (int)(TotalTicks / TicksPerDay) + 1; // 1-based
    public int TickOfDay => (int)(TotalTicks % TicksPerDay);     // 0-based within day

    // Night is the second half of the day.
    public bool IsNight => TickOfDay >= TicksPerDay / 2;

    public SimulationClock(int ticksPerDay)
    {
        TicksPerDay = System.Math.Max(1, ticksPerDay);
        TotalTicks = 0;
    }

    public bool Advance()
    {
        long dayBefore = TotalTicks / TicksPerDay;
        TotalTicks++;
        long dayAfter = TotalTicks / TicksPerDay;
        return dayAfter != dayBefore;
    }
}
