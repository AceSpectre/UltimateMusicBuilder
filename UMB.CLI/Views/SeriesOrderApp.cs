using Avalonia;
using Avalonia.Themes.Fluent;

namespace UMB.CLI.Views
{
    public class SeriesOrderApp : Application
    {
        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
        }
    }
}
