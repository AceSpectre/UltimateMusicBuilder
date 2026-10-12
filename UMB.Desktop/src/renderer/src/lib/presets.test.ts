import { describe, expect, it } from 'vitest'
import type { SongPresets, TrackFields } from '$lib/types/electron'
import { applyChoice, emptyPreset, missingDefaults, presetChoices, resolvePreset } from './presets'

const presets = (over: Partial<SongPresets> = {}): SongPresets => ({
  seriesDefault: emptyPreset({ game: 'g1', author: 'Series Author', copyright: 'Series Co', record_type: 'original', in_soundtest: 'True' }),
  gameDefaults: [emptyPreset({ game: 'g2', author: 'G2 Author', record_type: 'arrange' })],
  presets: [],
  ...over
})

describe('resolvePreset', () => {
  it('keeps the song game and fills from its game default, then the series default', () => {
    expect(resolvePreset(presets(), 'g2')).toEqual({
      game: 'g2',
      author: 'G2 Author',
      copyright: 'Series Co',
      record_type: 'arrange',
      in_soundtest: 'True'
    })
  })

  it('uses the series default for a game without its own default', () => {
    expect(resolvePreset(presets(), 'g3')).toMatchObject({ game: 'g3', author: 'Series Author', record_type: 'original' })
  })

  it('falls back to the series default game when the song has none', () => {
    expect(resolvePreset(presets(), '').game).toBe('g1')
  })

  it('lets a named preset override its game and fields, inheriting the rest', () => {
    const remix = emptyPreset({ name: 'Remix', game: 'g2', record_type: 'new_arrange', in_soundtest: 'False' })

    expect(resolvePreset(presets(), 'g1', remix)).toEqual({
      game: 'g2',
      author: 'G2 Author',
      copyright: 'Series Co',
      record_type: 'new_arrange',
      in_soundtest: 'False'
    })
  })

  it('falls back to original and in sound test when nothing sets them', () => {
    const bare = presets({ seriesDefault: emptyPreset(), gameDefaults: [] })

    expect(resolvePreset(bare, 'g1')).toMatchObject({ record_type: 'original', in_soundtest: 'True' })
  })
})

describe('presetChoices', () => {
  const games = [{ id: 'g1', name: 'Game One' }, { id: 'g2', name: 'Game Two' }, { id: 'g3', name: 'Game Three' }]

  it('orders series default, the song game, named presets, then the other games', () => {
    const choices = presetChoices(presets({ presets: [emptyPreset({ name: 'Remix' })] }), games, 'g2')

    expect(choices.map((c) => [c.kind, c.label])).toEqual([
      ['series', ''],
      ['game', 'Game Two'],
      ['preset', 'Remix'],
      ['game', 'Game One'],
      ['game', 'Game Three']
    ])
    expect(choices[1].preset.author).toBe('G2 Author')
    expect(choices[3].preset).toEqual(emptyPreset({ game: 'g1' }))
  })
})

describe('applyChoice', () => {
  it('applies the series default without the song game default', () => {
    const [series] = presetChoices(presets(), [], 'g2')

    expect(applyChoice(presets(), 'g2', series)).toMatchObject({ game: 'g1', author: 'Series Author', record_type: 'original' })
  })

  it('applies another game default, switching the song to that game', () => {
    const choice = presetChoices(presets(), [{ id: 'g2', name: 'Game Two' }], 'g1')[1]

    expect(applyChoice(presets(), 'g1', choice)).toMatchObject({ game: 'g2', author: 'G2 Author', record_type: 'arrange' })
  })
})

describe('missingDefaults', () => {
  const fields = (over: Partial<TrackFields> = {}): TrackFields => ({
    title: 't', game: 'g2', author: '', copyright: '', record_type: 'original', special_category: '', info1: '', in_soundtest: 'True', ...over
  })

  it('fills only empty fields from the song game default, then the series default', () => {
    expect(missingDefaults(presets(), fields({ copyright: 'Mine' }))).toEqual({ author: 'G2 Author' })
  })

  it('gives a song without a game the series default game', () => {
    expect(missingDefaults(presets(), fields({ game: '', author: 'x', copyright: 'y' }))).toEqual({ game: 'g1' })
  })
})
