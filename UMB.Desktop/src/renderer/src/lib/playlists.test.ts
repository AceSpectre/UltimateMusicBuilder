import { describe, expect, it } from 'vitest'
import type { ManagedPlaylist, ManagedStage } from '$lib/types/electron'
import {
  addSongs,
  excludedTracks,
  playlistIdError,
  playlistIdFromName,
  isChanged,
  playlistDefaults,
  resetToDefault,
  playlistsToSave,
  removeTrack,
  setIncluded,
  setSlotOrder,
  silentStages,
  slotTracks,
  stagesToSave
} from './playlists'

const all = (value: number) => Array.from({ length: 16 }, () => value)

function playlist(overrides: Partial<ManagedPlaylist> = {}): ManagedPlaylist {
  return {
    id: 'bgmmario',
    name: 'Mario',
    kind: 'vanilla',
    series: [],
    defaultTracks: null,
    tracks: [
      { bgmId: 'a', orders: all(0), incidences: all(100) },
      { bgmId: 'b', orders: all(1), incidences: all(100) }
    ],
    ...overrides
  }
}

function stage(overrides: Partial<ManagedStage> = {}): ManagedStage {
  return {
    id: 'ui_stage_x',
    name: 'X',
    hidden: false,
    seriesName: '',
    vanillaPlaylistId: 'bgmmario',
    vanillaOrder: 0,
    playlistId: 'bgmmario',
    order: 0,
    ...overrides
  }
}

const ids = (tracks: { bgmId: string }[]) => tracks.map((t) => t.bgmId)

describe('slots', () => {
  it('lists a slot in play order and leaves out tracks with order -1', () => {
    const p = playlist()
    p.tracks[0].orders[3] = 1
    p.tracks[1].orders[3] = 0
    p.tracks[1].orders[5] = -1

    expect(ids(slotTracks(p, 3))).toEqual(['b', 'a'])
    expect(ids(slotTracks(p, 5))).toEqual(['a'])
    expect(ids(excludedTracks(p, 5))).toEqual(['b'])
  })

  it('reorders one slot only', () => {
    const p = playlist()
    setSlotOrder(p, 2, [p.tracks[1], p.tracks[0]])

    expect(ids(slotTracks(p, 2))).toEqual(['b', 'a'])
    expect(ids(slotTracks(p, 0))).toEqual(['a', 'b'])
  })

  it('adds new songs at the end of every slot, skipping ones already there', () => {
    const p = playlist()
    p.tracks[1].orders[4] = -1

    addSongs(p, ['c', 'a'])

    expect(ids(p.tracks)).toEqual(['a', 'b', 'c'])
    expect(p.tracks[2].orders[0]).toBe(2)
    expect(p.tracks[2].orders[4]).toBe(1)
    expect(p.tracks[2].incidences).toEqual(all(100))
  })

  it('removes a track and closes the gap in every slot', () => {
    const p = playlist()
    removeTrack(p, 'a')

    expect(p.tracks).toEqual([{ bgmId: 'b', orders: all(0), incidences: all(100) }])
  })

  it('leaves a track out of a slot and puts it back at the end', () => {
    const p = playlist()
    setIncluded(p, 7, p.tracks[0], false)
    expect(p.tracks.map((t) => t.orders[7])).toEqual([-1, 0])

    setIncluded(p, 7, p.tracks[0], true)
    expect(ids(slotTracks(p, 7))).toEqual(['b', 'a'])
  })
})

describe('playlist ids', () => {
  it('makes a Sma5h-style id from the name', () => {
    expect(playlistIdFromName('My Mix 2!')).toBe('bgmmymix')
  })

  it('rejects invalid and taken ids', () => {
    expect(playlistIdError('bgmmine', ['bgmmario'])).toBeNull()
    expect(playlistIdError('bgm', [])).toBe('invalid')
    expect(playlistIdError('mine', [])).toBe('invalid')
    expect(playlistIdError('bgmmario', ['bgmmario'])).toBe('taken')
  })
})

describe('saving', () => {
  it('saves custom and series-flagged playlists, and ones differing from their default', () => {
    const untouched = playlist({ id: 'bgmzelda' })
    const changed = playlist()
    const saved = playlist({ id: 'bgmkirby', defaultTracks: [] })
    const custom = playlist({ id: 'bgmmine', kind: 'custom' })
    const flagged = playlist({ id: 'bgm_mix', kind: 'series', series: [{ id: 'dev', name: 'Dev' }] })
    const defaults = playlistDefaults([untouched, changed, saved, custom, flagged])
    changed.tracks[0].incidences[0] = 5

    expect(playlistsToSave([untouched, changed, saved, custom, flagged], defaults).map((p) => p.id)).toEqual([
      'bgmmario',
      'bgmkirby',
      'bgmmine',
      'bgm_mix'
    ])
  })

  it('resets a changed playlist to its default, which is then not saved', () => {
    const saved = playlist({ defaultTracks: [{ bgmId: 'a', orders: all(0), incidences: all(100) }] })
    const defaults = playlistDefaults([saved])
    expect(isChanged(saved, defaults)).toBe(true)

    resetToDefault(saved, defaults)

    expect(ids(saved.tracks)).toEqual(['a'])
    expect(isChanged(saved, defaults)).toBe(false)
    expect(playlistsToSave([saved], defaults)).toEqual([])
  })

  it('never counts a custom playlist as changed', () => {
    const custom = playlist({ kind: 'custom' })
    const defaults = playlistDefaults([custom])
    custom.tracks.pop()

    expect(isChanged(custom, defaults)).toBe(false)
  })

  it('saves only stages that differ from the game', () => {
    const stages = [stage(), stage({ id: 'moved', playlistId: 'bgmmine' }), stage({ id: 'slot', order: 3 })]

    expect(stagesToSave(stages)).toEqual([
      { id: 'moved', playlistId: 'bgmmine', order: 0 },
      { id: 'slot', playlistId: 'bgmmario', order: 3 }
    ])
  })

  it('flags stages whose playlist slot is empty or missing', () => {
    const p = playlist()
    p.tracks.forEach((t) => (t.orders[2] = -1))
    const stages = [stage(), stage({ id: 'empty', order: 2 }), stage({ id: 'gone', playlistId: 'bgmgone' })]

    expect(silentStages(stages, [p]).map((s) => s.id)).toEqual(['empty', 'gone'])
  })
})
