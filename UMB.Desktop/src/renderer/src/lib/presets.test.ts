import { describe, expect, it } from 'vitest'
import type { SongPresets, TrackOrderItem } from '$lib/types/electron'
import { emptyPreset, gameDefaultRows, pinchSongOptions, resolvePreset } from './presets'

const presets = (over: Partial<SongPresets> = {}): SongPresets => ({
  seriesDefault: emptyPreset({ game: 'g1', author: 'Series Author', copyright: 'Series Co', record_type: 'original', in_soundtest: 'True' }),
  gameDefaults: [emptyPreset({ game: 'g2', author: 'G2 Author', record_type: 'arrange', pinch_song: 'g2_pinch.nus3audio' })],
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
      in_soundtest: 'True',
      pinch_song: 'g2_pinch.nus3audio'
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
      in_soundtest: 'False',
      pinch_song: 'g2_pinch.nus3audio'
    })
  })

  it('falls back to original and in sound test when nothing sets them', () => {
    const bare = presets({ seriesDefault: emptyPreset(), gameDefaults: [] })

    expect(resolvePreset(bare, 'g1')).toMatchObject({ record_type: 'original', in_soundtest: 'True' })
  })
})

describe('gameDefaultRows', () => {
  it('gives every game a row and keeps defaults for unlisted games last', () => {
    const orphan = emptyPreset({ game: 'gone', author: 'x' })
    const rows = gameDefaultRows([{ id: 'g1' }, { id: 'g2' }], [orphan, presets().gameDefaults[0]])

    expect(rows.map((r) => [r.game, r.author])).toEqual([['g1', ''], ['g2', 'G2 Author'], ['gone', 'x']])
  })
})

describe('pinchSongOptions', () => {
  const song = (filename: string, title: string, info1 = ''): TrackOrderItem => ({
    id: filename,
    title,
    subtitle: '',
    bgmId: '',
    filename,
    isLocked: false,
    originalIndex: 0,
    fields: { title, game: 'g1', author: '', copyright: '', record_type: 'original', special_category: '', info1, in_soundtest: 'True' }
  })

  it('lists only the mod and vanilla songs some song already uses as its pinch song', () => {
    const items = [song('a.nus3audio', 'A', 'a_pinch.nus3audio'), song('a_pinch.nus3audio', 'A Pinch'), song('b.nus3audio', 'B', 'info_v2')]
    const vanilla = [{ infoId: 'info_v1', name: 'V1' }, { infoId: 'info_v2', name: 'V2' }]

    expect(pinchSongOptions(items, vanilla)).toEqual([
      { value: 'a_pinch.nus3audio', title: 'A Pinch' },
      { value: 'info_v2', title: 'V2' }
    ])
  })
})
