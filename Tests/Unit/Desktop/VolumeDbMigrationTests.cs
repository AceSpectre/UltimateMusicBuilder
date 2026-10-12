using System.Text.Json.Nodes;
using Tests.Helpers;
using UMB.CLI.Services;
using Xunit;

namespace Tests.Unit.Desktop
{
    public class VolumeDbMigrationTests : IDisposable
    {
        private readonly DesktopWorkspace _ws = new();

        public void Dispose() => _ws.Dispose();

        [Fact]
        public void MigrateSettings_ConvertsLegacyMultipliersToDb()
        {
            var path = DesktopWorkspace.WriteFile(_ws.Root, "appsettings.json",
                "{ \"LogPath\": \"Log\", \"Sma5hMusic\": { \"GlobalVolumeMultiplier\": 2.0, \"LufsNormalization\": { \"MaxGainMultiplier\": 4.0 } } }");

            VolumeDbMigration.MigrateSettings(path);

            var music = JsonNode.Parse(File.ReadAllText(path))!["Sma5hMusic"]!.AsObject();
            Assert.Equal(6.02, music["GlobalVolumeDb"]!.GetValue<double>());
            Assert.Equal(12.04, music["LufsNormalization"]!["MaxGainDb"]!.GetValue<double>());
            Assert.False(music.ContainsKey("GlobalVolumeMultiplier"));
            Assert.Equal("Log", JsonNode.Parse(File.ReadAllText(path))!["LogPath"]!.GetValue<string>());
        }

        [Fact]
        public void MigrateSettings_LeavesDbSettingsUntouched()
        {
            const string json = "{ \"Sma5hMusic\": { \"GlobalVolumeDb\": 1.5 } }";
            var path = DesktopWorkspace.WriteFile(_ws.Root, "appsettings.json", json);

            VolumeDbMigration.MigrateSettings(path);
            Assert.Equal(json, File.ReadAllText(path));
        }

        [Fact]
        public void MigrateMods_ConvertsSeriesTomlAndTracksCsv()
        {
            var series = _ws.WriteSeries("persona", "persona",
                "filename,title,volume,in_soundtest\na.flac,A,0.5,True\nb.flac,B,,True\nc.flac,C,1,True\n",
                "[series]\nid = \"persona\"\n\n[default-track-data]\ngame = \"p\"\nvolume = 2.0\n");

            VolumeDbMigration.MigrateMods(_ws.ModsRoot);

            var toml = File.ReadAllText(Path.Combine(series, "series.toml"));
            Assert.Contains("volume-db = 6.02", toml);
            Assert.DoesNotContain("volume = ", toml);
            Assert.Equal("filename,title,volume_db,in_soundtest\na.flac,A,-6.02,True\nb.flac,B,,True\nc.flac,C,0.0,True\n",
                File.ReadAllText(Path.Combine(series, "tracks.csv")).Replace("\r\n", "\n"));
        }

        [Fact]
        public void MigrateMods_IsANoOpOnceMigrated()
        {
            const string csv = "filename,title,volume_db\na.flac,A,-6\n";
            const string toml = "[series]\nid = \"persona\"\n\n[default-track-data]\nvolume-db = 3.0\n";
            var series = _ws.WriteSeries("persona", "persona", csv, toml);

            VolumeDbMigration.MigrateMods(_ws.ModsRoot);

            Assert.Equal(csv, File.ReadAllText(Path.Combine(series, "tracks.csv")));
            Assert.Equal(toml, File.ReadAllText(Path.Combine(series, "series.toml")));
        }
    }
}
