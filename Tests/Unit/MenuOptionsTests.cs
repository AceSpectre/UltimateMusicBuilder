using Spectre.Console.Testing;
using UMB.CLI;
using Xunit;

namespace Tests.Unit
{
    // Guards against regressions where an action handled by Program.Main's switch
    // is dropped from the interactive Spectre menu (MenuOptions), leaving it
    // reachable only via raw CLI args. Notably "accept-nus3", which commit d2cd439
    // accidentally removed from the menu while keeping the switch case.
    public class MenuOptionsTests
    {
        // Every action the interactive menu is expected to offer. Keep in sync with
        // the switch in Program.Main and Program.MenuOptions.
        private static readonly string[] ExpectedActions =
        {
            "build",
            "scaffold",
            "nus3-convert",
            "accept-nus3",
            "convert",
            "merge",
            "extract-icons",
            "cleanup",
            "order-series",
            "order-tracks",
            "config-volume",
            "dump-stages",
            "quit",
        };

        [Fact]
        public void MenuOptions_ContainsAllExpectedActions()
        {
            var actual = Program.MenuOptions.Values;
            foreach (var action in ExpectedActions)
                Assert.Contains(action, actual);
        }

        [Fact]
        public void MenuOptions_HasNoUnexpectedActions()
        {
            foreach (var action in Program.MenuOptions.Values)
                Assert.Contains(action, ExpectedActions);
        }

        [Fact]
        public void ShowMenu_RendersEveryMenuOptionLabel()
        {
            var console = new TestConsole()
                .Interactive();
            console.Profile.Width = 200;
            // SelectionPrompt only renders its visible page (PageSize), so scroll
            // Down once per option to force every item to render into a frame.
            // With WrapAround, N downs on N options lands back on the first item.
            for (int i = 0; i < Program.MenuOptions.Count; i++)
                console.Input.PushKey(System.ConsoleKey.DownArrow);
            console.Input.PushKey(System.ConsoleKey.Enter);

            var selected = Program.ShowMenu(console);

            // N downs wrap back to the first entry ("build"), then Enter selects it.
            Assert.Equal("build", selected);

            // Every menu label must have been rendered to the console.
            var output = console.Output;
            foreach (var label in Program.MenuOptions.Keys)
            {
                // Assert on the leading token (text before the description dash) to
                // stay robust against any width-based truncation of the long suffix.
                var lead = label.Split('-')[0].TrimEnd();
                Assert.Contains(lead, output);
            }
        }
    }
}
