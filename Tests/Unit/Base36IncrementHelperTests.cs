using Sma5h.Helpers;
using Xunit;

namespace Tests.Unit
{
    /// <summary>
    /// Base36 round-trip used to generate/parse in-game NameIds. Hand-rolled ASCII
    /// math (3-char zero-pad, digit/letter split at 10) — round-trip must be exact.
    /// </summary>
    public class Base36IncrementHelperTests
    {
        [Theory]
        [InlineData(0, "000")]
        [InlineData(1, "001")]
        [InlineData(9, "009")]
        [InlineData(10, "00A")]
        [InlineData(35, "00Z")]
        [InlineData(36, "010")]
        [InlineData(37, "011")]
        [InlineData(71, "01Z")]
        [InlineData(1295, "0ZZ")] // 36^2 - 1 → two digits, zero-padded to 3
        [InlineData(46655, "ZZZ")] // 36^3 - 1
        public void ToString_ProducesExpectedBase36(int counter, string expected)
        {
            Assert.Equal(expected, Base36IncrementHelper.ToString(counter));
        }

        [Theory]
        [InlineData("000", 0)]
        [InlineData("001", 1)]
        [InlineData("00A", 10)]
        [InlineData("00Z", 35)]
        [InlineData("010", 36)]
        [InlineData("0ZZ", 1295)]
        [InlineData("ZZZ", 46655)]
        public void ToInt_ParsesBase36(string text, int expected)
        {
            Assert.Equal(expected, Base36IncrementHelper.ToInt(text));
        }

        [Fact]
        public void ToStringThenToInt_IsIdentity()
        {
            for (int i = 0; i <= 5000; i++)
                Assert.Equal(i, Base36IncrementHelper.ToInt(Base36IncrementHelper.ToString(i)));
        }

        [Fact]
        public void ToString_PadsToAtLeastThreeChars()
        {
            Assert.Equal(3, Base36IncrementHelper.ToString(0).Length);
            Assert.Equal(3, Base36IncrementHelper.ToString(35).Length);
        }

        [Fact]
        public void ToString_GrowsBeyondThreeCharsForLargeValues()
        {
            // 36^3 = 46656 needs a 4th char.
            Assert.Equal("1000", Base36IncrementHelper.ToString(46656));
            Assert.Equal(46656, Base36IncrementHelper.ToInt("1000"));
        }
    }
}
