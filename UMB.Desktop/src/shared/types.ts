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
  isPinchTarget: boolean
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

export interface DefaultTrackData {
  game: string
  author: string
  copyright: string
  record_type: string
}

export interface TrackOrderData {
  seriesName: string
  seriesPath: string
  isExistingSeries: boolean
  hasSongOrder: boolean
  games: SeriesGame[]
  vanillaSongs: VanillaSongOption[]
  defaultTrackData: DefaultTrackData | null
  items: TrackOrderItem[]
}

// Editable series.toml fields (series id stays read-only — it is the identity). Covers the
// [series] table, the [[games]] array, and the [default-track-data] table (song defaults).
export interface SeriesFields {
  name: string
  seriesPlaylist: string
  playlistIncidence: number
  games: SeriesGame[]
  defaultGame: string
  defaultAuthor: string
  defaultCopyright: string
  defaultRecordType: string
  defaultVolume: number
}

export interface SeriesOrderItem {
  id: string
  name: string
  seriesId: string
  iconDataUrl: string | null
  originalIndex: number
  fields: SeriesFields
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
  items: SeriesOrderItem[]
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
  autoGain: number
  wasClamped: boolean
  userOverride: number
}

export interface VolumeConfigData {
  seriesName: string
  seriesPath: string
  globalVolumeMultiplier: number
  targetLufs: number
  maxMultiplier: number
  ffmpegAvailable: boolean
  lufsCacheExists: boolean
  items: VolumeRowItem[]
}

export interface VolumeOverride {
  originalIndex: number
  volume: number
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
  globalVolumeMultiplier: number
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

export interface PlaylistInfo {
  id: string
  name: string
  series: string[]
  songCount: number
}

export interface StageSong {
  order: number
  bgmId: string
  name: string
}

export interface StageInfo {
  uiStageId: string
  name: string
  hidden: boolean
  seriesId: string
  seriesName: string
  playlistId: string
  playlistName: string
  order: number
  songs: StageSong[]
}

export interface PlaylistInfoData {
  playlists: PlaylistInfo[]
  stages: StageInfo[]
}

// One mod song assigned to a playlist, with its per-song incidence (random-roll weight).
export interface PlaylistSongAssignment {
  seriesId: string
  seriesName: string
  filename: string
  title: string
  incidence: number
}

export interface PlaylistTarget {
  id: string
  name: string
  assignedCount: number
  assignments: PlaylistSongAssignment[]
}

// A mod track available to assign (the pool shown in the "add song" picker).
export interface ModSong {
  seriesId: string
  seriesName: string
  filename: string
  title: string
}

export interface ManagePlaylistsData {
  modName: string
  modPath: string
  playlists: PlaylistTarget[]
  songs: ModSong[]
}

// Flat assignment record sent from the renderer on save.
export interface PlaylistAssignmentInput {
  playlistId: string
  seriesId: string
  filename: string
  incidence: number
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
