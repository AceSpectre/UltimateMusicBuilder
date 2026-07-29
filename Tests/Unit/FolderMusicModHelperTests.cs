using System.Collections.Generic;
using Sma5h.Mods.Music.Models;
using Sma5h.Mods.Music.MusicMods.FolderMusicMod;
using Tests.Helpers;
using Xunit;

namespace Tests.Unit
{
    /// <summary>
    /// Pure private helpers on FolderMusicMod reached via reflection (kept private
    /// in production; tested here without changing their visibility):
    /// NormalizeRecordType, BuildPlaylistEntry, ToKebabCase, ReadBE32.
    /// </summary>
    public class FolderMusicModHelperTests
    {
        // ── NormalizeRecordType ─────────────────────────────────────────────

        [Theory]
        [InlineData(null, "record_original")]
        [InlineData("", "record_original")]
        [InlineData("   ", "record_original")]
        [InlineData("record_arrange", "record_arrange")]
        [InlineData("record_new_arrange", "record_new_arrange")]
        [InlineData("record_bogus", "record_original")] // prefixed but not whitelisted
        [InlineData("arrange", "record_arrange")]
        [InlineData("ORIGINAL", "record_original")]     // lowercased before prefixing
        [InlineData("new_arrange", "record_new_arrange")]
        [InlineData("bogus", "record_original")]
        public void NormalizeRecordType_MapsToValidRecordType(string input, string expected)
        {
            var result = Reflect.InvokeStatic<string>(
                typeof(FolderMusicMod), "NormalizeRecordType", input);
            Assert.Equal(expected, result);
        }

        // ── BuildPlaylistEntry ──────────────────────────────────────────────

        [Fact]
        public void BuildPlaylistEntry_SetsIdOrderAndClampedIncidence()
        {
            var tracks = new List<(string uiBgmId, int incidence)>
            {
                ("ui_bgm_a", 5),
                ("ui_bgm_b", 70000), // above ushort.MaxValue → clamps to 65535
                ("ui_bgm_c", -3),    // below 0 → clamps to 0
            };

            var entry = Reflect.InvokeStatic<PlaylistEntry>(
                typeof(FolderMusicMod), "BuildPlaylistEntry", "bgm_test", tracks);

            Assert.Equal("bgm_test", entry.Id);
            Assert.Equal(3, entry.Tracks.Count);

            Assert.Equal("ui_bgm_a", entry.Tracks[0].UiBgmId);
            Assert.Equal((short)0, entry.Tracks[0].Order0);
            Assert.Equal((ushort)5, entry.Tracks[0].Incidence0);
            // Order/incidence fan out identically across all 16 slots.
            Assert.Equal((short)0, entry.Tracks[0].Order15);
            Assert.Equal((ushort)5, entry.Tracks[0].Incidence15);

            Assert.Equal((short)1, entry.Tracks[1].Order0);
            Assert.Equal(ushort.MaxValue, entry.Tracks[1].Incidence0);

            Assert.Equal((short)2, entry.Tracks[2].Order0);
            Assert.Equal((ushort)0, entry.Tracks[2].Incidence0);
        }

        // ── ToKebabCase ─────────────────────────────────────────────────────

        [Theory]
        [InlineData("FinalFantasy", "final-fantasy")]
        [InlineData("Final", "final")]
        [InlineData("ABC", "a-b-c")]
        [InlineData("already", "already")]
        [InlineData("", "")]
        public void ToKebabCase_InsertsDashesBeforeInnerCapitals(string input, string expected)
        {
            var result = Reflect.InvokeStatic<string>(
                typeof(FolderMusicMod), "ToKebabCase", input);
            Assert.Equal(expected, result);
        }

        // ── ReadBE32 ────────────────────────────────────────────────────────

        [Fact]
        public void ReadBE32_ReadsBigEndianUint()
        {
            Assert.Equal(1u, Reflect.InvokeStatic<uint>(typeof(FolderMusicMod), "ReadBE32",
                new byte[] { 0x00, 0x00, 0x00, 0x01 }, 0));
            Assert.Equal(0x12345678u, Reflect.InvokeStatic<uint>(typeof(FolderMusicMod), "ReadBE32",
                new byte[] { 0x12, 0x34, 0x56, 0x78 }, 0));
            Assert.Equal(uint.MaxValue, Reflect.InvokeStatic<uint>(typeof(FolderMusicMod), "ReadBE32",
                new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, 0));
        }

        [Fact]
        public void ReadBE32_HonorsOffset()
        {
            var data = new byte[] { 0xAA, 0xBB, 0x00, 0x00, 0x01, 0x00 };
            Assert.Equal(0x00000100u,
                Reflect.InvokeStatic<uint>(typeof(FolderMusicMod), "ReadBE32", data, 2));
        }
    }
}
