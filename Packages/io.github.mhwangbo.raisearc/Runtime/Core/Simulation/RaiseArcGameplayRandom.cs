using System;

namespace RaiseArc.Core
{
    internal static class GameplayRandom
    {
        internal static int Inclusive(ref uint state, int minimum, int maximum)
        {
            if (minimum > maximum) throw new ArgumentException("Random minimum exceeds maximum.");
            if (minimum == maximum) return minimum;
            var span = (ulong)((long)maximum - minimum + 1);
            var limit = 4294967296UL - 4294967296UL % span;
            uint value;
            do
            {
                // A saved counter, rather than global Random, keeps failed transactions and replay deterministic.
                state = unchecked(state + 0x9e3779b9U);
                value = state;
                value = unchecked((value ^ (value >> 16)) * 0x21f0aaadU);
                value = unchecked((value ^ (value >> 15)) * 0x735a2d97U);
                value ^= value >> 15;
            } while (value >= limit);
            return (int)(minimum + (long)((ulong)value % span));
        }
    }
}
