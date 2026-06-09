// Agent.cs
// Version: 0.3 (continuous, speed-based movement; decoupled from tick rate)
// Purpose: Plain-C# simulation agent (NPC) for the TimeCraft prototype. Holds a
//          continuous position in cell space and walks its path at its own Speed
//          (cells per game-second), independent of the day/stat tick rate. Pure sim
//          state -- no MonoBehaviour, no rendering -- so it stays on the decoupled sim
//          side (architecture principle) and is snapshot-friendly for time-rewind.
//          Deliberately "dumb": higher-level behaviour hands it a path via SetPath();
//          the agent only walks it.
// Location: Assets/Scripts/Simulation/Agent.cs
// Dependencies: System.Collections.Generic; UnityEngine for Vector2Int/Mathf only
//               (value types, not MonoBehaviour/render -- same precedent as GridData).
// Events: none. Advanced by Simulation.Advance(dt) each fixed sim step.

using System.Collections.Generic;
using UnityEngine;

public class Agent
{
    // Movement speed in cells per game-second. Set by the spawner; NOT tied to ticks.
    public float Speed = 3f;

    // Continuous position in cell space (x along Width, z along Depth via Vector2Int.y).
    public float PosX { get; private set; }
    public float PosZ { get; private set; }

    // Nearest logical cell, for systems that need a discrete cell (gathering, building).
    public int CellX => Mathf.RoundToInt(PosX);
    public int CellZ => Mathf.RoundToInt(PosZ);

    // Path being followed; nextIndex is the node currently being walked toward.
    private List<Vector2Int> path;
    private int nextIndex;

    public Agent(int startX, int startZ)
    {
        PosX = startX;
        PosZ = startZ;
    }

    // True while there is still a node ahead to reach.
    public bool HasPath => path != null && nextIndex < path.Count;

    // Assigns a new path. Snaps the agent onto path[0] (expected to be its current cell).
    public void SetPath(List<Vector2Int> newPath)
    {
        path = newPath;
        if (path != null && path.Count > 0)
        {
            PosX = path[0].x;
            PosZ = path[0].y;
            nextIndex = 1; // index 0 is the start we are already on
        }
        else
        {
            nextIndex = 0;
        }
    }

    // Advances continuous movement by dt game-seconds at Speed cells/sec. Consumes a
    // distance budget so movement is frame-rate independent and can cross several short
    // segments in one step.
    public void Advance(float dt)
    {
        if (!HasPath) return;

        float budget = Speed * dt;
        while (budget > 0f && HasPath)
        {
            Vector2Int target = path[nextIndex];
            float dx = target.x - PosX;
            float dz = target.y - PosZ;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);

            if (dist <= budget)
            {
                PosX = target.x;
                PosZ = target.y;
                budget -= dist;
                nextIndex++;
            }
            else
            {
                float inv = budget / dist;
                PosX += dx * inv;
                PosZ += dz * inv;
                budget = 0f;
            }
        }
    }
}
