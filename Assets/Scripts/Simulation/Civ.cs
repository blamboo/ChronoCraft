// Civ.cs
// Version: 0.2 (added CivState: per-civ record with a spawn anchor)
// Purpose: Plain-C# civ identity. CivId tags every Agent and StructureNode so systems
//          tell the two civilizations apart. CivState is the per-civ record the
//          Simulation registers at startup (its spawn anchor cell), read by systems that
//          need a civ's home location (e.g. StructureManager places one structure per
//          civ near its anchor; territory/storage will hang off this later).
//          Pure sim data; no MonoBehaviour, no UnityEngine.
// Location: Assets/Scripts/Simulation/Civ.cs
// Dependencies: none.
// Events: none.

public enum CivId { None, Civ1, Civ2 }

public class CivState
{
    public CivId Id;
    public int   AnchorX;   // spawn anchor cell (cells, not world units)
    public int   AnchorZ;

    public CivState(CivId id, int anchorX, int anchorZ)
    {
        Id = id; AnchorX = anchorX; AnchorZ = anchorZ;
    }
}
