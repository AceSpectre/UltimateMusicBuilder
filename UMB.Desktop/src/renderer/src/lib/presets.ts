import type { SeriesGame, SongPreset, SongPresets, TrackFields } from '$lib/types/electron'

export const RECORD_OPTIONS = ['original', 'arrange', 'new_arrange'] as const

export type PresetValues = Pick<TrackFields, 'game' | 'author' | 'copyright' | 'record_type' | 'in_soundtest'>

/** An Apply Preset menu entry; `label` is the game or preset name. */
export interface PresetChoice {
  kind: 'series' | 'game' | 'preset'
  label: string
  preset: SongPreset
}

export function emptyPreset(fields: Partial<SongPreset> = {}): SongPreset {
  return { name: '', game: '', author: '', copyright: '', record_type: '', in_soundtest: '', ...fields }
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
    in_soundtest: pick('in_soundtest') || 'True'
  }
}

/** Defaults for the fields a song leaves empty: its game default, then the series default. */
export function missingDefaults(presets: SongPresets, fields: TrackFields): Partial<PresetValues> {
  const defaults = resolvePreset(presets, fields.game)
  const keys = (Object.keys(defaults) as (keyof PresetValues)[]).filter((key) => defaults[key] && !fields[key])
  return Object.fromEntries(keys.map((key) => [key, defaults[key]]))
}

/** Apply Preset menu order: series default, the song's game, named presets, then the other games. */
export function presetChoices(presets: SongPresets, games: SeriesGame[], songGame: string): PresetChoice[] {
  const gameChoice = (game: SeriesGame): PresetChoice => ({
    kind: 'game',
    label: game.name,
    preset: presets.gameDefaults.find((d) => d.game === game.id) ?? emptyPreset({ game: game.id })
  })
  return [
    { kind: 'series', label: '', preset: presets.seriesDefault },
    ...games.filter((g) => g.id === songGame).map(gameChoice),
    ...presets.presets.map((preset): PresetChoice => ({ kind: 'preset', label: preset.name, preset })),
    ...games.filter((g) => g.id !== songGame).map(gameChoice)
  ]
}

/** The values a menu choice gives a song; the series default skips game defaults. */
export function applyChoice(presets: SongPresets, songGame: string, choice: PresetChoice): PresetValues {
  return resolvePreset(choice.kind === 'series' ? { ...presets, gameDefaults: [] } : presets, songGame, choice.preset)
}
