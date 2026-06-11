// AgentView.cs
// Version: 0.2 (shows the decision-controller Action + the four needs)
// Purpose: Unity-side debug component on each agent capsule. Mirrors its agent's live
//          state (civ, current action/intent, the four needs, inventory, cell) into the
//          Inspector. Read-only instrument -- never drives behavior.
// Location: Assets/Scripts/World/AgentView.cs
// Dependencies: UnityEngine; Agent, AgentBehavior, CivId. Bound by AgentManager at spawn.
// Events: none.

using UnityEngine;

public class AgentView : MonoBehaviour
{
    [Header("Agent (read-only, live)")]
    [SerializeField] private string civ    = "-";
    [SerializeField] private string action = "-";

    [Header("Needs (0..100)")]
    [SerializeField] private float hunger;
    [SerializeField] private float thirst;
    [SerializeField] private float stamina;
    [SerializeField] private float health;

    [Header("Home")]
    [SerializeField] private string homeCell   = "-";
    [SerializeField] private bool   homeBuilt;

    [Header("Inventory / position")]
    [SerializeField] private int        wood;
    [SerializeField] private int        food;
    [SerializeField] private int        stone;
    [SerializeField] private Vector2Int cell;

    private Agent         agent;
    private AgentBehavior behavior;

    public void Bind(Agent a, AgentBehavior b) { agent = a; behavior = b; }

    void Update()
    {
        if (agent == null) return;
        civ     = agent.Civ.ToString();
        action  = behavior != null ? behavior.Action : "-";
        hunger  = agent.Hunger;
        thirst  = agent.Thirst;
        stamina = agent.Stamina;
        health  = agent.Health;
        wood    = agent.WoodCarried;
        food    = agent.FoodCarried;
        stone   = agent.StoneCarried;
        cell    = new Vector2Int(agent.CellX, agent.CellZ);
        homeCell  = agent.Home != null ? $"({agent.Home.CellX},{agent.Home.CellZ})" : "none";
        homeBuilt = agent.Home != null && agent.Home.IsBuilt;
    }
}
