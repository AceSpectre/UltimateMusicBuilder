import type { SongPreset, SongPresets, TrackFields, TrackOrderItem, VanillaSongOption } from '$lib/types/electron'

export const RECORD_OPTIONS = ['original', 'arrange', 'new_arrange'] as const

export type PresetValues = Pick<TrackFields, 'game' | 'author' | 'copyright' | 'record_type' | 'in_soundtest'> &
  Pick<SongPreset, 'pinch_song'>

export interface SongOption {
  value: string
  title: string
}

export function recordLabelKey(value: string): string {
  if (value === 'arrange') return 'orderTracks.recordArrange'
  if (value === 'new_arrange') return 'orderTracks.recordNewArrange'
  return 'orderTracks.recordOriginal'
}

export function emptyPreset(fields: Partial<SongPreset> = {}): SongPreset {
  return { name: '', game: '', author: '', copyright: '', record_type: '', in_soundtest: '', pinch_song: '', ...fields }
}

/**
 * The values a preset (null for the default) gives a song from `songGame`. The preset's game
 * wins, then the song's; each other empty field falls back to that game's default, then the series default.
 */
export function resolvePreset(presets: SongPresets, songGame: string, preset: SongPreset | null = null): PresetValues {
  const game = preset?.game || songGame || presets.seriesDefault.game
  const chain = [preset, presets.gameDefaults.find((d) => d.game === game), presets.seriesDefault]
  const pick = (key: keyof SongPreset): string => chain.find((p) => p?.[key])?.[key] ?? ''
  return {
    game,
    author: pick('author'),
    copyright: pick('copyright'),
    record_type: pick('record_type') || 'original',
    in_soundtest: pick('in_soundtest') || 'True',
    pinch_song: pick('pinch_song')
  }
}

/** Songs a pinch link can point at: mod tracks by filename, vanilla songs by info_* id. */
export function songOptions(items: TrackOrderItem[], vanillaSongs: VanillaSongOption[]): SongOption[] {
  return [
    ...items.filter((item) => item.fields && item.filename).map((item) => ({ value: item.filename, title: item.fields?.title || item.filename })),
    ...vanillaSongs.map((song) => ({ value: song.infoId, title: song.name }))
  ]
}

/** The song options some series song already uses as its pinch song. */
export function pinchSongOptions(items: TrackOrderItem[], vanillaSongs: VanillaSongOption[]): SongOption[] {
  const used = new Set(items.map((item) => item.fields?.info1?.trim() ?? '').filter(Boolean))
  return songOptions(items, vanillaSongs).filter((option) => used.has(option.value))
}

/** One row per series game (existing default or a blank one), then defaults for games no longer listed. */
export function gameDefaultRows(games: { id: string }[], gameDefaults: SongPreset[]): SongPreset[] {
  return [
    ...games.map((g) => gameDefaults.find((d) => d.game === g.id) ?? emptyPreset({ game: g.id })),
    ...gameDefaults.filter((d) => !games.some((g) => g.id === d.game))
  ]
}
