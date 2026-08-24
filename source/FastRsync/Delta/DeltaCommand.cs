namespace FastRsync.Delta
{
    /// <summary>
    /// The kind of a single delta command in a delta's command stream.
    /// The underlying values are the on-disk marker bytes that prefix each command in the delta
    /// format - do not change them, they are part of the binary format contract.
    /// </summary>
    public enum DeltaCommandType : byte
    {
        /// <summary>Copy a byte range from the basis file.</summary>
        CopyCommand = 0x60,

        /// <summary>Insert new bytes that are stored inline in the delta stream.</summary>
        DataCommand = 0x80
    }

    /// <summary>
    /// One entry in a delta's command "plan", as produced by
    /// <see cref="BinaryDeltaReader.ReadCommands"/>. It describes an operation without applying it,
    /// which lets callers reconstruct the target file by other means - for example, assembling an
    /// Azure block blob with Put Block From URL directly from the basis and delta blobs.
    /// </summary>
    public readonly struct DeltaCommand
    {
        public DeltaCommand(DeltaCommandType type, long offset, long length)
        {
            Type = type;
            Offset = offset;
            Length = length;
        }

        /// <summary>Whether these bytes come from the basis file (<see cref="DeltaCommandType.CopyCommand"/>) or from the delta stream (<see cref="DeltaCommandType.DataCommand"/>).</summary>
        public DeltaCommandType Type { get; }

        /// <summary>
        /// For <see cref="DeltaCommandType.CopyCommand"/>, the offset of the range within the basis file.
        /// For <see cref="DeltaCommandType.DataCommand"/>, the absolute offset of the payload within the
        /// delta stream (i.e. the delta blob), pointing at the raw new bytes themselves.
        /// </summary>
        public long Offset { get; }

        /// <summary>Length of the range, in bytes.</summary>
        public long Length { get; }
    }
}
