// Adapted from dotnet/runtime 5535e31a712343a63f5d7d796cd874e563e5ac14,
// Random.Net5CompatImpl.cs CompatPrng. Copyright (c) .NET Foundation and Contributors.
// Licensed under the MIT license:
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
using System;
namespace SowSiege.Core
{
    internal sealed class PortableRandom
    {
        internal const int ArrayLength = 56;
        internal const int Last = 55;
        internal const int InitialOffset = 21;
        internal const int MixOffset = 30;
        internal const int MixLimit = 5;
        internal const int GoldenSeed = 161803398;
        internal const string Algorithm = "dotnet-seeded-subtractive-v1";
        internal readonly int[] Words = new int[ArrayLength];
        internal int Index;
        internal int Partner = InitialOffset;
        public long Draws { get; private set; }
        public PortableRandom(int seed)
        {
            unchecked
            {
                var mj = GoldenSeed - (seed == int.MinValue ? int.MaxValue : Math.Abs(seed));
                Words[Last] = mj; var mk = 1; var ii = 0;
                for (var i = 1; i < Last; i++)
                {
                    if ((ii += InitialOffset) >= Last) { ii -= Last; }
                    Words[ii] = mk; mk = mj - mk; if (mk < 0) { mk += int.MaxValue; }
                    mj = Words[ii];
                }
                for (var k = 1; k < MixLimit; k++)
                {
                    for (var i = 1; i < ArrayLength; i++)
                    {
                        var n = i + MixOffset; if (n >= Last) { n -= Last; }
                        Words[i] -= Words[1 + n]; if (Words[i] < 0) { Words[i] += int.MaxValue; }
                    }
                }
            }
        }
        public int Next(int limit)
        {
            if (limit < 0) { throw new ArgumentOutOfRangeException(nameof(limit)); }
            if (++Index >= ArrayLength) { Index = 1; }
            if (++Partner >= ArrayLength) { Partner = 1; }
            var result = unchecked(Words[Index] - Words[Partner]);
            if (result == int.MaxValue) { result--; }
            if (result < 0) { result += int.MaxValue; }
            Words[Index] = result; Draws++;
            var sample = result * (1.0 / int.MaxValue);
            return (int)(sample * limit);
        }
    }
}
