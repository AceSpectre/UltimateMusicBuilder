using Sma5h.Mods.Music.MusicMods.FolderMusicMod;
using System.Collections.Generic;
using Xunit;

namespace Tests.Unit
{
    public class StaticHelperTests
    {
        [Fact]
        public void IsWildcardSongs_StarReturnsTrue()
        {
            Assert.True(FolderMusicMod.IsWildcardSongs("*"));
        }

        [Fact]
        public void IsWildcardSongs_PaddedStarReturnsTrue()
        {
            Assert.True(FolderMusicMod.IsWildcardSongs(" * "));
        }

        [Fact]
        public void IsWildcardSongs_NullReturnsFalse()
        {
            Assert.False(FolderMusicMod.IsWildcardSongs(null));
        }

        [Fact]
        public void IsWildcardSongs_ListReturnsFalse()
        {
            Assert.False(FolderMusicMod.IsWildcardSongs(new List<string> { "song.wav" }));
        }

        [Fact]
        public void IsWildcardSongs_EmptyStringReturnsFalse()
        {
            Assert.False(FolderMusicMod.IsWildcardSongs(""));
        }

        [Fact]
        public void ExplicitSongs_ListReturnsItems()
        {
            var list = new List<object> { "song1.wav", "song2.wav" };
            var result = FolderMusicMod.ExplicitSongs(list);
            Assert.Equal(2, result.Count);
            Assert.Equal("song1.wav", result[0]);
            Assert.Equal("song2.wav", result[1]);
        }

        [Fact]
        public void ExplicitSongs_NullReturnsEmpty()
        {
            var result = FolderMusicMod.ExplicitSongs(null);
            Assert.Empty(result);
        }

        [Fact]
        public void ExplicitSongs_StringReturnsEmpty()
        {
            var result = FolderMusicMod.ExplicitSongs("*");
            Assert.Empty(result);
        }

        [Fact]
        public void ExplicitSongs_TrimsWhitespace()
        {
            var list = new List<object> { " song.wav ", "  other.wav  " };
            var result = FolderMusicMod.ExplicitSongs(list);
            Assert.Equal("song.wav", result[0]);
            Assert.Equal("other.wav", result[1]);
        }

        [Fact]
        public void ExplicitSongs_SkipsNullAndEmptyEntries()
        {
            var list = new List<object> { "song.wav", null, "", "  ", "other.wav" };
            var result = FolderMusicMod.ExplicitSongs(list);
            Assert.Equal(2, result.Count);
        }
    }
}
