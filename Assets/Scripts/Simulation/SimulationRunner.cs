// SimulationRunner.cs
// Version: 0.3 (fixed-step game-time loop; day length in seconds + live time scale)
// Purpose: The single MonoBehaviour bridge between Unity's frame loop and the plain-C#
//          Simulation. Converts real time into a continuous game-time stream advanced in
//          FIXED steps (frame-rate independent -- required for determinism and the future
//          time-rewind path), and exposes inspector controls (day length, global time
//          scale, pause) plus live read-outs. Contains NO simulation logic; it only paces
//          Simulation.Advance(). Other systems reach the sim through the Sim property.
// Location: Assets/Scripts/Simulation/SimulationRunner.cs
// Dependencies: UnityEngine; Simulation + SimulationClock (plain C#).
// Events emitted: none. Events consumed: Simulation.OnDayChanged (console log).

using UnityEngine;

public class SimulationRunner : MonoBehaviour
{
    // Fixed game-time step. Const so sim resolution / reproducibility cannot drift via
    // the inspector. 1/60 s gives smooth movement; raising it costs accuracy, lowering
    // it costs CPU. Do not change at runtime.
    private const double FixedStep = 1.0 / 60.0;
    private const int MaxStepsPerFrame = 300; // guards against a spiral of death

    [Header("Day length (applied when Play starts)")]
    [Tooltip("Real seconds in one in-game day at Time Scale 1. Default 1200 = 20 minutes. " +
             "Read at Awake, so changes take effect on the next Play.")]
    [Range(10f, 3600f)]
    [SerializeField] private float secondsPerDay = 1200f;

    [Tooltip("Logical day/stat ticks in one in-game day. Hunger and other over-time stats " +
             "drain on these ticks. Read at Awake; effective on the next Play.")]
    [Range(1, 96)]
    [SerializeField] private int ticksPerDay = 24;

    [Header("Time scale (drag live in Play mode)")]
    [Tooltip("Global multiplier on game-time. 1 = real time. Speeds the WHOLE sim equally " +
             "(day clock and NPC movement together). NPC base speed is set per-agent on " +
             "AgentManager and is independent of this.")]
    [Range(0.1f, 20f)]
    [SerializeField] private float timeScale = 1f;

    [Tooltip("Uncheck to pause the simulation; re-check to resume.")]
    [SerializeField] private bool running = true;

    [Header("Read-out (updates in Play mode -- editing has no effect)")]
    [SerializeField] private int currentDay;
    [SerializeField] private int currentTickOfDay;

    // The live simulation. Other systems read this (read-only) to subscribe to OnTick /
    // OnDayChanged or to spawn agents. Null until Awake.
    public Simulation Sim => sim;

    private Simulation sim;
    private double accumulator;

    void Awake()
    {
        sim = new Simulation(ticksPerDay, secondsPerDay);
        sim.OnDayChanged += HandleDayChanged;
        currentDay = sim.Clock.Day;
        currentTickOfDay = sim.Clock.TickOfDay;
    }

    void Update()
    {
        if (sim == null || !running)
            return;

        // Real frame time -> game time, then drain in fixed steps so the sim advances
        // deterministically regardless of frame rate. Time.deltaTime is clamped by
        // Time.maximumDeltaTime, and MaxStepsPerFrame caps catch-up after a hitch.
        accumulator += Time.deltaTime * timeScale;

        int steps = 0;
        while (accumulator >= FixedStep && steps < MaxStepsPerFrame)
        {
            sim.Advance(FixedStep);
            accumulator -= FixedStep;
            steps++;
        }
        if (steps == MaxStepsPerFrame)
            accumulator = 0.0; // dropped the backlog; better than freezing

        currentDay = sim.Clock.Day;
        currentTickOfDay = sim.Clock.TickOfDay;
    }

    // Advance one game-second while paused, for stepping through movement/behaviour.
    // Play-mode only (sim is constructed in Awake).
    [ContextMenu("Step One Second")]
    public void StepOneSecond()
    {
        if (sim == null)
            return;
        sim.Advance(1.0);
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
