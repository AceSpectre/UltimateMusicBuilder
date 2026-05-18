Resources/Game
==============

This directory is intentionally empty in distributed UMB releases.

Populate it with extracted vanilla Smash Ultimate game data before running
a build. UMB reads vanilla audio/UI/parameter data from here to derive
defaults and to keep your mod compatible with the base game.

Expected layout (after extraction):

  Resources/Game/
    sound/    (e.g. config/bgm_property.bin)
    stream/   (e.g. sound/bgm/bgm_*.nus3audio)
    ui/       (e.g. param/sound/ui_bgm_db.prc, message/*.msbt)

You provide this content yourself by extracting an unmodified copy of
Smash Ultimate's ARC. UMB does not ship Nintendo data.
