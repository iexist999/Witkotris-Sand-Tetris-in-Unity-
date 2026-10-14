namespace SandBXDSIM
{
    public struct Zone
    {
        public int x;
        public int y;

        public byte awake;          // 0 = sleeping, 1 = awake, 2 = awake and updated this frame
        public bool renderDirty;    // something in here changed since the renderer last drew it
    }
}