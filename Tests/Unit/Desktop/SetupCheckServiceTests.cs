using Tests.Helpers;
using UMB.CLI.Services;
using Xunit;

namespace Tests.Unit.Desktop
{
    public class SetupCheckServiceTests : IDisposable
    {
        private readonly DesktopWorkspace _ws = new();

        public void Dispose() => _ws.Dispose();

        private static readonly string[] GameFiles =
        {
            "ui/param/database/ui_bgm_db.prc",
            "ui/param/database/ui_gametitle_db.prc",
            "ui/param/database/ui_stage_db.prc",
            "ui/message/msg_bgm+us_en.msbt",
            "sound/config/bgm_property.bin"
        };

        private void WriteGameFiles(IEnumerable<string> files)
        {
            foreach (var file in files)
            {
                var path = Path.Combine(_ws.Options.GameResourcesPath, file);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, "");
            }
        }

        [Fact]
        public void CheckResources_ListsEveryFileWhenTheFolderIsMissing()
        {
            var status = SetupCheckService.CheckResources(_ws.Options.GameResourcesPath);

            Assert.False(status.Ok);
            Assert.Equal(GameFiles, status.Missing);
        }

        [Fact]
        public void CheckResources_ListsOnlyTheMissingFiles()
        {
            WriteGameFiles(GameFiles.Skip(1));

            var status = SetupCheckService.CheckResources(_ws.Options.GameResourcesPath);

            Assert.False(status.Ok);
            Assert.Equal(new[] { GameFiles[0] }, status.Missing);
        }

        [Fact]
        public void CheckResources_IsOkWithAllFiles()
        {
            WriteGameFiles(GameFiles);

            var status = SetupCheckService.CheckResources(_ws.Options.GameResourcesPath);

            Assert.True(status.Ok);
            Assert.Empty(status.Missing);
            Assert.Equal(Path.GetFullPath(_ws.Options.GameResourcesPath), status.Path);
        }

        [Fact]
        public void CheckTool_FailsForAMissingTool()
        {
            Assert.Equal(new SetupCheckService.ToolStatus(false, null), SetupCheckService.CheckTool("umb-no-such-tool", "--version"));
        }

        [Fact]
        public void CheckTool_ReportsTheFirstLineOfARunnableTool()
        {
            var status = SetupCheckService.CheckTool("dotnet", "--version");

            Assert.True(status.Ok);
            Assert.Matches(@"^\d+\.\d+\.\d+", status.Version);
        }
    }
}
