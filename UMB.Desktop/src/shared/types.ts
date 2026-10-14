// Single source of truth for the domain types shared by the main process,
// the preload bridge and the renderer (via lib/types/electron.d.ts).

export interface LogLine {
  timestamp: string
  level: 'info' | 'warn' | 'error'
  message: string
}

export interface ModInfo {
  name: string
  path: string
}

export interface ModSeriesInfo {
  name: string
  path: string
}

export interface ModStats {
  seriesCount: number
  trackCount: number
}

export interface TrackFields {
  title: string
  game: string
  author: string
  copyright: string
  record_type: string
  special_category: string
  info1: string
  in_soundtest: string
}

export interface TrackOrderItem {
  id: string
  title: string
  subtitle: string
  bgmId: string
  filename: string
  isLocked: boolean
  originalIndex: number | null
  fields: TrackFields | null
  /** A vanilla song: a tracks.csv row named by its ui_bgm id, or a locked song_order.toml entry. */
  isVanilla: boolean
}

export interface SeriesGame {
  id: string
  name: string
}

export interface SaveTrackItem {
  id: string
  fields: TrackFields | null
}

export interface VanillaSongOption {
  infoId: string
  name: string
}

// Song values a preset fills in; '' inherits (game default, then series default).
export interface SongPreset {
  name: string
  game: string
  author: string
  copyright: string
  record_type: string
  in_soundtest: string
}

// series.toml presets: [default-track-data], [[game-defaults]] and [[presets]].
export interface SongPresets {
  seriesDefault: SongPreset
  gameDefaults: SongPreset[]
  presets: SongPreset[]
}

export interface TrackOrderData {
  seriesName: string
  seriesPath: string
  isExistingSeries: boolean
  hasSongOrder: boolean
  games: SeriesGame[]
  vanillaSongs: VanillaSongOption[]
  presets: SongPresets
  items: TrackOrderItem[]
}

// Editable series.toml fields (series id stays read-only — it is the identity). Covers the
// [series] table and the [[games]] array.
export interface SeriesFields {
  name: string
  seriesPlaylist: string
  playlistIncidence: number
  games: SeriesGame[]
}

export interface SeriesOrderItem {
  id: string
  name: string
  seriesId: string
  seriesPath: string
  /** Read-only series dB boost; edited in Config Volume. */
  volumeDb: number
  iconDataUrl: string | null
  originalIndex: number
  fields: SeriesFields
  isVanilla: boolean
  /** Vanilla games of a vanilla series; the build keeps their names. */
  lockedGameIds: string[]
  /** A vanilla series' playlist in the game ('' for a custom series). */
  vanillaPlaylist: string
}

export interface SaveSeriesItem {
  id: string
  fields: SeriesFields | null
}

// Payload for creating a brand-new custom series folder.
export interface CreateSeriesInput {
  seriesId: string
  name: string
  seriesPlaylist: string
  games: SeriesGame[]
  iconDataUrl?: string | null
}

export interface SeriesOrderData {
  modName: string
  modPath: string
  hasSeriesOrder: boolean
  /** Custom series, in display order. */
  items: SeriesOrderItem[]
  /** Every vanilla series; seriesPath is '' until it has a folder in the mod. */
  vanillaItems: SeriesOrderItem[]
}

export interface LoopCandidate {
  rank: number
  score: number
  loopStart: number
  loopEnd: number
  loopLength: number
  loopStartStr: string
  loopEndStr: string
  loopLengthStr: string
  beatAligned: boolean
  bars: number | null
  tempo: number
  key: string
  noteDistance: number
  spectralSim: number
  rmsDelta: number
  seam: 'smooth' | 'good' | 'audible' | 'click'
  note: string
}

export interface Nus3SourceTrack {
  id: string
  name: string
  src: string
  duration: string
  durationSeconds: number
  /** true when a matching .nus3audio already exists in songs-to-validate */
  converted: boolean
}

export interface Nus3TrackDecision {
  trackId: string
  mode: 'loop' | 'end-to-end'
  candidate?: LoopCandidate
  status: 'accepted' | 'rejected' | 'skipped' | 'pending'
}

/** Persisted per-track conversion metadata, keyed by source filename (track id). */
export interface Nus3ConversionMeta {
  mode: 'loop' | 'end-to-end'
  candidate?: LoopCandidate
}

export interface Nus3AnalysisResult {
  track: Nus3SourceTrack
  candidates: LoopCandidate[]
}

/** pymusiclooper tuning passed from the convert view's settings panel. */
export interface LoopAnalysisOptions {
  /** --min-loop-duration (seconds). Overrides the duration multiplier when > 0. */
  minLoopDuration?: number
  /** --min-duration-multiplier (0<x<1). pymusiclooper default is 0.35. */
  minDurationMultiplier?: number
  /** --disable-pruning: keep loop points the initial pass would discard. */
  disablePruning?: boolean
  /** Bypass the analysis cache and re-run pymusiclooper. */
  force?: boolean
}

/** One track row mirroring the Avalonia VolumeRowViewModel. */
export interface VolumeRowItem {
  originalIndex: number
  title: string
  filename: string
  hasMeasurement: boolean
  measuredLufs: number
  autoGainDb: number
  wasClamped: boolean
  trackDb: number
  /** nus3bank volume (dB) of the track with no boosts. */
  bankBaseDb: number
  /** A vanilla song: no auto-gain. Only listed with applyVolumeToVanilla. */
  isVanilla: boolean
}

export interface VolumeConfigData {
  seriesName: string
  seriesPath: string
  globalDb: number
  seriesDb: number
  targetLufs: number
  maxGainDb: number
  ffmpegAvailable: boolean
  lufsCacheExists: boolean
  items: VolumeRowItem[]
}

export interface VolumeOverride {
  originalIndex: number
  db: number
}

export interface VolumeProgress {
  completed: number
  total: number
  currentFile: string
}

export interface ExtractIconMatch {
  seriesId: string
  bntxPath: string
  hasExistingIcon: boolean
}

export interface ExtractIconsAnalysis {
  compiledModPath: string
  modPath: string
  modName: string
  matched: ExtractIconMatch[]
  unmatched: string[]
}

export interface ExtractIconsResult {
  extracted: number
  skipped: number
  failed: number
}

export interface AppSettings {
  globalVolumeDb: number
  /** Experimental: apply the global and series dB boosts to vanilla songs. */
  applyVolumeToVanilla: boolean
  /** Every mod song joins the Smash Battle playlist (bgmsmashbtl), at smashBattleIncidence. */
  addModSongsToSmashBattle: boolean
  smashBattleIncidence: number
  /** Album selection (My Music) on every stage. */
  albumSelectionOnAllStages: boolean
}

export interface MergeSeriesSource {
  modName: string
  modPath: string
  seriesPath: string
}

export interface MergeConflict {
  seriesName: string
  mods: string[]
}

export interface MergeAnalysis {
  modNames: string[]
  modPaths: string[]
  series: { name: string; sources: MergeSeriesSource[] }[]
  conflicts: MergeConflict[]
  totalSeries: number
}

export interface MergeResult {
  outputPath: string
  outputName: string
  totalSeries: number
  totalTracks: number
  conflictsResolved: number
}

// A song that can go in a playlist (vanilla or from the mod).
export interface PlaylistSong {
  bgmId: string
  title: string
  seriesName: string
  isMod: boolean
}

// A playlist song's order and incidence per stage order slot (0-15); order -1 leaves it out of that slot.
// autoAdded: a mod song the build always adds to this playlist, so it can only be left out of orders.
export interface PlaylistTrack {
  bgmId: string
  orders: number[]
  incidences: number[]
  autoAdded?: boolean
}

export type PlaylistKind = 'vanilla' | 'series' | 'custom'

export interface SeriesRef {
  id: string
  name: string
}

// series: the series playlists.toml flags this playlist with; it always holds all their songs.
// defaultTracks: a changed game or series playlist's tracks without playlists.toml, else null.
export interface ManagedPlaylist {
  id: string
  name: string
  kind: PlaylistKind
  series: SeriesRef[]
  tracks: PlaylistTrack[]
  defaultTracks: PlaylistTrack[] | null
}

// A playlist to save; null tracks keeps only its name and series flag (the build fills in the rest).
export type SavedPlaylist = Omit<ManagedPlaylist, 'tracks'> & { tracks: PlaylistTrack[] | null }

export interface ManagedStage {
  id: string
  name: string
  hidden: boolean
  seriesName: string
  vanillaPlaylistId: string
  vanillaOrder: number
  playlistId: string
  order: number
}

export interface ManagePlaylistsData {
  modName: string
  modPath: string
  playlists: ManagedPlaylist[]
  stages: ManagedStage[]
  songs: PlaylistSong[]
  // series.toml still has legacy playlists (series-playlist, [[playlists]] blocks), which saving moves into playlists.toml.
  legacyBlocks: boolean
}

export interface StageAssignmentInput {
  id: string
  playlistId: string
  order: number
}

// Track flagged by the pre-build volume check.
export interface SuspiciousVolumeTrack {
  modName: string
  seriesName: string
  filename: string
  title: string
  effectiveDb: number
}

export interface DebugPingResult {
  ok: boolean
  workspace: string
}

export interface WindowActionResult {
  ok: boolean
  action: 'minimize' | 'fullscreen' | 'close'
  fullScreen?: boolean
}
