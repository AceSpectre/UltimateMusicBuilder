import { describe, expect, it } from 'vitest'
import type { TrackFields, TrackOrderItem } from '$lib/types/electron'
import {
  MODDED_HUE,
  OVERLAY_MODES,
  VANILLA_HUE,
  gameHue,
  nextOverlayMode,
  overlayStyle,
  seriesGameIds,
  tintStyle,
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
  fields: f,
  isPinchTarget: false
})

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

describe('gameHue', () => {
  it('gives distinct hues within the colour wheel', () => {
    const hues = [0, 1, 2, 3, 4, 5].map(gameHue)
    expect(new Set(hues).size).toBe(hues.length)
    expect(hues.every((h) => h >= 0 && h < 360)).toBe(true)
  })
})

describe('overlayStyle', () => {
  it('is empty when the overlay is off', () => {
    expect(overlayStyle('off', item(fields()), ['g1'])).toBe('')
  })

  it('tints songs by their game and leaves unknown games or vanilla rows plain', () => {
    expect(overlayStyle('game', item(fields({ game: 'g2' })), ['g1', 'g2'])).toBe(tintStyle(gameHue(1)))
    expect(overlayStyle('game', item(fields({ game: 'zz' })), ['g1'])).toBe('')
    expect(overlayStyle('game', item(null), ['g1'])).toBe('')
  })

  it('tints modded and vanilla rows differently', () => {
    expect(overlayStyle('source', item(fields()), [])).toBe(tintStyle(MODDED_HUE))
    expect(overlayStyle('source', item(null), [])).toBe(tintStyle(VANILLA_HUE))
  })

  it('greys out only songs hidden from the sound test', () => {
    expect(overlayStyle('soundtest', item(fields({ in_soundtest: 'False' })), [])).toContain('opacity')
    expect(overlayStyle('soundtest', item(fields({ in_soundtest: 'TRUE' })), [])).toBe('')
    expect(overlayStyle('soundtest', item(null), [])).toBe('')
  })
})
