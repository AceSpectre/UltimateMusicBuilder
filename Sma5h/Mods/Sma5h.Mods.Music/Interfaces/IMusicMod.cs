using Sma5h.Mods.Music.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sma5h.Mods.Music.Interfaces
{
    public interface IMusicMod
    {
        string Id { get; }
        string Name { get; }
        string ModPath { get; }

        MusicModInformation Mod { get; }

        MusicModEntries GetMusicModEntries();
        Task<bool> AddOrUpdateMusicModEntries(MusicModEntries musicModEntries);
    }
}
