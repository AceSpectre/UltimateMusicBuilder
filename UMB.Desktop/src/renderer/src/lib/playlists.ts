import type { ManagedPlaylist, ManagedStage, PlaylistTrack, SavedPlaylist, StageAssignmentInput } from '$lib/types/electron'

// Each stage plays one order slot of its playlist; a track's order -1 leaves it out of that slot.
export const ORDER_COUNT = 16
export const ORDER_SLOTS = Array.from({ length: ORDER_COUNT }, (_, slot) => slot)
export const DEFAULT_INCIDENCE = 100

/** The tracks playing in a slot, in play order. */
export function slotTracks(playlist: ManagedPlaylist, slot: number): PlaylistTrack[] {
  return playlist.tracks.filter((t) => t.orders[slot] >= 0).sort((a, b) => a.orders[slot] - b.orders[slot])
}

/** The tracks left out of a slot. */
export function excludedTracks(playlist: ManagedPlaylist, slot: number): PlaylistTrack[] {
  return playlist.tracks.filter((t) => t.orders[slot] < 0)
}

/** Sets a slot's play order to the given track order (0, 1, 2, ...). */
export function setSlotOrder(playlist: ManagedPlaylist, slot: number, ordered: PlaylistTrack[]): void {
  ordered.forEach((track, i) => (track.orders[slot] = i))
}

function renumber(playlist: ManagedPlaylist, slot: number): void {
  setSlotOrder(playlist, slot, slotTracks(playlist, slot))
}

/** An incidence (random-roll weight) as a whole number in 0-65535. */
export function clampIncidence(value: unknown): number {
  return Math.max(0, Math.min(65535, Math.trunc(Number(value)) || 0))
}

/** Appends a song to every slot (as Sma5h did), with the default incidence. */
export function addSong(playlist: ManagedPlaylist, bgmId: string): void {
  if (playlist.tracks.some((t) => t.bgmId === bgmId)) return
  playlist.tracks.push({
    bgmId,
    orders: ORDER_SLOTS.map((slot) => slotTracks(playlist, slot).length),
    incidences: ORDER_SLOTS.map(() => DEFAULT_INCIDENCE)
  })
}

export function removeTrack(playlist: ManagedPlaylist, bgmId: string): void {
  playlist.tracks = playlist.tracks.filter((t) => t.bgmId !== bgmId)
  ORDER_SLOTS.forEach((slot) => renumber(playlist, slot))
}

/** Puts a track in a slot (at the end) or leaves it out. */
export function setIncluded(playlist: ManagedPlaylist, slot: number, track: PlaylistTrack, included: boolean): void {
  track.orders[slot] = included ? slotTracks(playlist, slot).length : -1
  renumber(playlist, slot)
}

/** Playlist ids follow Sma5h: "bgm" plus lowercase letters, made from the name. */
export function playlistIdFromName(name: string): string {
  return 'bgm' + name.replace(/[^a-zA-Z]/g, '').toLowerCase()
}

export function playlistIdError(id: string, existingIds: string[]): 'invalid' | 'taken' | null {
  if (!/^bgm[a-z]+$/.test(id)) return 'invalid'
  return existingIds.includes(id) ? 'taken' : null
}

function tracksKey(tracks: PlaylistTrack[]): string {
  return JSON.stringify(tracks)
}

/** Each playlist's default tracks (its tracks when unchanged), kept as text to compare and reset to. */
export function playlistDefaults(playlists: ManagedPlaylist[]): Record<string, string> {
  return Object.fromEntries(playlists.map((p) => [p.id, tracksKey(p.defaultTracks ?? p.tracks)]))
}

/** A game or series playlist differing from its default. */
export function isChanged(playlist: ManagedPlaylist, defaults: Record<string, string>): boolean {
  return playlist.kind !== 'custom' && tracksKey(playlist.tracks) !== defaults[playlist.id]
}

export function resetToDefault(playlist: ManagedPlaylist, defaults: Record<string, string>): void {
  playlist.tracks = JSON.parse(defaults[playlist.id])
}

/**
 * What playlists.toml holds: custom playlists, game or series playlists differing from their default, and series-flagged
 * ones. An unchanged series-flagged playlist keeps only its flag, so it follows the game and its series.
 */
export function playlistsToSave(playlists: ManagedPlaylist[], defaults: Record<string, string>): SavedPlaylist[] {
  return playlists
    .filter((p) => p.kind === 'custom' || p.series.length > 0 || isChanged(p, defaults))
    .map((p) => (p.kind !== 'custom' && !isChanged(p, defaults) ? { ...p, tracks: null } : p))
}

/** Stages playing a different playlist or slot than in the game. */
export function stagesToSave(stages: ManagedStage[]): StageAssignmentInput[] {
  return stages
    .filter((s) => s.playlistId !== s.vanillaPlaylistId || s.order !== s.vanillaOrder)
    .map((s) => ({ id: s.id, playlistId: s.playlistId, order: s.order }))
}

/** Stages whose playlist slot has no songs, which would leave the stage silent. */
export function silentStages(stages: ManagedStage[], playlists: ManagedPlaylist[]): ManagedStage[] {
  const byId = new Map(playlists.map((p) => [p.id, p]))
  return stages.filter((s) => {
    const playlist = byId.get(s.playlistId)
    return !playlist || slotTracks(playlist, s.order).length === 0
  })
}
