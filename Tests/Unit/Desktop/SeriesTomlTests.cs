using UMB.CLI.Desktop;
using UMB.CLI.Services;
using Xunit;

namespace Tests.Unit.Desktop
{
    public class SeriesTomlTests
    {
        private const string Sample =
            "# comment\n[series]\nid = \"my_series\"\nname = \"My Series\"\nseries-playlist = \"bgm_mine\"\n\n" +
            "[[games]]\nid = \"game_one\"\nname = \"Game One\"\n\n[[games]]\nid = \"game_two\"\n\n" +
            "[default-track-data]\ngame = \"game_one\"\nrecord-type = \"arrange\"\n";

        [Fact]
        public void TableSection_ReturnsOnlyThatTable()
        {
            var series = SeriesToml.TableSection(Sample, "series");

            Assert.Contains("id = \"my_series\"", series);
            Assert.DoesNotContain("game_one", series);
            Assert.Null(SeriesToml.TableSection(Sample, "missing"));
        }

        [Fact]
        public void Games_ScopesEachBlock_AndDefaultsNameToId()
        {
            Assert.Equal(2, SeriesToml.TableArrayBlocks(Sample, "games").Count);
            Assert.Equal(new[] { new SeriesGame("game_one", "Game One"), new SeriesGame("game_two", "game_two") }, SeriesToml.Games(Sample));
        }

        [Fact]
        public void String_ReadsQuotedValues_AndEscapeHandlesSpecials()
        {
            var defaults = SeriesToml.TableSection(Sample, "default-track-data");

            Assert.Equal("arrange", SeriesToml.String(defaults, "record-type"));
            Assert.Equal("", SeriesToml.String(defaults, "absent"));
            Assert.Equal("say \\\"hi\\\" \\\\ bye", SeriesToml.Escape("say \"hi\" \\ bye"));
            Assert.Equal("", SeriesToml.Escape(null));
        }

        [Fact]
        public void ReadIdList_ReadsEveryQuotedId_AndIsEmptyForAMissingFile()
        {
            var path = Path.Combine(Path.GetTempPath(), $"umb-ids-{Guid.NewGuid():N}.toml");
            try
            {
                File.WriteAllText(path, "song_order = [\n  \"ui_bgm_a\", # mod\n  \"ui_bgm_b\"\n]\n");

                Assert.Equal(new[] { "ui_bgm_a", "ui_bgm_b" }, SeriesToml.ReadIdList(path));
                Assert.Empty(SeriesToml.ReadIdList(path + ".missing"));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
