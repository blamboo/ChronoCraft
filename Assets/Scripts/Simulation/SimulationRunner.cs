// SimulationRunner.cs
// Purpose: The single MonoBehaviour bridge between Unity's frame loop and the plain-C#
//          Simulation, for the TimeCraft prototype. Owns one Simulation, converts real
//          time into whole logical ticks via an accumulator (fixed logical step,
//          independent of frame rate -- required for determinism and the future
//          time-rewind path), and exposes inspector controls (day speed, ticks/day,
//          pause) plus live read-outs. Contains NO simulation logic; it only paces
//          Simulation.Tick(). Other systems reach the sim through the Sim property to
//          subscribe to its tick events.
// Location: Assets/Scripts/Simulation/SimulationRunner.cs
// Dependencies: UnityEngine; Simulation + SimulationClock (plain C#).
// Events emitted: none. Events consumed: Simulation.OnDayChanged (for a console log).

using UnityEngine;

public class SimulationRunner : MonoBehaviour
{
    [Header("Clock setup (applied when Play starts)")]
    [Tooltip("Logical ticks in one in-game day. One tick is the smallest sim step. " +
             "Read at Awake, so changes take effect on the next Play, not live -- this " +
             "avoids the day number jumping mid-run.")]
    [Range(1, 96)]
    [SerializeField] private int ticksPerDay = 24;

    [Header("Day speed (drag live in Play mode)")]
    [Tooltip("Logical ticks advanced per real second. A day lasts ticksPerDay / " +
             "ticksPerSecond seconds. Changing this only changes the rate, never the " +
             "sim's results.")]
    [Range(0.25f, 20f)]
    [SerializeField] private float ticksPerSecond = 4f;

    [Tooltip("Uncheck to pause the simulation; re-check to resume.")]
    [SerializeField] private bool running = true;

    [Header("Read-out (updates in Play mode -- editing has no effect)")]
    [SerializeField] private int currentDay;
    [SerializeField] private int currentTickOfDay;

    // The live simulation. Other systems read this (read-only) to subscribe to OnTick /
    // OnDayChanged, mirroring the GridManager.Grid ownership pattern. Null until Awake.
    public Simulation Sim => sim;

    private Simulation sim;
    private double accumulator;

    void Awake()
    {
        sim = new Simulation(ticksPerDay);
        sim.OnDayChanged += HandleDayChanged;
        currentDay = sim.Clock.Day;
        currentTickOfDay = sim.Clock.TickOfDay;
    }

    void Update()
    {
        if (sim == null || !running)
            return;

        // Time.deltaTime is clamped by Time.maximumDeltaTime (default 0.333s), so a
        // frame hitch cannot make this loop run away at the current speed range. If
        // ticksPerSecond's max is ever raised far higher, add a per-frame tick cap
        // here. Flagged as the scalable guard, not needed now.
        accumulator += Time.deltaTime * ticksPerSecond;

        // Run only whole ticks; carry the fractional remainder into the next frame so
        // the sim advances at a fixed logical step regardless of frame rate.
        while (accumulator >= 1.0)
        {
            sim.Tick();
            accumulator -= 1.0;
        }

        currentDay = sim.Clock.Day;
        currentTickOfDay = sim.Clock.TickOfDay;
    }

    // Advance exactly one logical tick. Useful for stepping through behaviour while
    // paused. Play-mode only (sim is constructed in Awake); does nothing in edit mode.
    [ContextMenu("Step One Tick")]
    public void StepOneTick()
    {
        if (sim == null)
            return;
        sim.Tick();
        currentDay = sim.Clock.Day;
        currentTickOfDay = sim.Clock.TickOfDay;
    }

    void HandleDayChanged(int newDay)
    {
        Debug.Log($"[Sim] Day {newDay} begins.");
    }

    void OnDestroy()
    {
        if (sim != null)
            sim.OnDayChanged -= HandleDayChanged;
    }
}
