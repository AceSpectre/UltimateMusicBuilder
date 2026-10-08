using Sma5h.Mods.Music.Helpers;
using Xunit;

namespace Tests.Unit
{
    public class VolumeHelperTests
    {
        [Theory]
        [InlineData(1.0f, 0f)]
        [InlineData(2.0f, 6.0206f)]
        [InlineData(0.5f, -6.0206f)]
        [InlineData(4.0f, 12.0412f)]
        public void MultiplierToDb_ConvertsLinearGain(float multiplier, float expectedDb)
        {
            Assert.Equal(expectedDb, VolumeHelper.MultiplierToDb(multiplier), precision: 3);
            Assert.Equal(multiplier, VolumeHelper.DbToMultiplier(expectedDb), precision: 3);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(-1f)]
        public void MultiplierToDb_NonPositiveIsMuted(float multiplier)
        {
            Assert.Equal(-90f, VolumeHelper.MultiplierToDb(multiplier));
        }
    }
}
