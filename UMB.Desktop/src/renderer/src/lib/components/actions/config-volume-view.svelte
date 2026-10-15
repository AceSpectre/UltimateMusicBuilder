<script lang="ts">
  import Volume2 from '@lucide/svelte/icons/volume-2'
  import RefreshCw from '@lucide/svelte/icons/refresh-cw'
  import Play from '@lucide/svelte/icons/play'
  import Square from '@lucide/svelte/icons/square'
  import AlertTriangle from '@lucide/svelte/icons/alert-triangle'
  import Activity from '@lucide/svelte/icons/activity'
  import { untrack } from 'svelte'
  import { _ } from 'svelte-i18n'
  import { logStore } from '$lib/stores/logs.svelte'
  import { guardLeave, trackUnsaved } from '$lib/stores/unsaved.svelte'
  import { modsStore } from '$lib/stores/mods.svelte'
  import { settingsStore } from '$lib/stores/settings.svelte'
  import { createSeriesLoader } from '$lib/series-loader'
  import { DB_INPUT, dbToGain, formatBankDb, formatDb, parseDb } from '$lib/volume'
  import ActionHeader from '$lib/components/ui/action-header.svelte'
  import EmptyState from '$lib/components/ui/empty-state.svelte'
  import MissingFileNote from '$lib/components/ui/missing-file-note.svelte'
  import SaveButton from '$lib/components/ui/save-button.svelte'
  import SeriesPicker from '$lib/components/ui/series-picker.svelte'
  import type { ModInfo, ModSeriesInfo, VolumeConfigData, VolumeRowItem, VolumeProgress } from '$lib/types/electron'

  let { activeMod }: { activeMod: ModInfo | null } = $props()

  let loading = $state(false)
  // `loadingConfig` = fast cache-only read; `analyzing` = FFmpeg LUFS analysis (slow, user-triggered).
  let loadingConfig = $state(false)
  let analyzing = $state(false)
  let analyzeProgress = $state<VolumeProgress | null>(null)
  let series = $state<ModSeriesInfo[]>([])
  let selectedPath = $state<string | null>(null)
  let data = $state<VolumeConfigData | null>(null)
  let rows = $state<VolumeRowItem[]>([])
  let saveState = $state<'idle' | 'saving' | 'saved'>('idle')
  let baseline = new Map<number, number>()
  let seriesDb = $state(0)
  let baselineSeriesDb = $state(0)
  const seriesLoader = createSeriesLoader((v) => (loading = v))
  const thClass = 'px-3 py-2 kicker'
  const dbInputClass = 'field w-[88px] text-right tabular-nums text-[12px]'

  // Web Audio preview
  let audioCtx: AudioContext | null = null
  let sourceNode: AudioBufferSourceNode | null = null
  let gainNode: GainNode | null = null
  let playingIndex = $state<number | null>(null)
  let previewLoadingIndex = $state<number | null>(null)
  const bufferCache = new Map<string, AudioBuffer>()

  const seriesPath = $derived(selectedPath)
  const selectedSeries = $derived(series.find((e) => e.path === selectedPath) ?? null)
  const seriesDbDirty = $derived(Math.abs(seriesDb - baselineSeriesDb) > 0.0001)
  const isDirty = $derived(seriesDbDirty || rows.some((r) => Math.abs((baseline.get(r.originalIndex) ?? r.trackDb) - r.trackDb) > 0.0001))
  // Highlighted when no LUFS cache exists yet.
  const canAnalyze = $derived(!!data && !!selectedPath && data.ffmpegAvailable && !analyzing && !loadingConfig)
  const needsAnalysis = $derived(!!data && !data.lufsCacheExists && rows.some((r) => !r.isVanilla))

  // Total boost the build applies: global + series + auto-gain + track.
  function effectiveDb(row: VolumeRowItem): number {
    return (data?.globalDb ?? 0) + seriesDb + row.autoGainDb + row.trackDb
  }

  async function loadSeries(modPath: string | null) {
    if (!modPath) {
      seriesLoader.invalidate()
      series = []
      selectedPath = null
      return
    }

    const next = await seriesLoader.load(modPath)
    if (next === null) return
    series = next
    // Deliberately do NOT auto-select a series — the user must pick one.
    if (selectedPath && !next.some((e) => e.path === selectedPath)) {
      selectedPath = null
    }
  }

  async function loadConfig(path: string | null, analyze = false) {
    stopPreview()
    bufferCache.clear()
    if (!path) {
      data = null
      rows = []
      return
    }

    if (analyze) { analyzing = true; analyzeProgress = null }
    else loadingConfig = true
    saveState = 'idle'
    try {
      const result = await window.electron.umb.loadVolumeConfig(path, analyze)
      if (path !== selectedPath) return
      data = result
      rows = result.items.map((item) => ({ ...item }))
      baseline = new Map(result.items.map((item) => [item.originalIndex, item.trackDb]))
      seriesDb = baselineSeriesDb = result.seriesDb
    } catch (err) {
      logStore.log('error', `Volume analysis failed: ${err instanceof Error ? err.message : String(err)}`)
      data = null
      rows = []
    } finally {
      analyzing = false
      analyzeProgress = null
      loadingConfig = false
    }
  }

  function selectSeries(path: string) {
    if (path === selectedPath) return
    guardLeave(() => {
      selectedPath = path
      void loadConfig(path)
    })
  }

  function handleAnalyze() {
    if (!selectedPath) return
    void loadConfig(selectedPath, true)
  }

  function handleReload() {
    void loadSeries(activeMod?.path ?? null)
    if (seriesPath) void loadConfig(seriesPath)
  }

  // Keeps a playing preview at the edited loudness.
  function refreshPreviewGain() {
    const playing = rows.find((r) => r.originalIndex === playingIndex)
    if (playing && gainNode) gainNode.gain.value = dbToGain(effectiveDb(playing))
  }

  function onBoostEdited() {
    refreshPreviewGain()
    if (saveState === 'saved') saveState = 'idle'
  }

  function onTrackDbInput(row: VolumeRowItem, raw: string) {
    const next = parseDb(raw)
    rows = rows.map((r) => (r.originalIndex === row.originalIndex ? { ...r, trackDb: next } : r))
    onBoostEdited()
  }

  function onSeriesDbInput(raw: string) {
    seriesDb = parseDb(raw)
    onBoostEdited()
  }

  async function handleSave() {
    if (!data || !seriesPath) return
    saveState = 'saving'
    try {
      await window.electron.umb.saveVolumeConfig(
        seriesPath,
        rows.map((r) => ({ originalIndex: r.originalIndex, db: r.trackDb })),
        seriesDbDirty ? seriesDb : undefined
      )
      baseline = new Map(rows.map((r) => [r.originalIndex, r.trackDb]))
      baselineSeriesDb = seriesDb
      saveState = 'saved'
    } catch (err) {
      logStore.log('error', `Save failed: ${err instanceof Error ? err.message : String(err)}`)
      saveState = 'idle'
    }
  }

  function stopPreview() {
    if (sourceNode) {
      try { sourceNode.onended = null; sourceNode.stop() } catch { /* already stopped */ }
      try { sourceNode.disconnect() } catch { /* ignore */ }
      sourceNode = null
    }
    if (gainNode) {
      try { gainNode.disconnect() } catch { /* ignore */ }
      gainNode = null
    }
    playingIndex = null
  }

  async function togglePreview(row: VolumeRowItem) {
    if (playingIndex === row.originalIndex) {
      stopPreview()
      return
    }
    if (!seriesPath) return

    stopPreview()
    previewLoadingIndex = row.originalIndex

    try {
      let buffer = bufferCache.get(row.filename)
      if (!buffer) {
        const dataUrl = await window.electron.umb.decodeTrackPreview(seriesPath, row.filename)
        if (!dataUrl) {
          logStore.log('warn', `Could not decode "${row.title}" for preview.`)
          return
        }
        const arrayBuf = await (await fetch(dataUrl)).arrayBuffer()
        audioCtx ??= new AudioContext()
        buffer = await audioCtx.decodeAudioData(arrayBuf)
        bufferCache.set(row.filename, buffer)
      }

      audioCtx ??= new AudioContext()
      if (audioCtx.state === 'suspended') await audioCtx.resume()

      gainNode = audioCtx.createGain()
      gainNode.gain.value = dbToGain(effectiveDb(row))
      sourceNode = audioCtx.createBufferSource()
      sourceNode.buffer = buffer
      sourceNode.connect(gainNode).connect(audioCtx.destination)
      sourceNode.onended = () => {
        if (playingIndex === row.originalIndex) stopPreview()
      }
      sourceNode.start()
      playingIndex = row.originalIndex
    } catch (err) {
      logStore.log('error', `Preview failed: ${err instanceof Error ? err.message : String(err)}`)
      stopPreview()
    } finally {
      previewLoadingIndex = null
    }
  }

  $effect(() => {
    const modPath = activeMod?.path ?? null
    untrack(() => {
      stopPreview()
      bufferCache.clear()
      selectedPath = null
      data = null
      rows = []
      void loadSeries(modPath)
    })
  })

  // Another view (Manage Songs) can ask us to open a specific series. Declared after the
  // activeMod reset effect so its selection wins on mount.
  $effect(() => {
    const pending = modsStore.pendingConfigVolumePath
    if (!pending) return
    untrack(() => {
      modsStore.pendingConfigVolumePath = null
      selectedPath = pending
      void loadConfig(pending)
    })
  })

  // Settings changed (global boost, vanilla rows): reload the open series, keeping unsaved edits.
  $effect(() => {
    if (!settingsStore.version) return
    untrack(async () => {
      const path = selectedPath
      if (!path || analyzing) return
      const edits = new Map(rows.map((r) => [r.originalIndex, r.trackDb]))
      const editedSeriesDb = seriesDb
      await loadConfig(path)
      if (path !== selectedPath) return
      rows = rows.map((r) => ({ ...r, trackDb: edits.get(r.originalIndex) ?? r.trackDb }))
      seriesDb = editedSeriesDb
    })
  })

  $effect(() => {
    const unsub = window.electron.umb.subscribeVolumeProgress((p: VolumeProgress) => {
      analyzeProgress = p
    })
    return unsub
  })

  $effect(() => () => {
    stopPreview()
    if (audioCtx) {
      void audioCtx.close()
      audioCtx = null
    }
  })

  trackUnsaved(() => isDirty, handleSave)
</script>

{#snippet boost(db: number, caption: string, hint: string)}
  <div class="flex flex-col items-end text-muted-foreground" title={hint}>
    <span>{formatDb(db)}</span>
    <span class="font-sans text-[11px]">{caption}</span>
  </div>
{/snippet}

<div class="flex-1 overflow-hidden">
  <div class="flex h-full min-h-0">
    <SeriesPicker
      {activeMod}
      {series}
      {loading}
      spinning={loading || analyzing || loadingConfig}
      activePath={selectedPath}
      onSelect={selectSeries}
      onReload={handleReload}
    />

    <section class="surface-grain flex h-full min-h-0 min-w-0 flex-1 flex-col overflow-hidden bg-card">
      <ActionHeader title={$_('configVolume.title')} subtitle={selectedSeries?.name ?? ''}>
          {#if data && rows.length > 0}
            <span class="chip">
              {$_('configVolume.globalLabel', { values: { value: formatDb(data.globalDb) } })}
            </span>
            <span class="chip">
              {$_('configVolume.targetLabel', { values: { value: data.targetLufs.toFixed(1) } })}
            </span>
          {/if}
          {#if canAnalyze}
            <button
              onclick={handleAnalyze}
              title={data?.ffmpegAvailable ? $_('configVolume.analyzeHint') : $_('configVolume.ffmpegWarning')}
              class="{needsAnalysis ? 'btn-primary' : 'btn'} shrink-0"
            >
              <Activity size={14} />
              {needsAnalysis ? $_('configVolume.analyze') : $_('configVolume.reanalyze')}
            </button>
          {/if}
          <SaveButton
            {saveState}
            onclick={handleSave}
            disabled={!data || rows.length === 0 || !isDirty || saveState === 'saving'}
            save={$_('configVolume.save')}
          />
      </ActionHeader>

      {#if analyzing}
        <div class="grid h-full min-h-[320px] place-items-center">
          <div class="flex w-full max-w-[400px] flex-col items-center gap-4 px-6 text-center">
            {#if analyzeProgress}
              {@const pct = Math.round((analyzeProgress.completed / analyzeProgress.total) * 100)}
              <Activity size={28} class="animate-pulse text-primary" />
              <p class="text-[13.5px] font-semibold">
                {$_('configVolume.analyzingProgress', { values: { completed: analyzeProgress.completed, total: analyzeProgress.total } })}
              </p>
              <div class="h-1.5 w-full overflow-hidden rounded-sm bg-muted">
                <div class="h-full bg-primary transition-all duration-300 ease-out" style="width: {pct}%;"></div>
              </div>
              <p class="flex items-center gap-1.5 text-[12px] text-muted-foreground">
                <span class="font-semibold">{pct}%</span>
                <span class="mx-1">·</span>
                <span class="truncate">{analyzeProgress.currentFile}</span>
              </p>
            {:else}
              <RefreshCw size={28} class="animate-spin text-primary" />
              <p class="text-[13.5px] font-semibold">{$_('configVolume.analyzing')}</p>
            {/if}
          </div>
        </div>
      {:else if !selectedSeries}
        <EmptyState body={$_('common.selectSeries')}>
          {#snippet icon()}<Volume2 size={20} />{/snippet}
        </EmptyState>
      {:else if loadingConfig}
        <div class="grid h-full min-h-[320px] place-items-center">
          <div class="flex flex-col items-center gap-4 text-center">
            <RefreshCw size={28} class="animate-spin text-primary" />
            <p class="text-[13.5px] font-semibold">{$_('configVolume.loadingTracks')}</p>
          </div>
        </div>
      {:else if rows.length === 0}
        <div class="grid h-full min-h-[320px] place-items-center">
          <p class="max-w-[340px] px-6 text-center text-[13px] text-muted-foreground">{$_('configVolume.empty')}</p>
        </div>
      {:else}
        <div class="min-h-0 flex-1 overflow-auto px-[22px] py-3.5">
          <p class="mb-3 max-w-[820px] text-[12.5px] leading-relaxed text-muted-foreground">{$_('configVolume.intro')}</p>

          <div class="mb-3 flex items-center gap-3 rounded-xl border border-line2 px-[13px] py-2">
            <label for="series-db" class="text-[12.5px] font-semibold">{$_('configVolume.seriesDb')}</label>
            <input
              id="series-db"
              type="number"
              {...DB_INPUT}
              value={seriesDb}
              oninput={(e) => onSeriesDbInput(e.currentTarget.value)}
              class={dbInputClass}
            />
            <span class="text-[12px] text-muted-foreground">{$_('configVolume.seriesDbHint')}</span>
          </div>

          {#if data && !data.ffmpegAvailable}
            <div class="mb-3 flex items-start gap-2 rounded-lg border border-line2 px-3 py-2 text-[12px] text-warn">
              <AlertTriangle size={15} class="mt-0.5 shrink-0" />
              <span>{$_('configVolume.ffmpegWarning')}</span>
            </div>
          {:else if needsAnalysis}
            <div class="mb-3 flex items-start gap-2 rounded-lg border border-line2 px-3 py-2 text-[12px] text-muted-foreground">
              <Activity size={15} class="mt-0.5 shrink-0" />
              <span>{$_('configVolume.notAnalyzed')}</span>
            </div>
          {/if}

          <div class="overflow-hidden rounded-xl border border-border">
            <table class="w-full border-collapse text-[12.5px]">
              <thead class="sticky top-0">
                <tr class="bg-card">
                  <th class="w-full text-left {thClass}">{$_('configVolume.colTrack')}</th>
                  <th class="whitespace-nowrap text-right {thClass}">{$_('configVolume.colMeasured')}</th>
                  <th class="whitespace-nowrap text-right {thClass}">{$_('configVolume.colAutoGain')}</th>
                  <th class="whitespace-nowrap text-right {thClass}" title={$_('configVolume.colNormalisedHint')}>{$_('configVolume.colNormalised')}</th>
                  <th class="whitespace-nowrap text-right {thClass}">{$_('configVolume.colOverride')}</th>
                  <th class="whitespace-nowrap text-right {thClass}" title={$_('configVolume.colFinalHint')}>{$_('configVolume.colFinal')}</th>
                  <th class="whitespace-nowrap text-right {thClass}"></th>
                </tr>
              </thead>
              <tbody>
                {#each rows as row (row.originalIndex)}
                  {@const isPlaying = playingIndex === row.originalIndex}
                  {@const isPreviewLoading = previewLoadingIndex === row.originalIndex}
                  <tr class="border-t border-line2 transition-colors duration-100 hover:bg-accent">
                    <td class="w-full max-w-0 px-3 py-2">
                      <div class="flex min-w-0 flex-col">
                        <span class="truncate font-semibold">{row.title}</span>
                        <span class="truncate text-[11px] text-muted-foreground">{row.isVanilla ? `${$_('orderTracks.vanillaTag')} · ` : ''}{row.filename}</span>
                        <MissingFileNote missing={row.missing} />
                      </div>
                    </td>
                    <td class="whitespace-nowrap px-3 py-2 text-right tabular-nums text-[12px]">
                      {row.hasMeasurement ? `${row.measuredLufs.toFixed(1)} LUFS` : '—'}
                    </td>
                    <td class="whitespace-nowrap px-3 py-2 text-right">
                      <div class="flex items-center justify-end gap-1.5">
                        {#if row.wasClamped}
                          <span title={$_('configVolume.clampWarning')} class="text-warn">
                            <AlertTriangle size={13} />
                          </span>
                        {/if}
                        <span class="tabular-nums text-[12px]">{row.hasMeasurement ? formatDb(row.autoGainDb) : '—'}</span>
                      </div>
                    </td>
                    <td class="whitespace-nowrap px-3 py-2 text-right tabular-nums text-[12px]">
                      {row.hasMeasurement || row.isVanilla ? formatBankDb(row.bankBaseDb + row.autoGainDb) : '—'}
                    </td>
                    <td class="whitespace-nowrap px-3 py-2 text-right">
                      <div class="flex items-center justify-end gap-2 tabular-nums text-[12px]">
                        {@render boost(data?.globalDb ?? 0, $_('configVolume.globalCaption'), $_('configVolume.globalHint'))}
                        <span class="text-muted-foreground">+</span>
                        {@render boost(seriesDb, $_('configVolume.seriesCaption'), $_('configVolume.seriesDbHint'))}
                        <span class="text-muted-foreground">+</span>
                        <input
                          type="number"
                          {...DB_INPUT}
                          value={row.trackDb}
                          oninput={(e) => onTrackDbInput(row, e.currentTarget.value)}
                          title={$_('configVolume.trackHint')}
                          class={dbInputClass}
                        />
                      </div>
                    </td>
                    <td class="whitespace-nowrap px-3 py-2 text-right tabular-nums text-[12px] font-semibold">{formatBankDb(row.bankBaseDb + effectiveDb(row))}</td>
                    <td class="whitespace-nowrap px-3 py-2 text-right">
                      <button
                        onclick={() => togglePreview(row)}
                        disabled={isPreviewLoading}
                        title={isPlaying ? $_('configVolume.stop') : $_('configVolume.play')}
                        class="btn-icon"
                      >
                        {#if isPreviewLoading}
                          <RefreshCw size={13} class="animate-spin" />
                        {:else if isPlaying}
                          <Square size={13} />
                        {:else}
                          <Play size={13} />
                        {/if}
                      </button>
                    </td>
                  </tr>
                {/each}
              </tbody>
            </table>
          </div>
        </div>
      {/if}
    </section>
  </div>
</div>
