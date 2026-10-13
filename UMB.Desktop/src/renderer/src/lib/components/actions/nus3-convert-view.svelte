<script lang="ts">
  import AudioWaveform from '@lucide/svelte/icons/audio-waveform'
  import CheckCircle from '@lucide/svelte/icons/check-circle'
  import X from '@lucide/svelte/icons/x'
  import Play from '@lucide/svelte/icons/play'
  import Trash2 from '@lucide/svelte/icons/trash-2'
  import RefreshCw from '@lucide/svelte/icons/refresh-cw'
  import Settings from '@lucide/svelte/icons/settings'
  import { untrack } from 'svelte'
  import { _ } from 'svelte-i18n'
  import { modsStore } from '$lib/stores/mods.svelte'
  import { logStore } from '$lib/stores/logs.svelte'
  import { createSeriesLoader } from '$lib/series-loader'
  import ActionHeader from '$lib/components/ui/action-header.svelte'
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
  const seriesLoader = createSeriesLoader((v) => (loading = v))
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
    if (!modPath) {
      seriesLoader.invalidate()
      series = []
      modsStore.activeSeriesPath = null
      return
    }

    const nextSeries = await seriesLoader.load(modPath)
    if (nextSeries === null) return
    series = nextSeries
    if (!nextSeries.some((e) => e.path === modsStore.activeSeriesPath)) {
      modsStore.activeSeriesPath = nextSeries[0]?.path ?? null
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
    >
      {#snippet actions()}
        <IconButton onclick={openSettings} title={$_('nus3Convert.settingsTitle')}>
          <Settings size={14} />
        </IconButton>
      {/snippet}
    </SeriesPicker>

    <section class="surface-grain flex h-full min-h-0 min-w-0 flex-1 flex-col overflow-hidden bg-card">
      <ActionHeader title={$_('nav.items.nus3-convert')} subtitle={selectedSeries?.name ?? ''} />

      {#if !selectedSeries || sourcesTracks.length === 0}
        <EmptyState
          title={analyzing
            ? $_('nus3Convert.loading')
            : selectedSeries
              ? $_('nus3Convert.noConvertableTitle')
              : $_('common.selectSeries')}
          body={analyzing ? $_('nus3Convert.loading') : selectedSeries ? $_('nus3Convert.noConvertableHint') : ''}
        >
          {#snippet icon()}<AudioWaveform size={20} />{/snippet}
        </EmptyState>
      {:else}
        <div class="shrink-0 flex items-center gap-1 border-b border-line2 px-[22px]">
          <button
            onclick={() => (tab = 'convert')}
            class="relative px-3 py-2.5 text-[13px] font-medium transition-colors duration-100 {tab === 'convert' ? 'text-foreground' : 'text-muted-foreground hover:text-foreground'}"
          >
            {$_('nus3Convert.tabConvert')}
            {#if tab === 'convert'}
              <span class="absolute inset-x-2 -bottom-px h-0.5 bg-primary"></span>
            {/if}
          </button>
          <button
            onclick={() => (tab = 'review')}
            class="relative px-3 py-2.5 text-[13px] font-medium transition-colors duration-100 {tab === 'review' ? 'text-foreground' : 'text-muted-foreground hover:text-foreground'}"
          >
            {$_('nus3Convert.tabReview')}
            {#if tab === 'review'}
              <span class="absolute inset-x-2 -bottom-px h-0.5 bg-primary"></span>
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
              class="btn-primary"
            >
              {$_('nus3Convert.tabReview')}
            </button>
          </EmptyState>
        {:else}
        <div class="shrink-0 border-b border-line2 px-[22px] py-3">
          <div class="flex flex-col gap-2">
            <span class="text-[10.5px] font-semibold uppercase tracking-[.1em] text-dim">
              {$_('nus3Convert.kicker1')}
            </span>
            <div class="flex items-center gap-4">
              <h2 class="min-w-0 flex-1 text-[15px] font-semibold tracking-[-.01em]">
                {currentTrack?.name ?? ''}
                {#if currentTrackAnalyzing}
                  <span class="ml-2 text-[12px] font-normal text-muted-foreground">{$_('nus3Convert.loading')}</span>
                {:else if converting}
                  <span class="ml-2 text-[12px] font-normal text-muted-foreground">{$_('nus3Convert.convertingTrack', { values: { name: currentTrack?.name ?? '' } })}</span>
                {/if}
              </h2>
              <div class="flex shrink-0 items-center gap-2">
              <button
                onclick={reanalyzeCurrent}
                disabled={reanalyzing || converting}
                title={$_('nus3Convert.reanalyzeHint')}
                class="btn"
              >
                <RefreshCw size={14} class={reanalyzing ? 'animate-spin' : ''} />
                {reanalyzing ? $_('nus3Convert.reanalyzing') : $_('nus3Convert.reanalyze')}
              </button>
              <button
                onclick={rejectCurrent}
                disabled={converting}
                class="btn-danger"
              >
                {$_('nus3Convert.rejectAll')}
              </button>
              <button
                onclick={acceptCurrent}
                disabled={!selectedCandidate || converting}
                class="btn-primary"
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

        <div class="flex min-h-0 flex-1 flex-col gap-2.5 overflow-auto px-[22px] py-3.5">
          <!-- Progress strip + track pills (single row) -->
          <div class="flex items-center gap-2">
            <span class="shrink-0 text-[10.5px] font-semibold uppercase tracking-[.1em] text-dim">
              {$_('nus3Convert.seriesQueue', { values: { series: selectedSeries?.name ?? '' } })}
            </span>
            <span class="chip shrink-0 tabular-nums">
              {doneCount}/{totalCount}
            </span>
            <div class="h-1 w-[100px] shrink-0 overflow-hidden rounded-sm bg-muted">
              <div
                class="h-full bg-primary transition-all"
                style="width: {totalCount > 0 ? (doneCount / totalCount) * 100 : 0}%"
              ></div>
            </div>
            <div class="flex min-w-0 flex-1 items-center gap-1 overflow-hidden">
              {#each pendingTracks as track, i}
                {@const isCurrent = i === clampedIndex}
                {@const trackNo = sourcesTracks.findIndex((t) => t.id === track.id) + 1}
                <button
                  onclick={() => goToPending(i)}
                  class="chip shrink-0 whitespace-nowrap transition-colors duration-100 {isCurrent ? 'border-transparent bg-accbg font-medium text-accink' : 'bg-transparent hover:text-foreground'}"
                >
                  <span class="tabular-nums text-[10px]">{String(trackNo).padStart(2, '0')}</span>
                  <span class="max-w-[110px] overflow-hidden text-ellipsis">{track.name}</span>
                </button>
              {/each}
            </div>
          </div>

          {#if currentTrackAnalyzing}
            <div class="grid min-h-[260px] flex-1 place-items-center rounded-xl border border-line2 p-6">
              <div class="flex flex-col items-center gap-4 text-center">
                <RefreshCw size={28} class="animate-spin text-primary" />
                <p class="text-[13.5px] font-semibold">{$_('nus3Convert.calculatingLoops')}</p>
              </div>
            </div>
          {:else if selectedCandidate}
            {@const c = selectedCandidate}
            {@const dur = currentTrack?.durationSeconds || 228}
            {@const peaks = currentPeaks.length > 0 ? currentPeaks : generatePeaks(140)}
            {@const bars = peaks.length > 0 ? peaks.length : 140}
            <div class="rounded-xl border border-line2 p-3">
              <div class="mb-2 flex items-center justify-between">
                <div class="flex items-center gap-2.5">
                  <span class="text-[13.5px] font-semibold">
                    {$_('nus3Convert.candidateTitle', { values: { rank: c.rank } })}
                  </span>
                  <span class="chip border-transparent bg-accbg text-accink">
                    {$_('nus3Convert.score', { values: { score: c.score.toFixed(1) } })}
                  </span>
                  <span class="tabular-nums text-xs text-muted-foreground">
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
                  <!-- Loop region shading -->
                  <rect
                    x={((c.loopStart) / dur) * bars * 3}
                    y="0"
                    width={((c.loopEnd - c.loopStart) / dur) * bars * 3}
                    height="120"
                    fill="rgb(var(--acc) / .12)"
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
                      fill={insideLoop ? 'rgb(var(--acc))' : 'rgb(var(--mut))'}
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
                        stroke={active ? 'rgb(var(--acc))' : 'rgb(var(--ink))'}
                        stroke-width={active ? 2 : 1}
                      />
                      <line
                        x1={xE} x2={xE} y1="4" y2="116"
                        stroke={active ? 'rgb(var(--acc))' : 'rgb(var(--ink))'}
                        stroke-width={active ? 2 : 1}
                        stroke-dasharray="3 2"
                      />
                    </g>
                  {/each}

                  <!-- Selected marker dots -->
                  <circle cx={(c.loopStart / dur) * bars * 3} cy="10" r="5" fill="rgb(var(--acc))" />
                  <circle cx={(c.loopEnd / dur) * bars * 3} cy="10" r="5" fill="rgb(var(--acc))" />
                </svg>

                <!-- Time labels -->
                <div class="absolute bottom-0 left-0 right-0 flex justify-between pointer-events-none">
                  <span class="tabular-nums text-[10px] text-muted-foreground">0:00</span>
                  {#each Array.from({ length: Math.floor(dur / 60) }, (_, i) => i + 1) as min}
                    <span class="tabular-nums text-[10px] text-muted-foreground">{min}:00</span>
                  {/each}
                  <span class="tabular-nums text-[10px] text-muted-foreground">
                    {Math.floor(dur / 60)}:{String(Math.floor(dur % 60)).padStart(2, '0')}
                  </span>
                </div>
              </div>

              <!-- Transport bar -->
              <div class="flex items-center gap-2 rounded-lg border border-line2 px-2.5 py-1.5">
                <button
                  onclick={() => previewPlaying ? stopPreview() : playLoopPreview()}
                  disabled={previewLoading}
                  class="btn-primary min-w-[110px]"
                >
                  {#if previewLoading}
                    <RefreshCw size={13} class="animate-spin" />
                  {:else}
                    <Play size={13} />
                  {/if}
                  {previewPlaying ? 'Stop' : previewLoading ? 'Loading...' : $_('nus3Convert.loopPreview')}
                </button>
                <div class="flex-1"></div>
                <span class="tabular-nums text-xs text-muted-foreground">{c.loopLengthStr}</span>
              </div>
            </div>
          {:else}
            <!-- No loop points found: only end-to-end is available -->
            <div class="grid min-h-[260px] flex-1 place-items-center rounded-xl border border-line2 p-6">
              <div class="flex max-w-[620px] flex-col items-center gap-4 text-center">
                <AudioWaveform size={20} class="text-dim" />
                <div class="flex flex-col gap-1">
                  <p class="text-[13.5px] font-semibold">{$_('nus3Convert.noLoopTitle')}</p>
                  <p class="text-[13px] text-muted-foreground">{$_('nus3Convert.noLoopHint')}</p>
                </div>
                <div class="flex flex-wrap items-center justify-center gap-2.5 pt-1">
                  <button
                    onclick={openSettings}
                    class="btn whitespace-nowrap"
                  >
                    <Settings size={15} />
                    {$_('nus3Convert.settingsTitle')}
                  </button>
                  <button
                    onclick={reanalyzeCurrent}
                    disabled={reanalyzing || converting}
                    class="btn whitespace-nowrap"
                  >
                    <RefreshCw size={15} class={reanalyzing ? 'animate-spin' : ''} />
                    {reanalyzing ? $_('nus3Convert.reanalyzing') : $_('nus3Convert.reanalyze')}
                  </button>
                  <button
                    onclick={rejectCurrent}
                    disabled={converting}
                    class="btn-danger whitespace-nowrap"
                  >
                    {$_('nus3Convert.rejectAll')}
                  </button>
                </div>
              </div>
            </div>
          {/if}

          {#if !currentTrackAnalyzing && currentCandidates.length > 0}
            <div class="flex min-h-0 flex-1 flex-col overflow-hidden rounded-xl border border-border">
              <div class="flex shrink-0 items-center justify-between border-b border-line2 px-3 py-1.5">
                <div class="flex items-center gap-2">
                  <span class="text-[13px] font-semibold">
                    {$_('nus3Convert.candidateCount', { values: { count: currentCandidates.length } })}
                  </span>
                  <span class="chip">
                    {$_('nus3Convert.clickToPreview')}
                  </span>
                </div>
                <span class="tabular-nums text-[11px] text-muted-foreground">{$_('nus3Convert.sortedBy')}</span>
              </div>

              <div class="min-h-0 flex-1 overflow-auto">
                <table class="w-full border-collapse text-[12px]">
                  <thead class="sticky top-0">
                    <tr class="bg-card">
                      {#each [$_('nus3Convert.colRank'), $_('nus3Convert.colScore'), $_('nus3Convert.colLoopStart'), $_('nus3Convert.colLoopEnd'), $_('nus3Convert.colLength'), $_('nus3Convert.colNoteDist'), $_('nus3Convert.colRms'), $_('nus3Convert.colSpec')] as header}
                        <th class="px-2 py-1.5 text-left text-[10.5px] font-semibold uppercase tracking-[.1em] text-dim">
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
                        class="cursor-pointer border-t border-line2 transition-colors duration-100 {isSel ? 'bg-accbg text-accink' : 'hover:bg-accent'} {belowThreshold ? 'opacity-45' : ''}"
                      >
                        <td class="relative px-2 py-1">
                          {#if isSel}
                            <span class="absolute bottom-0 left-0 top-0 w-0.5 bg-primary"></span>
                          {/if}
                          <span class="tabular-nums text-[11px] {isSel ? 'font-semibold' : 'font-medium'}">
                            {String(cand.rank).padStart(2, '0')}
                          </span>
                        </td>
                        <td class="px-2 py-1">
                          <div class="flex items-center gap-1.5">
                            <div class="h-1 w-[50px] overflow-hidden rounded-sm bg-muted">
                              <div class="h-full bg-primary" style="width: {cand.score}%;"></div>
                            </div>
                            <span class="tabular-nums text-[11px] {isSel ? 'font-semibold' : 'font-medium'}">{cand.score.toFixed(1)}</span>
                          </div>
                        </td>
                        <td class="px-2 py-1 tabular-nums text-[11px]">{cand.loopStartStr}</td>
                        <td class="px-2 py-1 tabular-nums text-[11px]">{cand.loopEndStr}</td>
                        <td class="px-2 py-1 tabular-nums text-[11px]">{cand.loopLengthStr}</td>
                        <td class="px-2 py-1 tabular-nums text-[11px]">{(cand.noteDistance ?? 0).toFixed(4)}</td>
                        <td class="px-2 py-1 tabular-nums text-[11px]">{cand.rmsDelta.toFixed(1)}</td>
                        <td class="px-2 py-1 tabular-nums text-[11px]">{(cand.spectralSim * 100).toFixed(0)}%</td>
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
        <div class="shrink-0 border-b border-line2 px-[22px] py-3">
          <div class="flex items-center gap-4">
            <div class="flex min-w-0 flex-1 flex-col gap-1">
              <span class="text-[10.5px] font-semibold uppercase tracking-[.1em] text-dim">
                {$_('nus3Convert.kicker2')}
              </span>
              <h2 class="text-[15px] font-semibold tracking-[-.01em]">
                {$_('nus3Convert.reviewTitle', { values: { count: convertedTracks.length } })}
              </h2>
              <p class="max-w-[720px] text-[12.5px] text-muted-foreground">
                {$_('nus3Convert.reviewSubtitle')}
              </p>
            </div>
            <div class="flex shrink-0 items-center gap-2">
              <button
                onclick={openAcceptModal}
                disabled={writing || convertedTracks.length === 0}
                class="btn-primary"
              >
                <CheckCircle size={14} />
                {writing ? $_('nus3Convert.writing') : $_('nus3Convert.acceptWrite')}
              </button>
            </div>
          </div>
        </div>

        <div class="flex min-h-0 flex-1 flex-col gap-2 overflow-auto px-[22px] py-3.5">
          {#if convertedTracks.length === 0}
            <div class="grid min-h-[240px] place-items-center">
              <p class="max-w-[360px] text-center text-[13px] text-muted-foreground">{$_('nus3Convert.reviewEmpty')}</p>
            </div>
          {:else}
          <div class="flex items-center gap-2.5">
            <span class="text-[10.5px] font-semibold uppercase tracking-[.1em] text-dim">
              {$_('nus3Convert.reviewQueue')}
            </span>
            <span class="chip border-transparent bg-accbg text-accink">
              {$_('nus3Convert.keeping', { values: { count: convertedTracks.length } })}
            </span>
          </div>

          {#each convertedTracks as track}
            {@const meta = conversions[track.id]}
            {@const cand = meta?.candidate}
            {@const realPeaks = waveformPeaks.get(track.id) ?? []}
            {@const miniPeaks = realPeaks.length > 0 ? downsamplePeaks(realPeaks, 60) : generatePeaks(60, track.id.charCodeAt(0))}
            <div class="rounded-xl border border-line2 p-3">
              <div class="flex items-center gap-3.5">
                <button
                  onclick={() => playReviewPreview(track)}
                  disabled={reviewLoadingId === track.id}
                  class="btn w-[27px] px-0 shrink-0"
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
                      <span class="chip h-[18px] border-transparent bg-accbg text-accink">
                        #{cand.rank} &middot; {cand.score.toFixed(1)}
                      </span>
                    {/if}
                  </div>
                  {#if cand}
                    <span class="tabular-nums text-xs text-muted-foreground">
                      {cand.loopStartStr} &rarr; {cand.loopEndStr} &middot; loop {cand.loopLengthStr}
                    </span>
                  {:else}
                    <span class="tabular-nums text-xs text-muted-foreground">{$_('nus3Convert.endToEndLoop')}</span>
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
                        fill="rgb(var(--acc) / .12)"
                      />
                      {#each miniPeaks as p, i}
                        {@const h = 3 + p * 28}
                        {@const y = (36 - h) / 2}
                        {@const x = i * 2}
                        {@const inside = x / 120 >= sFrac && x / 120 <= eFrac}
                        <rect
                          x={x} y={y} width="1.2" height={h} rx="0.6"
                          fill={inside ? 'rgb(var(--acc))' : 'rgb(var(--mut))'}
                          opacity={inside ? 0.9 : 0.35}
                        />
                      {/each}
                      <line
                        x1={sFrac * 120} x2={sFrac * 120} y1="2" y2="34"
                        stroke="rgb(var(--acc))" stroke-width="1.4"
                      />
                      <line
                        x1={eFrac * 120} x2={eFrac * 120} y1="2" y2="34"
                        stroke="rgb(var(--acc))" stroke-width="1.4" stroke-dasharray="2 2"
                      />
                    </svg>
                  </div>
                {/if}

                <!-- Reject (delete nus3, convert again) -->
                <button
                  onclick={() => rejectFromReview(track)}
                  title={$_('nus3Convert.removeConverted')}
                  class="btn-danger w-[27px] shrink-0 px-0"
                >
                  <Trash2 size={13} />
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
