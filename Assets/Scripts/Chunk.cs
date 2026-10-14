namespace SandBXDSIM
{
    struct Chunk
    {
        public int x0, y0;//first cell (inclusive) x:0 y:0
        public int x1, y1;//last cell (exclusive) x:width y:height

        public byte awake;          // 0 = sleeping, 1 = awake, 2 = awake and updated this frame
        public bool renderDirty;    // something in here changed since the renderer last drew it

        public bool IsAwake => awake > 0;
    }
}