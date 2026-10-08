using Sma5h.Mods.Music.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sma5h.Mods.Music.Interfaces
{
    public interface IMusicModManagerService
    {
        IEnumerable<IMusicMod> MusicMods { get; }
        IEnumerable<IMusicMod> RefreshMusicMods();
        Task<bool> UpdateSeriesEntry(SeriesEntry seriesEntry);
    }
}
