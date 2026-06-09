// Civ.cs
// Version: 0.1 (initial -- civ identity for the two-civilization world)
// Purpose: Plain-C# civ identity enum. Every Agent (and later StructureNode, GridCell
//          owner, etc.) carries a CivId so systems can tell the two civilizations apart.
//          Pure sim data; no MonoBehaviour, no UnityEngine.
// Location: Assets/Scripts/Simulation/Civ.cs
// Dependencies: none.
// Events: none.

public enum CivId { None, Civ1, Civ2 }
