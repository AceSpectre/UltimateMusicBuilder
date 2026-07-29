using Sma5h.Mods.Music.Helpers;
using Xunit;

namespace Tests.Unit
{
    /// <summary>
    /// Byte-pattern search used when patching binary game resources. Boundary
    /// conditions (candidate longer than remaining bytes, empty inputs, multiple
    /// matches) are exactly where index math silently regresses.
    /// </summary>
    public class ByteHelperTests
    {
        [Fact]
        public void Locate_FindsAllMatchPositions()
        {
            var array = new byte[] { 1, 2, 3, 2, 3, 9 };
            var hits = array.Locate(new byte[] { 2, 3 });
            Assert.Equal(new[] { 1, 3 }, hits);
        }

        [Fact]
        public void Locate_MatchAtStartAndEnd()
        {
            var array = new byte[] { 7, 7, 0, 7, 7 };
            var hits = array.Locate(new byte[] { 7, 7 });
            Assert.Equal(new[] { 0, 3 }, hits);
        }

        [Fact]
        public void Locate_NoMatchReturnsEmpty()
        {
            var array = new byte[] { 1, 2, 3 };
            Assert.Empty(array.Locate(new byte[] { 4, 5 }));
        }

        [Fact]
        public void Locate_CandidateLongerThanArrayReturnsEmpty()
        {
            var array = new byte[] { 1, 2 };
            Assert.Empty(array.Locate(new byte[] { 1, 2, 3 }));
        }

        [Fact]
        public void IsMatch_TrueWhenPatternPresentAtPosition()
        {
            var array = new byte[] { 0, 1, 2, 3 };
            Assert.True(ByteHelper.IsMatch(array, 1, new byte[] { 1, 2 }));
        }

        [Fact]
        public void IsMatch_FalseWhenCandidateOverrunsEnd()
        {
            var array = new byte[] { 0, 1, 2 };
            Assert.False(ByteHelper.IsMatch(array, 2, new byte[] { 2, 9 }));
        }

        [Fact]
        public void IsMatch_FalseOnMismatch()
        {
            var array = new byte[] { 0, 1, 2, 3 };
            Assert.False(ByteHelper.IsMatch(array, 1, new byte[] { 1, 9 }));
        }

        [Theory]
        [InlineData(true, true)]   // both null
        [InlineData(false, true)]  // candidate null
        public void IsEmptyLocate_NullInputs(bool arrayNull, bool candidateNull)
        {
            var array = arrayNull ? null : new byte[] { 1 };
            var candidate = candidateNull ? null : new byte[] { 1 };
            Assert.True(ByteHelper.IsEmptyLocate(array, candidate));
        }

        [Fact]
        public void IsEmptyLocate_EmptyArraysAndOversizeCandidate()
        {
            Assert.True(ByteHelper.IsEmptyLocate(new byte[0], new byte[] { 1 }));
            Assert.True(ByteHelper.IsEmptyLocate(new byte[] { 1 }, new byte[0]));
            Assert.True(ByteHelper.IsEmptyLocate(new byte[] { 1 }, new byte[] { 1, 2 }));
        }

        [Fact]
        public void IsEmptyLocate_FalseForValidInputs()
        {
            Assert.False(ByteHelper.IsEmptyLocate(new byte[] { 1, 2 }, new byte[] { 1 }));
        }
    }
}
