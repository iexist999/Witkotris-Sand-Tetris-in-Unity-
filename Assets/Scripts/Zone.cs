namespace SandBXDSIM
{
    public struct Zone
    {
        public int x, y;            // zone coordinates
        public int x0, y0;          // first area (inclusive)
        public int x1, y1;          // last area (exclusive)

        public byte awake;          // 0 = sleeping, 1 = awake (has at least one awake area)
        public bool renderDirty;    // something in here changed since the renderer last drew it

        public bool IsAwake => awake > 0;
    }
}