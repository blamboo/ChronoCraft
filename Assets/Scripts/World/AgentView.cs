// AgentView.cs
// Version: 0.1 (initial -- per-agent live debug read-out in the Inspector)
// Purpose: Unity-side debug component attached to each agent capsule. Holds a reference
//          to the agent's plain-C# Agent + AgentBehavior and mirrors their live state into
//          serialized fields each frame, so selecting a capsule shows what that agent is
//          doing in the Inspector. Read-only instrument -- it never drives behavior.
//          Grows as Phase B adds needs (Thirst/Stamina/Health) and the decision model.
// Location: Assets/Scripts/World/AgentView.cs
// Dependencies: UnityEngine; Agent, AgentBehavior, CivId. Bound by AgentManager at spawn.
// Events: none.

using UnityEngine;

public class AgentView : MonoBehaviour
{
    [Header("Agent (read-only, live)")]
    [SerializeField] private string     civ    = "-";
    [SerializeField] private string     action = "-";
    [SerializeField] private float      hunger;
    [SerializeField] private int        wood;
    [SerializeField] private int        food;
    [SerializeField] private int        stone;
    [SerializeField] private Vector2Int cell;

    private Agent         agent;
    private AgentBehavior behavior;

    // Called by AgentManager right after the capsule is created.
    public void Bind(Agent a, AgentBehavior b)
    {
        agent    = a;
        behavior = b;
    }

    void Update()
    {
        if (agent == null) return;
        civ    = agent.Civ.ToString();
        action = behavior != null ? behavior.CurrentState.ToString() : "-";
        hunger = agent.Hunger;
        wood   = agent.WoodCarried;
        food   = agent.FoodCarried;
        stone  = agent.StoneCarried;
        cell   = new Vector2Int(agent.CellX, agent.CellZ);
    }
}
