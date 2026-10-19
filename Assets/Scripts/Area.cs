namespace SandBXDSIM
{
    public struct Area
    {
        /// <summary>
        /// area coordinates.
        /// </summary>
        public int x, y;
        /// <summary>
        /// first chunk coordinates (inclusive).
        /// </summary>
        public int x0, y0;
        /// <summary>
        /// last chunk coordinates (exclusive, index starts at 0).
        /// </summary>
        public int x1, y1;
        /// <summary>
        /// index of the zone this area belongs to.
        /// </summary>
        public int zone;

        /// <summary>
        /// determines whether this area is awake and actively simulated.
        /// <para>0 = asleep</para>
        /// <para>1 = awake</para>
        /// </summary>
        public byte awake;
        /// <summary>
        /// determines whether this area is marked as dirty and needs to be rendered.
        /// <para>true = dirty (scheduled for render)</para>
        /// <para>false = clean</para>
        /// </summary>
        public bool renderDirty;

        public bool IsAwake => awake > 0;
    }
}