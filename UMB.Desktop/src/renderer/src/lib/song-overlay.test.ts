import { describe, expect, it } from 'vitest'
import type { TrackFields, TrackOrderItem } from '$lib/types/electron'
import {
  OVERLAY_MODES,
  nextOverlayMode,
  overlayLegend,
  overlayStyle,
  seriesGameIds,
  type OverlayMode
} from './song-overlay'

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
  fields: f
})

const legendStyles = (mode: OverlayMode, gameIds: string[] = []) =>
  overlayLegend(mode, gameIds, (id) => id, (key) => key).map((entry) => entry.style)

describe('nextOverlayMode', () => {
  it('cycles through every mode and wraps back to off', () => {
    let mode: OverlayMode = 'off'
    const seen = OVERLAY_MODES.map(() => (mode = nextOverlayMode(mode)))
    expect(seen).toEqual([...OVERLAY_MODES.slice(1), 'off'])
  })
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
  it('is empty when the overlay is off', () => {
    expect(overlayStyle('off', item(fields()), ['g1'])).toBe('')
    expect(legendStyles('off')).toEqual([])
  })

  it('tints songs with their game legend colour and leaves unknown games or vanilla rows plain', () => {
    const [g1, g2] = legendStyles('game', ['g1', 'g2'])
    expect(g1).not.toBe(g2)
    expect(overlayStyle('game', item(fields({ game: 'g2' })), ['g1', 'g2'])).toBe(g2)
    expect(overlayStyle('game', item(fields({ game: 'zz' })), ['g1'])).toBe('')
    expect(overlayStyle('game', item(null), ['g1'])).toBe('')
  })

  it('tints modded and vanilla rows with their legend colours', () => {
    expect([overlayStyle('source', item(fields()), []), overlayStyle('source', item(null), [])]).toEqual(
      legendStyles('source')
    )
  })

  it('greys out only songs hidden from the sound test, matching the legend', () => {
    expect(overlayStyle('soundtest', item(fields({ in_soundtest: 'False' })), [])).toBe(legendStyles('soundtest')[0])
    expect(overlayStyle('soundtest', item(fields({ in_soundtest: 'TRUE' })), [])).toBe('')
    expect(overlayStyle('soundtest', item(null), [])).toBe('')
  })
})

describe('overlayLegend', () => {
  it('labels games by name and fixed entries by translation key', () => {
    const labels = (mode: OverlayMode) =>
      overlayLegend(mode, ['g1'], (id) => `name:${id}`, (key) => `t:${key}`).map((entry) => entry.label)
    expect(labels('game')).toEqual(['name:g1'])
    expect(labels('source')).toEqual(['t:orderTracks.overlayModded', 't:orderTracks.vanillaTag'])
    expect(labels('soundtest')).toEqual(['t:orderTracks.overlayHidden'])
  })
})
