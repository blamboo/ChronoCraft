// SeparationSystem.cs
// Version: 0.2 (Prototype v5: only space out STOPPED agents — never agents en route to a
//                goal, which was blocking them from reaching storage/resources)
// Purpose: A light steering pass that nudges apart any two STOPPED agents standing closer
//          than a minimum spacing, so soldiers in melee, foragers, drinkers, and resting
//          residents stand beside each other rather than overlapping. Agents that are
//          moving (following a path) are left alone: pushing them was overpowering their
//          per-step movement and stopping them from ever arriving at a shared cell such as
//          storage — they could not deposit and crowded around it. Movement and pathing are
//          unchanged; this only resolves overlaps once everyone has stopped.
//          Deliberately simple (boids-style separation, O(n^2) over the small prototype
//          population); a spatial hash is the scalable path if agent counts grow.
// Location: Assets/Scripts/Simulation/SeparationSystem.cs
// Dependencies: Simulation; Agent. No UnityEngine. Subscribes Simulation.OnStep.

public class SeparationSystem
{
    private readonly Simulation sim;

    public float MinSeparation  = 0.9f;   // cells; below this, two stopped agents are pushed apart
    public float MaxPushPerStep = 0.10f;  // cap on how far each agent moves per step

    public SeparationSystem(Simulation sim)
    {
        this.sim = sim;
        sim.OnStep += OnStep;
    }

    public void Dispose() { sim.OnStep -= OnStep; }

    private void OnStep(float dt)
    {
        var ags = sim.Agents;
        float min2 = MinSeparation * MinSeparation;

        for (int i = 0; i < ags.Count; i++)
        {
            Agent a = ags[i];
            if (!a.IsAlive || a.HasPath) continue;   // moving agents are left to reach their goal
            for (int j = i + 1; j < ags.Count; j++)
            {
                Agent b = ags[j];
                if (!b.IsAlive || b.HasPath) continue;

                float dx = a.PosX - b.PosX, dz = a.PosZ - b.PosZ;
                float d2 = dx * dx + dz * dz;
                if (d2 >= min2) continue;

                float d = (float)System.Math.Sqrt(d2);
                float nx, nz;
                if (d > 1e-4f) { nx = dx / d; nz = dz / d; }
                else
                {
                    // Exactly overlapping: split along a deterministic axis so they part.
                    nx = ((a.Id & 1) == 0) ? 1f : -1f; nz = 0f; d = 0f;
                }

                float push = (MinSeparation - d) * 0.5f;
                if (push > MaxPushPerStep) push = MaxPushPerStep;

                a.Nudge(nx * push, nz * push);
                b.Nudge(-nx * push, -nz * push);
            }
        }
    }
}

