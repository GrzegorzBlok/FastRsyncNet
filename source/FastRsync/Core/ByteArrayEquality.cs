using System;

namespace FastRsync.Core
{
    internal static class ByteArrayEquality
    {
        // Replacement for StructuralComparisons.StructuralEqualityComparer, which boxes every
        // byte during comparison. Span.SequenceEqual is allocation-free and vectorized on modern
        // runtimes; the System.Memory package provides it on net462 and netstandard2.0.
        public static bool AreEqual(byte[] left, byte[] right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null)
                return false;
            return left.AsSpan().SequenceEqual(right);
        }
    }
}
