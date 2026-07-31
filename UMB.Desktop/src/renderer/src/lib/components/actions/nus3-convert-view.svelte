<script lang="ts">
  import {
    AudioWaveform, CheckCircle, X, Play,
    Trash2, RefreshCw, Settings
  } from '@lucide/svelte'
  import { untrack } from 'svelte'
  import { _ } from 'svelte-i18n'
  import { modsStore } from '$lib/stores/mods.svelte'
  import { logStore } from '$lib/stores/logs.svelte'
  import EmptyState from '$lib/components/ui/empty-state.svelte'
  import IconButton from '$lib/components/ui/icon-button.svelte'
  import SeriesPicker from '$lib/components/ui/series-picker.svelte'
  import Nus3SettingsModal from './nus3-settings-modal.svelte'
  import Nus3AcceptModal from './nus3-accept-modal.svelte'
  import type {
    ModInfo, ModSeriesInfo,
    LoopCandidate, Nus3SourceTrack, Nus3TrackDecision, Nus3ConversionMeta,
    LoopAnalysisOptions
  } from '$lib/types/electron'

  let { activeMod }: { activeMod: ModInfo | null } = $props()

  let tab = $state<'convert' | 'review'>('convert')
  let loading = $state(false)
  let series = $state<ModSeriesInfo[]>([])
  let sourcesTracks = $state<Nus3SourceTrack[]>([])
  let analysisResults = $state<Map<string, LoopCandidate[]>>(new Map())
  let conversions = $state<Record<string, Nus3ConversionMeta>>({})
  let currentIndex = $state(0)
  let selectedRank = $state(1)
  let analyzing = $state(false)
  let reanalyzing = $state(false)
  let converting = $state(false)
  let writing = $state(false)
  let acceptModalOpen = $state(false)
  let settingsOpen = $state(false)
  let minScoreThreshold = $state(94.5)
  let previewLength = $state(5)
  // pymusiclooper tuning (applied via the settings panel, used by manual recalc).
  let minLoopDuration = $state(0) // seconds; 0 = pymusiclooper default multiplier
  let disablePruning = $state(false)
  let loadToken = 0
  let analysisToken = 0
  let waveformPeaks = $state<Map<string, number[]>>(new Map())
  let previewAudio = $state<HTMLAudioElement | null>(null)
  let previewPlaying = $state(false)
  let previewLoading = $state(false)
  let reviewPlayingId = $state<string | null>(null)
  let reviewLoadingId = $state<string | null>(null)

  const seriesPath = $derived(modsStore.activeSeriesPath)

  const pendingTracks = $derived(sourcesTracks.filter((t) => !t.converted))
  const convertedTracks = $derived(sourcesTracks.filter((t) => t.converted))
  const clampedIndex = $derived(Math.max(0, Math.min(currentIndex, pendingTracks.length - 1)))
  const currentTrack = $derived(pendingTracks[clampedIndex] ?? null)
  const allConverted = $derived(sourcesTracks.length > 0 && pendingTracks.length === 0)

  const currentCandidates = $derived(
    currentTrack ? (analysisResults.get(currentTrack.id) ?? []) : []
  )
  const selectedCandidate = $derived(
    currentCandidates.find((c) => c.rank === selectedRank) ?? currentCandidates[0] ?? null
  )

  const doneCount = $derived(convertedTracks.length)
  const totalCount = $derived(sourcesTracks.length)

  // Loop points are analyzed lazily per song: a pending track with no result yet
  // (or one being recalculated) shows the spinner until its candidates land.
  const currentTrackAnalyzing = $derived(
    (currentTrack != null && !currentTrack.converted && !analysisResults.has(currentTrack.id)) ||
    reanalyzing
  )
  const currentPeaks = $derived(
    currentTrack ? (waveformPeaks.get(currentTrack.id) ?? []) : []
  )

  const selectedSeries = $derived(
    series.find((entry) => entry.path === modsStore.activeSeriesPath) ?? null
  )

  async function loadSeries(modPath: string | null) {
    loadToken += 1
    const token = loadToken

    if (!modPath) {
      series = []
      modsStore.activeSeriesPath = null
      return
    }

    loading = true
    try {
      const nextSeries = await window.electron.umb.listModSeries(modPath)
      if (token !== loadToken) return
      series = nextSeries
      if (!nextSeries.some((e) => e.path === modsStore.activeSeriesPath)) {
        modsStore.activeSeriesPath = nextSeries[0]?.path ?? null
      }
    } finally {
      if (token === loadToken) loading = false
    }
  }

  async function loadSources(path: string | null) {
    if (!path) {
      resetState()
      return
    }

    analysisToken += 1
    const token = analysisToken

    analyzing = true
    tab = 'convert'
    currentIndex = 0
    selectedRank = 1
    sourcesTracks = []
    analysisResults = new Map()
    waveformPeaks = new Map()

    try {
      logStore.log('info', `Scanning series folder for audio files...`)
      const tracks = await window.electron.umb.listNus3Sources(path)
      if (token !== analysisToken) return

      sourcesTracks = tracks
      conversions = await window.electron.umb.loadNus3Conversions(path)
      if (token !== analysisToken) return

      if (tracks.length === 0) {
        logStore.log('warn', 'No source audio files found in series folder.')
        return
      }

      const pending = tracks.filter((t) => !t.converted)
      const already = tracks.length - pending.length
      if (already > 0) {
        logStore.log('info', `${already} song(s) already converted.`)
        if (pending.length === 0) tab = 'review'
      }

      logStore.log('info', `${pending.length} song(s) to convert. Loop points are calculated per song as you open them.`)
    } catch (err) {
      if (token !== analysisToken) return
      logStore.log('error', `Scan failed: ${err instanceof Error ? err.message : String(err)}`)
    } finally {
      if (token === analysisToken) analyzing = false
    }
  }

  // Lazily analyze a single track's loop points + waveform. Used both by the
  // per-song effect (as the current track changes) and when a converted song is
  // rejected back into the convert queue. Always records a result (even empty)
  // so the spinner clears.
  async function ensureAnalysis(track: Nus3SourceTrack) {
    const path = seriesPath
    if (!path || analysisResults.has(track.id)) return
    try {
      const result = await window.electron.umb.analyzeLoopPoints(path, track.src)
      sourcesTracks = sourcesTracks.map((t) =>
        t.id === track.id ? { ...t, duration: result.track.duration, durationSeconds: result.track.durationSeconds } : t
      )
      analysisResults.set(track.id, result.candidates)
      analysisResults = new Map(analysisResults)
      logStore.log('info', `"${track.name}" (${result.track.duration}): ${result.candidates.length} loop point(s) found.`)
      if (!waveformPeaks.has(track.id)) {
        const peaks = await window.electron.umb.extractWaveform(path, track.src, 140)
        if (peaks.length > 0) {
          waveformPeaks.set(track.id, peaks)
          waveformPeaks = new Map(waveformPeaks)
        }
      }
    } catch (err) {
      analysisResults.set(track.id, [])
      analysisResults = new Map(analysisResults)
      logStore.log('error', `Analysis failed for "${track.name}": ${err instanceof Error ? err.message : String(err)}`)
    }
  }

  // Force a fresh analysis of the current track using the settings-panel tuning.
  // With default settings (min loop 0, pruning on) this re-runs the default pass
  // plus the automatic relaxed fallback; otherwise it honours the chosen params.
  async function reanalyzeCurrent() {
    const track = currentTrack
    const path = seriesPath
    if (!track || !path || reanalyzing) return

    reanalyzing = true
    stopPreview()
    try {
      const options: LoopAnalysisOptions = { force: true }
      if (minLoopDuration > 0) options.minLoopDuration = minLoopDuration
      if (disablePruning) options.disablePruning = true

      const result = await window.electron.umb.analyzeLoopPoints(path, track.src, options)
      sourcesTracks = sourcesTracks.map((t) =>
        t.id === track.id ? { ...t, duration: result.track.duration, durationSeconds: result.track.durationSeconds } : t
      )
      analysisResults.set(track.id, result.candidates)
      analysisResults = new Map(analysisResults)
      selectedRank = 1
      logStore.log('info', `Recalculated "${track.name}": ${result.candidates.length} loop point(s) found.`)
    } catch (err) {
      logStore.log('error', `Recalculate failed for "${track.name}": ${err instanceof Error ? err.message : String(err)}`)
    } finally {
      reanalyzing = false
    }
  }

  // Load real waveform peaks + duration for converted tracks on the Review tab.
  async function ensureReviewData() {
    const path = seriesPath
    if (!path) return
    for (const track of convertedTracks) {
      if (!waveformPeaks.has(track.id)) {
        const peaks = await window.electron.umb.extractWaveform(path, track.src, 140)
        if (peaks.length > 0) {
          waveformPeaks.set(track.id, peaks)
          waveformPeaks = new Map(waveformPeaks)
        }
      }
      if (!track.durationSeconds || track.durationSeconds <= 0) {
        const dur = await window.electron.umb.getTrackDuration(path, track.src)
        if (dur > 0) {
          sourcesTracks = sourcesTracks.map((t) =>
            t.id === track.id ? { ...t, durationSeconds: dur } : t
          )
        }
      }
    }
  }

  function resetState() {
    sourcesTracks = []
    analysisResults = new Map()
    waveformPeaks = new Map()
    conversions = {}
    currentIndex = 0
    selectedRank = 1
    tab = 'convert'
    stopPreview()
  }

  async function playLoopPreview() {
    if (!selectedCandidate || !currentTrack || !seriesPath) return

    stopPreview()
    previewLoading = true

    try {
      const dataUrl = await window.electron.umb.generateLoopPreview(
        seriesPath,
        currentTrack.src,
        selectedCandidate.loopStart,
        selectedCandidate.loopEnd,
        previewLength
      )
      if (!dataUrl) {
        logStore.log('warn', 'Failed to generate loop preview.')
        return
      }

      const audio = new Audio(dataUrl)
      audio.onended = () => { previewPlaying = false }
      previewAudio = audio
      await audio.play()
      previewPlaying = true
    } catch (err) {
      logStore.log('error', `Preview failed: ${err instanceof Error ? err.message : String(err)}`)
    } finally {
      previewLoading = false
    }
  }

  function stopPreview() {
    if (previewAudio) {
      previewAudio.pause()
      previewAudio = null
    }
    previewPlaying = false
    reviewPlayingId = null
  }

  // Review tab: preview the loop for a converted track (chosen loop or end-to-end).
  async function playReviewPreview(track: Nus3SourceTrack) {
    if (!seriesPath) return

    if (reviewPlayingId === track.id) {
      stopPreview()
      return
    }

    stopPreview()
    reviewLoadingId = track.id

    try {
      const cand = conversions[track.id]?.candidate
      // loopEnd <= loopStart signals end-to-end; main probes the full duration.
      const start = cand ? cand.loopStart : 0
      const end = cand ? cand.loopEnd : 0
      const dataUrl = await window.electron.umb.generateLoopPreview(
        seriesPath,
        track.src,
        start,
        end,
        previewLength
      )
      if (!dataUrl) {
        logStore.log('warn', `Failed to generate loop preview for "${track.name}".`)
        return
      }

      const audio = new Audio(dataUrl)
      audio.onended = () => { if (reviewPlayingId === track.id) reviewPlayingId = null }
      previewAudio = audio
      reviewPlayingId = track.id
      await audio.play()
    } catch (err) {
      logStore.log('error', `Preview failed: ${err instanceof Error ? err.message : String(err)}`)
      reviewPlayingId = null
    } finally {
      reviewLoadingId = null
    }
  }

  function selectCandidate(rank: number) {
    selectedRank = rank
  }


  async function convertCurrent(mode: 'loop' | 'end-to-end', candidate?: LoopCandidate) {
    if (!currentTrack || !seriesPath || converting) return
    if (mode === 'loop' && !candidate) return

    const track = currentTrack
    converting = true
    stopPreview()
    logStore.clear()

    try {
      const decision: Nus3TrackDecision = {
        trackId: track.id,
        mode,
        candidate,
        status: mode === 'loop' ? 'accepted' : 'rejected'
      }
      const ok = await window.electron.umb.convertNus3Track(seriesPath, decision)
      if (ok) {
        conversions = { ...conversions, [track.id]: { mode, candidate } }
        sourcesTracks = sourcesTracks.map((t) =>
          t.id === track.id ? { ...t, converted: true } : t
        )
        selectedRank = 1
        logStore.log('info', `Converted "${track.name}".`)
      } else {
        logStore.log('error', `Conversion failed for "${track.name}".`)
      }
    } finally {
      converting = false
    }
  }

  function acceptCurrent() {
    if (selectedCandidate) void convertCurrent('loop', selectedCandidate)
  }

  function rejectCurrent() {
    void convertCurrent('end-to-end', undefined)
  }

  function goToPending(index: number) {
    currentIndex = index
    selectedRank = 1
    stopPreview()
  }


  async function rejectFromReview(track: Nus3SourceTrack) {
    if (!seriesPath) return
    await window.electron.umb.rejectNus3Track(seriesPath, track.id)
    const next = { ...conversions }
    delete next[track.id]
    conversions = next
    sourcesTracks = sourcesTracks.map((t) =>
      t.id === track.id ? { ...t, converted: false } : t
    )
    await ensureAnalysis(track)
  }

  function openAcceptModal() {
    if (convertedTracks.length === 0) return
    acceptModalOpen = true
  }

  async function runAccept(deleteSources: boolean) {
    acceptModalOpen = false
    if (!seriesPath) return
    writing = true
    logStore.clear()
    if (!logStore.drawerOpen) logStore.toggleDrawer()

    try {
      await window.electron.umb.acceptNus3Files(seriesPath, deleteSources)
    } finally {
      writing = false
    }
    await loadSources(seriesPath)
  }

  function openSettings() {
    settingsOpen = true
  }

  function applySettings(next: { minScoreThreshold: number; previewLength: number; minLoopDuration: number; disablePruning: boolean }) {
    minScoreThreshold = next.minScoreThreshold
    previewLength = next.previewLength
    minLoopDuration = next.minLoopDuration
    disablePruning = next.disablePruning
    settingsOpen = false
  }

  function handleReload() {
    void loadSeries(activeMod?.path ?? null)
    if (seriesPath) void loadSources(seriesPath)
  }

  $effect(() => {
    const modPath = activeMod?.path ?? null
    untrack(() => {
      resetState()
      void loadSeries(modPath)
    })
  })

  // Analyze the current track's loop points lazily.
  $effect(() => {
    const track = currentTrack
    const path = seriesPath
    if (!track || !path || track.converted || analysisResults.has(track.id)) return
    untrack(() => { void ensureAnalysis(track) })
  })

  $effect(() => {
    if (tab !== 'review' || !seriesPath) return
    // Re-run when the set of converted tracks changes (convert/reject).
    void convertedTracks.length
    untrack(() => { void ensureReviewData() })
  })

  function downsamplePeaks(peaks: number[], targetCount: number): number[] {
    if (peaks.length <= targetCount) return peaks
    const result: number[] = []
    const step = peaks.length / targetCount
    for (let i = 0; i < targetCount; i++) {
      const start = Math.floor(i * step)
      const end = Math.floor((i + 1) * step)
      let max = 0
      for (let j = start; j < end; j++) {
        if (peaks[j] > max) max = peaks[j]
      }
      result.push(max)
    }
    return result
  }

  function generatePeaks(count: number, seed: number = 0): number[] {
    return Array.from({ length: count }, (_, i) => {
      const t = i / count
      const env = Math.min(1, 1.6 * t * (1 - t) * 3.4)
      const wob = Math.sin((i + seed) * 0.7) * 0.5 + Math.sin((i + seed) * 1.9) * 0.3 + Math.sin((i + seed) * 0.31) * 0.4
      return env * (0.5 + Math.abs(wob) * 0.5)
    })
  }

  function handleKeydown(e: KeyboardEvent) {
    if (settingsOpen || acceptModalOpen || converting || writing) return

    if (tab === 'convert' && currentCandidates.length > 0) {
      if (e.key === 'ArrowUp') {
        e.preventDefault()
        const idx = currentCandidates.findIndex((c) => c.rank === selectedRank)
        if (idx > 0) selectedRank = currentCandidates[idx - 1].rank
      } else if (e.key === 'ArrowDown') {
        e.preventDefault()
        const idx = currentCandidates.findIndex((c) => c.rank === selectedRank)
        if (idx < currentCandidates.length - 1) selectedRank = currentCandidates[idx + 1].rank
      } else if (e.key === 'Enter') {
        e.preventDefault()
        acceptCurrent()
      } else if (e.key === 'r' && !e.ctrlKey && !e.metaKey) {
        rejectCurrent()
      } else if (e.key >= '1' && e.key <= '9') {
        const rank = parseInt(e.key)
        if (currentCandidates.some((c) => c.rank === rank)) {
          selectedRank = rank
        }
      } else if (e.key === '0') {
        if (currentCandidates.some((c) => c.rank === 10)) {
          selectedRank = 10
        }
      }
    } else if (tab === 'review') {
      if (e.key === 'Enter') {
        e.preventDefault()
        openAcceptModal()
      }
    }
  }
</script>

<svelte:window onkeydown={handleKeydown} />

<div class="flex-1 overflow-hidden">
  <div class="flex h-full min-h-0">
    <SeriesPicker
      {activeMod}
      {series}
      {loading}
      activePath={modsStore.activeSeriesPath}
      onSelect={(path) => { modsStore.activeSeriesPath = path; void loadSources(path) }}
      onReload={handleReload}
      heading={$_('orderTracks.seriesHeading')}
      subtitleFallback={$_('orderTracks.selectModSubtitle')}
      chooseModText={$_('orderTracks.chooseMod')}
      loadingText={$_('orderTracks.loadingSeries')}
      emptyText={$_('orderTracks.noSeries')}
      reloadTitle={$_('nus3Convert.reload')}
    >
      {#snippet actions()}
        <IconButton onclick={openSettings} title={$_('nus3Convert.settingsTitle')}>
          <Settings size={14} />
        </IconButton>
      {/snippet}
    </SeriesPicker>

    <section class="flex h-full min-h-0 min-w-0 flex-1 flex-col border border-border bg-card overflow-hidden">
      <div class="gradient-strip h-[3px] shrink-0"></div>

      {#if !selectedSeries || sourcesTracks.length === 0}
        <EmptyState
          title={analyzing
            ? $_('nus3Convert.loading')
            : selectedSeries
              ? $_('nus3Convert.noConvertableTitle')
              : $_('nus3Convert.chooseSeries')}
          body={analyzing ? $_('nus3Convert.loading') : selectedSeries ? $_('nus3Convert.noConvertableHint') : ''}
        >
          {#snippet icon()}<AudioWaveform size={20} />{/snippet}
        </EmptyState>
      {:else}
        <div class="shrink-0 flex items-center gap-1 border-b border-border bg-card px-4 pt-1">
          <button
            onclick={() => (tab = 'convert')}
            class="relative px-3 py-2 text-[13px] font-medium transition-colors {tab === 'convert' ? 'gradient-text' : 'text-muted-foreground hover:text-foreground'}"
          >
            {$_('nus3Convert.tabConvert')}
            {#if tab === 'convert'}
              <span class="gradient-strip absolute -bottom-px left-0 right-0 h-[2px] rounded-full"></span>
            {/if}
          </button>
          <button
            onclick={() => (tab = 'review')}
            class="relative px-3 py-2 text-[13px] font-medium transition-colors {tab === 'review' ? 'gradient-text' : 'text-muted-foreground hover:text-foreground'}"
          >
            {$_('nus3Convert.tabReview')}
            {#if tab === 'review'}
              <span class="gradient-strip absolute -bottom-px left-0 right-0 h-[2px] rounded-full"></span>
            {/if}
          </button>
        </div>

        {#if tab === 'convert'}
        <!-- ── Convert tab ── -->
        {#if allConverted}
          <!-- Everything converted: nudge the user toward Review & Save -->
          <EmptyState title={$_('nus3Convert.allConvertedTitle')} body={$_('nus3Convert.allConvertedHint')}>
            {#snippet icon()}<CheckCircle size={20} />{/snippet}
            <button
              onclick={() => (tab = 'review')}
              class="inline-flex h-8 items-center gap-1.5 rounded-md border-0 px-3 text-[12.5px] font-medium text-white transition-colors"
              style="background: linear-gradient(135deg, hsl(var(--gradient-from)), hsl(var(--gradient-to))); box-shadow: 0 4px 14px -2px hsl(var(--gradient-from) / .35);"
            >
              {$_('nus3Convert.tabReview')}
            </button>
          </EmptyState>
        {:else}
        <div class="shrink-0 border-b border-border bg-card px-5 py-3">
          <div class="flex flex-col gap-2">
            <span class="gradient-text text-[11px] font-semibold uppercase tracking-wide">
              {$_('nus3Convert.kicker1')}
            </span>
            <div class="flex items-center gap-4">
              <h1 class="min-w-0 flex-1 text-lg font-semibold tracking-tight">
                {currentTrack?.name ?? ''}
                {#if currentTrackAnalyzing}
                  <span class="ml-2 text-[12px] font-normal text-muted-foreground">{$_('nus3Convert.loading')}</span>
                {:else if converting}
                  <span class="ml-2 text-[12px] font-normal text-muted-foreground">{$_('nus3Convert.convertingTrack', { values: { name: currentTrack?.name ?? '' } })}</span>
                {/if}
              </h1>
              <div class="flex shrink-0 items-center gap-2">
              <button
                onclick={reanalyzeCurrent}
                disabled={reanalyzing || converting}
                title={$_('nus3Convert.reanalyzeHint')}
                class="inline-flex h-8 items-center gap-1.5 rounded-md border border-input bg-background px-3 text-[12.5px] font-medium transition-colors hover:bg-muted disabled:opacity-60"
              >
                <RefreshCw size={14} class={reanalyzing ? 'animate-spin' : ''} />
                {reanalyzing ? $_('nus3Convert.reanalyzing') : $_('nus3Convert.reanalyze')}
              </button>
              <button
                onclick={rejectCurrent}
                disabled={converting}
                class="inline-flex h-8 items-center gap-1.5 rounded-md border-0 px-3 text-[12.5px] font-medium text-white transition-colors disabled:opacity-60"
                style="background: hsl(271 76% 53%);"
              >
                {$_('nus3Convert.rejectAll')}
              </button>
              <button
                onclick={acceptCurrent}
                disabled={!selectedCandidate || converting}
                class="inline-flex h-8 items-center gap-1.5 rounded-md border-0 px-3 text-[12.5px] font-medium text-white transition-colors disabled:opacity-60"
                style="background: linear-gradient(135deg, hsl(var(--gradient-from)), hsl(var(--gradient-to))); box-shadow: 0 4px 14px -2px hsl(var(--gradient-from) / .35);"
              >
                {#if converting}
                  <RefreshCw size={14} class="animate-spin" />
                {:else}
                  <CheckCircle size={14} />
                {/if}
                {$_('nus3Convert.convertAccept', { values: { rank: selectedRank } })}
              </button>
              </div>
            </div>
          </div>
        </div>

        <div class="min-h-0 flex-1 overflow-auto bg-background px-5 py-2.5" style="display: flex; flex-direction: column; gap: 10px;">
          <!-- Progress strip + track pills (single row) -->
          <div class="flex items-center gap-2">
            <span class="shrink-0 text-[10px] font-semibold uppercase tracking-wide text-muted-foreground">
              {$_('nus3Convert.seriesQueue', { values: { series: selectedSeries?.name ?? '' } })}
            </span>
            <span class="shrink-0 inline-flex h-[20px] items-center rounded-full border border-border bg-muted px-1.5 text-[10px] font-medium text-muted-foreground">
              {doneCount}/{totalCount}
            </span>
            <div class="h-1 w-[100px] shrink-0 overflow-hidden rounded-full bg-muted">
              <div
                class="gradient-strip h-full rounded-full transition-all"
                style="width: {totalCount > 0 ? (doneCount / totalCount) * 100 : 0}%"
              ></div>
            </div>
            <div class="flex min-w-0 flex-1 items-center gap-1 overflow-hidden">
              {#each pendingTracks as track, i}
                {@const isCurrent = i === clampedIndex}
                {@const trackNo = sourcesTracks.findIndex((t) => t.id === track.id) + 1}
                <button
                  onclick={() => goToPending(i)}
                  class="flex shrink-0 items-center gap-1 rounded-full border px-2 py-0.5 text-[11px] font-medium whitespace-nowrap transition-colors"
                  style={isCurrent
                    ? 'background: linear-gradient(135deg, hsl(var(--gradient-from) / .14), hsl(var(--gradient-to) / .14)); border-color: hsl(var(--gradient-from) / .4); font-weight: 700;'
                    : 'background: transparent; border-color: hsl(var(--border)); color: hsl(var(--muted-foreground));'}
                >
                  <span class="font-mono text-[10px]">{String(trackNo).padStart(2, '0')}</span>
                  <span class="max-w-[110px] overflow-hidden text-ellipsis">{track.name}</span>
                </button>
              {/each}
            </div>
          </div>

          {#if currentTrackAnalyzing}
            <div class="grid min-h-[260px] flex-1 place-items-center rounded-xl border border-border bg-card p-6">
              <div class="flex flex-col items-center gap-4 text-center">
                <RefreshCw size={36} class="animate-spin" style="color: hsl(var(--gradient-from));" />
                <div class="flex flex-col gap-1">
                  <p class="text-sm font-semibold">{$_('nus3Convert.calculatingLoops')}</p>
                  <p class="text-[13px] text-muted-foreground">{$_('nus3Convert.calculatingLoopsHint')}</p>
                </div>
              </div>
            </div>
          {:else if selectedCandidate}
            {@const c = selectedCandidate}
            {@const dur = currentTrack?.durationSeconds || 228}
            {@const peaks = currentPeaks.length > 0 ? currentPeaks : generatePeaks(140)}
            {@const bars = peaks.length > 0 ? peaks.length : 140}
            <div class="rounded-xl border border-border bg-card p-3">
              <div class="mb-2 flex items-center justify-between">
                <div class="flex items-center gap-2.5">
                  <span class="text-base font-semibold">
                    {$_('nus3Convert.candidateTitle', { values: { rank: c.rank } })}
                  </span>
                  <span
                    class="inline-flex h-[22px] items-center rounded-full px-2 text-[11px] font-medium"
                    style="background: linear-gradient(135deg, hsl(var(--gradient-from) / .15), hsl(var(--gradient-to) / .15)); color: hsl(var(--gradient-from)); border: 1px solid hsl(var(--gradient-from) / .3);"
                  >
                    {$_('nus3Convert.score', { values: { score: c.score.toFixed(1) } })}
                  </span>
                  <span class="font-mono text-xs text-muted-foreground">
                    {c.loopStartStr} &rarr; {c.loopEndStr} &middot; loop {c.loopLengthStr}
                  </span>
                </div>
              </div>

              <!-- Waveform SVG -->
              <div class="relative pt-1.5 pb-5">
                <svg
                  viewBox="0 0 {bars * 3} 120"
                  width="100%"
                  height="120"
                  preserveAspectRatio="none"
                  class="block"
                >
                  <defs>
                    <linearGradient id="wf-grad" x1="0" y1="0" x2="1" y2="0">
                      <stop offset="0%" stop-color="hsl(var(--gradient-from))" />
                      <stop offset="100%" stop-color="hsl(var(--gradient-to))" />
                    </linearGradient>
                    <linearGradient id="wf-grad-soft" x1="0" y1="0" x2="0" y2="1">
                      <stop offset="0%" stop-color="hsl(var(--gradient-from) / .25)" />
                      <stop offset="100%" stop-color="hsl(var(--gradient-to) / .04)" />
                    </linearGradient>
                  </defs>

                  <!-- Loop region shading -->
                  <rect
                    x={((c.loopStart) / dur) * bars * 3}
                    y="0"
                    width={((c.loopEnd - c.loopStart) / dur) * bars * 3}
                    height="120"
                    fill="url(#wf-grad-soft)"
                  />

                  <!-- Peak bars -->
                  {#each peaks as p, i}
                    {@const h = 6 + p * 104}
                    {@const y = (120 - h) / 2}
                    {@const x = i * 3}
                    {@const frac = x / (bars * 3)}
                    {@const insideLoop = frac >= c.loopStart / dur && frac <= c.loopEnd / dur}
                    <rect
                      x={x}
                      y={y}
                      width="1.6"
                      height={h}
                      rx="0.8"
                      fill={insideLoop ? 'url(#wf-grad)' : 'hsl(var(--muted-foreground))'}
                      opacity={insideLoop ? 1 : 0.35}
                    />
                  {/each}

                  <!-- All candidate markers -->
                  {#each currentCandidates as cand}
                    {@const xS = (cand.loopStart / dur) * bars * 3}
                    {@const xE = (cand.loopEnd / dur) * bars * 3}
                    {@const active = cand.rank === selectedRank}
                    <g opacity={active ? 1 : 0.32}>
                      <line
                        x1={xS} x2={xS} y1="4" y2="116"
                        stroke={active ? 'hsl(var(--gradient-from))' : 'hsl(var(--foreground))'}
                        stroke-width={active ? 2 : 1}
                      />
                      <line
                        x1={xE} x2={xE} y1="4" y2="116"
                        stroke={active ? 'hsl(var(--gradient-to))' : 'hsl(var(--foreground))'}
                        stroke-width={active ? 2 : 1}
                        stroke-dasharray="3 2"
                      />
                    </g>
                  {/each}

                  <!-- Selected marker dots -->
                  <circle cx={(c.loopStart / dur) * bars * 3} cy="10" r="5" fill="hsl(var(--gradient-from))" />
                  <circle cx={(c.loopEnd / dur) * bars * 3} cy="10" r="5" fill="hsl(var(--gradient-to))" />
                </svg>

                <!-- Time labels -->
                <div class="absolute bottom-0 left-0 right-0 flex justify-between pointer-events-none">
                  <span class="font-mono text-[10px] text-muted-foreground">0:00</span>
                  {#each Array.from({ length: Math.floor(dur / 60) }, (_, i) => i + 1) as min}
                    <span class="font-mono text-[10px] text-muted-foreground">{min}:00</span>
                  {/each}
                  <span class="font-mono text-[10px] text-muted-foreground">
                    {Math.floor(dur / 60)}:{String(Math.floor(dur % 60)).padStart(2, '0')}
                  </span>
                </div>
              </div>

              <!-- Transport bar -->
              <div class="flex items-center gap-2 rounded-lg border border-border bg-card px-2.5 py-1.5">
                <button
                  onclick={() => previewPlaying ? stopPreview() : playLoopPreview()}
                  disabled={previewLoading}
                  class="inline-flex h-[30px] min-w-[110px] items-center justify-center gap-1.5 rounded-sm border-0 px-2.5 text-[12.5px] font-medium text-white disabled:opacity-60"
                  style="background: linear-gradient(135deg, hsl(var(--gradient-from)), hsl(var(--gradient-to))); box-shadow: 0 1px 0 hsl(var(--background)) inset, 0 4px 14px -2px hsl(var(--gradient-from) / .35);"
                >
                  {#if previewLoading}
                    <RefreshCw size={13} class="animate-spin" />
                  {:else}
                    <Play size={13} />
                  {/if}
                  {previewPlaying ? 'Stop' : previewLoading ? 'Loading...' : $_('nus3Convert.loopPreview')}
                </button>
                <div class="flex-1"></div>
                <span class="font-mono text-xs text-muted-foreground">{c.loopLengthStr}</span>
              </div>
            </div>
          {:else}
            <!-- No loop points found: only end-to-end is available -->
            <div class="grid min-h-[260px] flex-1 place-items-center rounded-xl border border-border bg-card p-6">
              <div class="flex max-w-[620px] flex-col items-center gap-4 text-center">
                <div
                  class="flex h-12 w-12 items-center justify-center rounded-2xl border border-border"
                  style="background: linear-gradient(135deg, hsl(var(--gradient-from) / .13), hsl(var(--gradient-to) / .16)); color: hsl(var(--gradient-from));"
                >
                  <AudioWaveform size={20} />
                </div>
                <div class="flex flex-col gap-1">
                  <p class="text-sm font-semibold">{$_('nus3Convert.noLoopTitle')}</p>
                  <p class="text-[13px] text-muted-foreground">{$_('nus3Convert.noLoopHint')}</p>
                </div>
                <div class="flex flex-wrap items-center justify-center gap-2.5 pt-1">
                  <button
                    onclick={openSettings}
                    class="inline-flex h-10 items-center gap-2 whitespace-nowrap rounded-lg border border-input bg-background px-4 text-[13px] font-medium transition-colors hover:bg-muted"
                  >
                    <Settings size={15} />
                    {$_('nus3Convert.settingsTitle')}
                  </button>
                  <button
                    onclick={reanalyzeCurrent}
                    disabled={reanalyzing || converting}
                    class="inline-flex h-10 items-center gap-2 whitespace-nowrap rounded-lg border border-input bg-background px-4 text-[13px] font-medium transition-colors hover:bg-muted disabled:opacity-60"
                  >
                    <RefreshCw size={15} class={reanalyzing ? 'animate-spin' : ''} />
                    {reanalyzing ? $_('nus3Convert.reanalyzing') : $_('nus3Convert.reanalyze')}
                  </button>
                  <button
                    onclick={rejectCurrent}
                    disabled={converting}
                    class="inline-flex h-10 items-center gap-2 whitespace-nowrap rounded-lg border-0 px-4 text-[13px] font-medium text-white transition-colors disabled:opacity-60"
                    style="background: hsl(271 76% 53%);"
                  >
                    {$_('nus3Convert.rejectAll')}
                  </button>
                </div>
              </div>
            </div>
          {/if}

          {#if !currentTrackAnalyzing && currentCandidates.length > 0}
            <div class="flex min-h-0 flex-1 flex-col overflow-hidden rounded-xl border border-border bg-card">
              <div class="flex shrink-0 items-center justify-between border-b border-border px-3 py-1.5">
                <div class="flex items-center gap-2">
                  <span class="text-[13px] font-semibold">
                    {$_('nus3Convert.candidateCount', { values: { count: currentCandidates.length } })}
                  </span>
                  <span class="inline-flex h-[20px] items-center rounded-full border border-border bg-transparent px-1.5 text-[10px] font-medium text-muted-foreground">
                    {$_('nus3Convert.clickToPreview')}
                  </span>
                </div>
                <span class="font-mono text-[11px] text-muted-foreground">{$_('nus3Convert.sortedBy')}</span>
              </div>

              <div class="min-h-0 flex-1 overflow-auto">
                <table class="w-full border-collapse text-[12px]">
                  <thead class="sticky top-0">
                    <tr style="background: hsl(var(--muted) / .5);">
                      {#each [$_('nus3Convert.colRank'), $_('nus3Convert.colScore'), $_('nus3Convert.colLoopStart'), $_('nus3Convert.colLoopEnd'), $_('nus3Convert.colLength'), $_('nus3Convert.colNoteDist'), $_('nus3Convert.colRms'), $_('nus3Convert.colSpec')] as header}
                        <th class="px-2 py-1 text-left text-[10px] font-semibold uppercase tracking-wider text-muted-foreground">
                          {header}
                        </th>
                      {/each}
                    </tr>
                  </thead>
                  <tbody>
                    {#each currentCandidates as cand}
                      {@const isSel = cand.rank === selectedRank}
                      {@const belowThreshold = cand.score < minScoreThreshold}
                      <tr
                        onclick={() => selectCandidate(cand.rank)}
                        class="cursor-pointer border-t border-border transition-colors hover:bg-muted/50"
                        style="{isSel ? 'background: linear-gradient(90deg, hsl(var(--gradient-from) / .10), transparent);' : ''}{belowThreshold ? ' opacity: 0.45;' : ''}"
                      >
                        <td class="relative px-2 py-1">
                          {#if isSel}
                            <span
                              class="absolute left-0 top-0.5 bottom-0.5 w-[3px] rounded-r"
                              style="background: linear-gradient(180deg, hsl(var(--gradient-from)), hsl(var(--gradient-to)));"
                            ></span>
                          {/if}
                          <span class="font-mono text-[11px] {isSel ? 'font-bold' : 'font-medium'}">
                            {String(cand.rank).padStart(2, '0')}
                          </span>
                        </td>
                        <td class="px-2 py-1">
                          <div class="flex items-center gap-1.5">
                            <div class="h-1 w-[50px] overflow-hidden rounded-full bg-muted">
                              <div class="gradient-strip h-full rounded-full" style="width: {cand.score}%;"></div>
                            </div>
                            <span class="font-mono text-[11px] {isSel ? 'font-bold' : 'font-medium'}">{cand.score.toFixed(1)}</span>
                          </div>
                        </td>
                        <td class="px-2 py-1 font-mono text-[11px]">{cand.loopStartStr}</td>
                        <td class="px-2 py-1 font-mono text-[11px]">{cand.loopEndStr}</td>
                        <td class="px-2 py-1 font-mono text-[11px]">{cand.loopLengthStr}</td>
                        <td class="px-2 py-1 font-mono text-[11px]">{(cand.noteDistance ?? 0).toFixed(4)}</td>
                        <td class="px-2 py-1 font-mono text-[11px]">{cand.rmsDelta.toFixed(1)}</td>
                        <td class="px-2 py-1 font-mono text-[11px]">{(cand.spectralSim * 100).toFixed(0)}%</td>
                      </tr>
                    {/each}
                  </tbody>
                </table>
              </div>
            </div>
          {/if}
        </div>
        {/if}

        {:else}
        <!-- ── Review & Save tab ── -->
        <div class="shrink-0 border-b border-border bg-card px-5 py-3">
          <div class="flex items-center gap-4">
            <div class="flex min-w-0 flex-1 flex-col gap-1">
              <span class="gradient-text text-[11px] font-semibold uppercase tracking-wide">
                {$_('nus3Convert.kicker2')}
              </span>
              <h1 class="text-lg font-semibold tracking-tight">
                {$_('nus3Convert.reviewTitle', { values: { count: convertedTracks.length } })}
              </h1>
              <p class="max-w-[720px] text-[12.5px] text-muted-foreground">
                {$_('nus3Convert.reviewSubtitle')}
              </p>
            </div>
            <div class="flex shrink-0 items-center gap-2">
              <button
                onclick={openAcceptModal}
                disabled={writing || convertedTracks.length === 0}
                class="inline-flex h-8 items-center gap-1.5 rounded-md border-0 px-3 text-[12.5px] font-medium text-white transition-colors disabled:opacity-60"
                style="background: linear-gradient(135deg, hsl(var(--gradient-from)), hsl(var(--gradient-to))); box-shadow: 0 4px 14px -2px hsl(var(--gradient-from) / .35);"
              >
                <CheckCircle size={14} />
                {writing ? $_('nus3Convert.writing') : $_('nus3Convert.acceptWrite')}
              </button>
            </div>
          </div>
        </div>

        <div class="min-h-0 flex-1 overflow-auto bg-background px-5 py-2.5" style="display: flex; flex-direction: column; gap: 8px;">
          {#if convertedTracks.length === 0}
            <div class="grid min-h-[240px] place-items-center">
              <p class="max-w-[360px] text-center text-[13px] text-muted-foreground">{$_('nus3Convert.reviewEmpty')}</p>
            </div>
          {:else}
          <div class="flex items-center gap-2.5">
            <span class="text-[10.5px] font-semibold uppercase tracking-wide text-muted-foreground">
              {$_('nus3Convert.reviewQueue')}
            </span>
            <span
              class="inline-flex h-[22px] items-center rounded-full px-2 text-[11px] font-medium"
              style="background: hsl(160 84% 39% / .12); color: hsl(160 84% 30%); border: 1px solid hsl(160 84% 39% / .3);"
            >
              {$_('nus3Convert.keeping', { values: { count: convertedTracks.length } })}
            </span>
          </div>

          {#each convertedTracks as track}
            {@const meta = conversions[track.id]}
            {@const cand = meta?.candidate}
            {@const realPeaks = waveformPeaks.get(track.id) ?? []}
            {@const miniPeaks = realPeaks.length > 0 ? downsamplePeaks(realPeaks, 60) : generatePeaks(60, track.id.charCodeAt(0))}
            <div class="rounded-xl border border-border bg-card p-3">
              <div class="flex items-center gap-3.5">
                <button
                  onclick={() => playReviewPreview(track)}
                  disabled={reviewLoadingId === track.id}
                  class="inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-lg border-0 text-white disabled:opacity-60"
                  style="background: linear-gradient(135deg, hsl(var(--gradient-from)), hsl(var(--gradient-to))); box-shadow: 0 1px 0 hsl(var(--background)) inset, 0 4px 14px -2px hsl(var(--gradient-from) / .35);"
                >
                  {#if reviewLoadingId === track.id}
                    <RefreshCw size={13} class="animate-spin" />
                  {:else if reviewPlayingId === track.id}
                    <X size={13} />
                  {:else}
                    <Play size={13} />
                  {/if}
                </button>

                <div class="flex min-w-0 flex-1 flex-col gap-0.5">
                  <div class="flex items-center gap-2">
                    <span class="text-[13.5px] font-semibold">{track.name}</span>
                    {#if cand}
                      <span
                        class="inline-flex h-[18px] items-center rounded-full px-2 text-[10px] font-medium"
                        style="background: linear-gradient(135deg, hsl(var(--gradient-from) / .15), hsl(var(--gradient-to) / .15)); color: hsl(var(--gradient-from)); border: 1px solid hsl(var(--gradient-from) / .3);"
                      >
                        #{cand.rank} &middot; {cand.score.toFixed(1)}
                      </span>
                    {/if}
                  </div>
                  {#if cand}
                    <span class="font-mono text-xs text-muted-foreground">
                      {cand.loopStartStr} &rarr; {cand.loopEndStr} &middot; loop {cand.loopLengthStr}
                    </span>
                  {:else}
                    <span class="font-mono text-xs text-muted-foreground">{$_('nus3Convert.endToEndLoop')}</span>
                  {/if}
                </div>

                {#if cand}
                  {@const sFrac = cand.loopStart / (track.durationSeconds || 228)}
                  {@const eFrac = cand.loopEnd / (track.durationSeconds || 228)}
                  <div class="w-[260px] shrink-0">
                    <svg viewBox="0 0 120 36" width="100%" height="36" preserveAspectRatio="none" class="block">
                      <rect
                        x={sFrac * 120} y="0"
                        width={(eFrac - sFrac) * 120}
                        height="36"
                        fill="hsl(var(--gradient-from) / .12)"
                      />
                      {#each miniPeaks as p, i}
                        {@const h = 3 + p * 28}
                        {@const y = (36 - h) / 2}
                        {@const x = i * 2}
                        {@const inside = x / 120 >= sFrac && x / 120 <= eFrac}
                        <rect
                          x={x} y={y} width="1.2" height={h} rx="0.6"
                          fill={inside ? 'hsl(var(--gradient-from))' : 'hsl(var(--muted-foreground))'}
                          opacity={inside ? 0.9 : 0.35}
                        />
                      {/each}
                      <line
                        x1={sFrac * 120} x2={sFrac * 120} y1="2" y2="34"
                        stroke="hsl(var(--gradient-from))" stroke-width="1.4"
                      />
                      <line
                        x1={eFrac * 120} x2={eFrac * 120} y1="2" y2="34"
                        stroke="hsl(var(--gradient-to))" stroke-width="1.4" stroke-dasharray="2 2"
                      />
                    </svg>
                  </div>
                {/if}

                <!-- Reject (delete nus3, convert again) -->
                <button
                  onclick={() => rejectFromReview(track)}
                  title={$_('nus3Convert.removeConverted')}
                  class="inline-flex h-9 w-9 shrink-0 items-center justify-center rounded-lg border-0 bg-transparent transition-colors hover:bg-destructive/10"
                  style="color: hsl(var(--destructive));"
                >
                  <Trash2 size={18} />
                </button>
              </div>
            </div>
          {/each}
          {/if}
        </div>
        {/if}
      {/if}
    </section>
  </div>
</div>

{#if settingsOpen}
  <Nus3SettingsModal
    initial={{ minScoreThreshold, previewLength, minLoopDuration, disablePruning }}
    onApply={applySettings}
    onClose={() => (settingsOpen = false)}
  />
{/if}

{#if acceptModalOpen}
  <Nus3AcceptModal onClose={() => (acceptModalOpen = false)} onAccept={runAccept} />
{/if}
