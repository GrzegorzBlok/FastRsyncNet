namespace FastRsync.Delta
{
    public class DeltaMetadata
    {
        public string HashAlgorithm { get; set; }
        public string ExpectedFileHashAlgorithm { get; set; }
        public string ExpectedFileHash { get; set; }
        public string BaseFileHashAlgorithm { get; set; }
        public string BaseFileHash { get; set; }

        /// <summary>
        /// Length in bytes of the basis file the delta was created against, or null when the
        /// delta was produced by an older version or from a signature without that information.
        /// </summary>
        public long? BaseFileLength { get; set; }

        /// <summary>
        /// Length in bytes of the file the delta reproduces, or null when the delta was
        /// produced by an older version. Used to preallocate the output when applying.
        /// </summary>
        public long? TargetFileLength { get; set; }
    }
}
