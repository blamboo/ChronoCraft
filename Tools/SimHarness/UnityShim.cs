// UnityShim.cs — HEADLESS TEST SUPPORT ONLY. Not part of the Unity project.
// Lives OUTSIDE Assets/ so the Unity compiler never sees it (Unity supplies the real
// UnityEngine). It provides just enough of UnityEngine — Vector2Int, Vector3, Mathf —
// for the plain-C# simulation/world files to compile and run under Mono (mcs), so the
// ChronoCraft simulation can be exercised and verified without the editor.
namespace UnityEngine
{
    public struct Vector2Int
    {
        public int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }

        public static Vector2Int operator +(Vector2Int a, Vector2Int b)
            => new Vector2Int(a.x + b.x, a.y + b.y);
        public static Vector2Int operator -(Vector2Int a, Vector2Int b)
            => new Vector2Int(a.x - b.x, a.y - b.y);
        public static bool operator ==(Vector2Int a, Vector2Int b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(Vector2Int a, Vector2Int b) => !(a == b);

        public override bool Equals(object o)
        {
            if (!(o is Vector2Int)) return false;
            Vector2Int v = (Vector2Int)o;
            return v.x == x && v.y == y;
        }
        public override int GetHashCode() { unchecked { return (x * 73856093) ^ (y * 19349663); } }
        public override string ToString() { return "(" + x + ", " + y + ")"; }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }

    // Present so the headless build reproduces the UnityEngine/System 'Random' ambiguity
    // (CS0104) the editor hits — this forces simulation code to qualify System.Random.
    public static class Random
    {
        public static float value { get { return 0f; } }
        public static int Range(int minInclusive, int maxExclusive) { return minInclusive; }
        public static float Range(float min, float max) { return min; }
    }

    public static class Mathf
    {
        public static float Min(float a, float b) { return a < b ? a : b; }
        public static int   Min(int a, int b)     { return a < b ? a : b; }
        public static float Max(float a, float b) { return a > b ? a : b; }
        public static int   Max(int a, int b)     { return a > b ? a : b; }
        public static int   Abs(int a)            { return a < 0 ? -a : a; }
        public static float Abs(float a)          { return a < 0f ? -a : a; }
        public static float Sqrt(float a)         { return (float)System.Math.Sqrt(a); }
        public static int   RoundToInt(float a)   { return (int)System.Math.Round(a); }
        public static int   FloorToInt(float a)   { return (int)System.Math.Floor(a); }
        public static int   CeilToInt(float a)    { return (int)System.Math.Ceiling(a); }
        public static int   Clamp(int v, int lo, int hi)        { return v < lo ? lo : (v > hi ? hi : v); }
        public static float Clamp(float v, float lo, float hi)  { return v < lo ? lo : (v > hi ? hi : v); }
        public static float Lerp(float a, float b, float t)     { return a + (b - a) * t; }
    }
}
