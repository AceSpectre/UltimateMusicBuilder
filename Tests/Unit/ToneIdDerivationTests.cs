using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.MusicMods.FolderMusicMod;
using Xunit;

namespace Tests.Unit
{
    public class ToneIdDerivationTests
    {
        [Theory]
        [InlineData("song.flac", "song")]
        [InlineData("My Song.nus3audio", "my_song")]
        [InlineData("UPPER CASE.wav", "upper_case")]
        [InlineData("track_01.mp3", "track_01")]
        public void SimpleFilenames(string filename, string expected)
        {
            Assert.Equal(expected, FolderMusicMod.DeriveToneId(filename));
        }

        [Theory]
        [InlineData("KARTS!.flac", "karts")]
        [InlineData("You Won!.flac", "you_won")]
        [InlineData("You Lost....flac", "you_lost")]
        [InlineData("bird's song.flac", "bird_s_song")]
        [InlineData("stars & chill.flac", "stars___chill")]
        [InlineData("7pm.flac", "7pm")]
        public void SpecialCharactersReplacedWithUnderscore(string filename, string expected)
        {
            Assert.Equal(expected, FolderMusicMod.DeriveToneId(filename));
        }

        [Theory]
        [InlineData("---leading.flac", "leading")]
        [InlineData("trailing---.flac", "trailing")]
        [InlineData("___both___.flac", "both")]
        public void LeadingTrailingUnderscoresTrimmed(string filename, string expected)
        {
            Assert.Equal(expected, FolderMusicMod.DeriveToneId(filename));
        }

        [Fact]
        public void LongFilenameTruncatedToMaxSize()
        {
            var longName = new string('a', 100) + ".flac";
            var result = FolderMusicMod.DeriveToneId(longName);
            Assert.Equal(MusicConstants.GameResources.ToneIdMaximumSize, result.Length);
            Assert.Equal(new string('a', 47), result);
        }

        [Fact]
        public void TestDataDevTrackIds()
        {
            var id1 = FolderMusicMod.DeriveToneId("flowerhead - Somewhat Good- Karts - 01 KARTS!.flac");
            Assert.StartsWith("flowerhead", id1);
            Assert.Contains("karts", id1);
            Assert.DoesNotContain("!", id1);
            Assert.DoesNotContain(" ", id1);
            Assert.True(id1.Length <= MusicConstants.GameResources.ToneIdMaximumSize);
        }

        [Fact]
        public void TestDataMarioTrackIds()
        {
            var id = FolderMusicMod.DeriveToneId("flowerhead - Somewhat Good- Lofi - 01 summer.flac");
            Assert.StartsWith("flowerhead", id);
            Assert.Contains("summer", id);
            Assert.True(id.Length <= MusicConstants.GameResources.ToneIdMaximumSize);
        }

        [Fact]
        public void AllTestTrackIdsUnique()
        {
            var allFiles = new[]
            {
                "flowerhead - Somewhat Good- Karts - 01 KARTS!.flac",
                "flowerhead - Somewhat Good- Karts - 02 Choose Your Racer.flac",
                "flowerhead - Somewhat Good- Karts - 03 Flowey Speedway.flac",
                "flowerhead - Somewhat Good- Karts - 04 Palmtree Square.flac",
                "flowerhead - Somewhat Good- Karts - 05 Bouncing Pyramids.flac",
                "flowerhead - Somewhat Good- Karts - 06 Retro Roundabout.flac",
                "flowerhead - Somewhat Good- Karts - 07 Stadium 64.flac",
                "flowerhead - Somewhat Good- Karts - 08 Highway Hustle.flac",
                "flowerhead - Somewhat Good- Karts - 09 Rainbow Way.flac",
                "flowerhead - Somewhat Good- Karts - 10 You Won!.flac",
                "flowerhead - Somewhat Good- Karts - 11 You Lost....flac",
                "flowerhead - Somewhat Good- Karts - 12 Race Results.flac",
                "flowerhead - Somewhat Good- Karts - 13 Time Trials.flac",
                "flowerhead - Somewhat Good- Lofi - 01 summer.flac",
                "flowerhead - Somewhat Good- Lofi - 02 rainy day.flac",
                "flowerhead - Somewhat Good- Lofi - 03 brain empty.flac",
                "flowerhead - Somewhat Good- Lofi - 04 bird's song.flac",
                "flowerhead - Somewhat Good- Lofi - 05 stars & chill.flac",
                "flowerhead - Somewhat Good- Lofi - 06 7pm.flac"
            };

            var ids = allFiles.Select(f => FolderMusicMod.DeriveToneId(f)).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }

        [Fact]
        public void AllTestTrackIdsWithinMaxSize()
        {
            var allFiles = new[]
            {
                "flowerhead - Somewhat Good- Karts - 01 KARTS!.flac",
                "flowerhead - Somewhat Good- Karts - 02 Choose Your Racer.flac",
                "flowerhead - Somewhat Good- Karts - 03 Flowey Speedway.flac",
                "flowerhead - Somewhat Good- Karts - 04 Palmtree Square.flac",
                "flowerhead - Somewhat Good- Karts - 05 Bouncing Pyramids.flac",
                "flowerhead - Somewhat Good- Karts - 06 Retro Roundabout.flac",
                "flowerhead - Somewhat Good- Karts - 07 Stadium 64.flac",
                "flowerhead - Somewhat Good- Karts - 08 Highway Hustle.flac",
                "flowerhead - Somewhat Good- Karts - 09 Rainbow Way.flac",
                "flowerhead - Somewhat Good- Karts - 10 You Won!.flac",
                "flowerhead - Somewhat Good- Karts - 11 You Lost....flac",
                "flowerhead - Somewhat Good- Karts - 12 Race Results.flac",
                "flowerhead - Somewhat Good- Karts - 13 Time Trials.flac",
                "flowerhead - Somewhat Good- Lofi - 01 summer.flac",
                "flowerhead - Somewhat Good- Lofi - 02 rainy day.flac",
                "flowerhead - Somewhat Good- Lofi - 03 brain empty.flac",
                "flowerhead - Somewhat Good- Lofi - 04 bird's song.flac",
                "flowerhead - Somewhat Good- Lofi - 05 stars & chill.flac",
                "flowerhead - Somewhat Good- Lofi - 06 7pm.flac"
            };

            foreach (var file in allFiles)
            {
                var id = FolderMusicMod.DeriveToneId(file);
                Assert.True(id.Length <= MusicConstants.GameResources.ToneIdMaximumSize,
                    $"ToneId '{id}' from '{file}' exceeds max size {MusicConstants.GameResources.ToneIdMaximumSize}");
            }
        }
    }
}
