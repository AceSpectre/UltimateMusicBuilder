using Avalonia;
using Avalonia.Themes.Fluent;

namespace Sma5h.CLI.Views
{
    public class SeriesOrderApp : Application
    {
        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
        }
    }
}
