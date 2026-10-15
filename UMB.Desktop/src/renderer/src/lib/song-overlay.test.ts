import { describe, expect, it } from 'vitest'
import type { TrackFields, TrackOrderItem } from '$lib/types/electron'
import { overlayLegend, overlayStyle, seriesGameIds } from './song-overlay'

const fields = (over: Partial<TrackFields> = {}): TrackFields => ({
  title: 't',
  game: 'g1',
  author: '',
  copyright: '',
  record_type: 'original',
  special_category: '',
  info1: '',
  in_soundtest: 'True',
  ...over
})

const item = (f: TrackFields | null): TrackOrderItem => ({
  id: 'x',
  title: 't',
  subtitle: '',
  bgmId: '',
  filename: 'f',
  isLocked: f === null,
  originalIndex: 0,
  fields: f,
  isVanilla: false,
  missing: false
})

describe('seriesGameIds', () => {
  it('lists declared games first then unknown song games, deduplicated', () => {
    const ids = seriesGameIds(
      [{ id: 'g1', name: 'One' }, { id: 'g2', name: 'Two' }],
      [item(fields({ game: 'g3' })), item(fields({ game: 'g1' })), item(null), item(fields({ game: '' }))]
    )
    expect(ids).toEqual(['g1', 'g2', 'g3'])
  })
})

describe('overlayStyle', () => {
  const [g1, g2] = overlayLegend(['g1', 'g2'], (id) => id).map((entry) => entry.style)

  it('tints songs with their game legend colour', () => {
    expect(g1).not.toBe(g2)
    expect(overlayStyle(item(fields({ game: 'g2', in_soundtest: 'TRUE' })), ['g1', 'g2'])).toBe(g2)
  })

  it('dims the game colour for songs hidden from the sound test', () => {
    const hidden = overlayStyle(item(fields({ in_soundtest: 'False' })), ['g1'])
    expect(hidden).not.toBe('')
    expect(hidden).not.toBe(g1)
  })

  it('leaves unknown games and vanilla rows plain', () => {
    expect(overlayStyle(item(fields({ game: 'zz' })), ['g1'])).toBe('')
    expect(overlayStyle(item(null), ['g1'])).toBe('')
  })
})

describe('overlayLegend', () => {
  it('labels each game by name', () => {
    expect(overlayLegend(['g1'], (id) => `name:${id}`).map((entry) => entry.label)).toEqual(['name:g1'])
  })
})
