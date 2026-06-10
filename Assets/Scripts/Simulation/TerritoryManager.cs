// TerritoryManager.cs
// Version: 0.2 (fixed: sim.Civs is List<CivState> not Dictionary; removed SpawnAnchor)
// Purpose: MonoBehaviour bridge for TerritorySystem. Exposes territory radii to the
//          Inspector, initialises start territories after the simulation is ready, and
//          draws the Scene-view gizmo overlay (territory ownership colours).
//          In-game overlay is deferred; toggle is Inspector enum for now (A4 scope).
// Location: Assets/Scripts/Simulation/TerritoryManager.cs
// Dependencies: TerritorySystem, GridData (via GridManager), Simulation (via SimulationRunner),
//               Civ (CivId, CivState).
// Events: none.

using UnityEngine;

public class TerritoryManager : MonoBehaviour
{
    // ── Scene references ──────────────────────────────────────────────────────────
    [Header("Scene References")]
    [Tooltip("The SimulationRunner on the Simulation GameObject.")]
    [SerializeField] private SimulationRunner _runner;

    [Tooltip("The GridManager on the ProceduralTerrain GameObject.")]
    [SerializeField] private GridManager      _gridManager;

    // ── Territory sizes ───────────────────────────────────────────────────────────
    [Header("Territory Sizes (cells from anchor)")]
    [Tooltip("Outer civ territory half-extent. Cells = (2r+1)^2.")]
    [Range(8, 64)]
    [SerializeField] private int _civTerritoryRadius  = 24;

    [Tooltip("Inner town territory (buildable zone) half-extent. Must be < CivRadius.")]
    [Range(4, 32)]
    [SerializeField] private int _townTerritoryRadius = 12;

    // ── Gizmo overlay ─────────────────────────────────────────────────────────────
    [Header("Scene Gizmo Overlay")]
    [Tooltip("Which overlay to draw in the Scene view.")]
    [SerializeField] private OverlayMode _overlayMode = OverlayMode.Territory;

    [SerializeField] private Color _civ1Color       = new Color(0.2f, 0.4f, 1f,   0.35f);
    [SerializeField] private Color _civ2Color       = new Color(1f,   0.3f, 0.2f, 0.35f);
    [SerializeField] private Color _neutralColor    = new Color(0.5f, 0.5f, 0.5f, 0.15f);
    [SerializeField] private Color _walkableColor   = new Color(0f,   1f,   0f,   0.25f);
    [SerializeField] private Color _unwalkableColor = new Color(1f,   0f,   0f,   0.25f);
    [SerializeField] private Color _waterColor      = new Color(0f,   0.5f, 1f,   0.35f);

    public enum OverlayMode { Off, Territory, Walkable }

    // ── Runtime ───────────────────────────────────────────────────────────────────
    private TerritorySystem _territory;
    private bool            _initialised;

    private void Start()
    {
        _territory = new TerritorySystem
        {
            CivTerritoryRadius  = _civTerritoryRadius,
            TownTerritoryRadius = _townTerritoryRadius
        };
    }

    private void Update()
    {
        if (_initialised) return;
        if (_runner == null || _gridManager == null) return;

        var sim = _runner.Sim;
        if (sim == null || sim.Civs == null || sim.Civs.Count == 0) return;

        _territory.InitialiseStartTerritories(_gridManager.Grid, sim.Civs);
        _initialised = true;
        Debug.Log("[TerritoryManager] Start territories initialised.");
    }

    /// <summary>
    /// Exposes the territory system to other managers (StructureManager, Builder job).
    /// Returns null until initialised.
    /// </summary>
    public TerritorySystem Territory => _initialised ? _territory : null;

    // ── Scene gizmo ───────────────────────────────────────────────────────────────
    private void OnDrawGizmos()
    {
        if (_overlayMode == OverlayMode.Off) return;
        if (_gridManager == null) return;

        GridData grid = _gridManager.Grid;
        if (grid == null || grid.Cells == null) return;

        float cs = grid.CellSize;

        for (int z = 0; z < grid.Depth; z++)
        for (int x = 0; x < grid.Width; x++)
        {
            var cell = grid.Cells[x, z];
            Color c;

            if (_overlayMode == OverlayMode.Territory)
            {
                c = cell.Owner switch
                {
                    CivId.Civ1 => _civ1Color,
                    CivId.Civ2 => _civ2Color,
                    _          => _neutralColor
                };
            }
            else // Walkable
            {
                if (cell.IsWater)       c = _waterColor;
                else if (cell.Walkable) c = _walkableColor;
                else                   c = _unwalkableColor;
            }

            if (c.a < 0.01f) continue;

            Vector3 centre = _gridManager.transform.TransformPoint(grid.CellToLocal(x, z));
            centre.y += 0.05f; // raise slightly above terrain surface

            Gizmos.color = c;
            Gizmos.DrawCube(centre, new Vector3(cs * 0.9f, 0.05f, cs * 0.9f));
        }
    }
}
