<script lang="ts">
  import { Volume2, Folder, RefreshCw, Save, Play, Square, AlertTriangle } from '@lucide/svelte'
  import { untrack } from 'svelte'
  import { _ } from 'svelte-i18n'
  import { modsStore } from '$lib/stores/mods.svelte'
  import { logStore } from '$lib/stores/logs.svelte'
  import type { ModInfo, ModSeriesInfo, VolumeConfigData, VolumeRowItem } from '$lib/types/electron'

  let { activeMod }: { activeMod: ModInfo | null } = $props()

  let loading = $state(false)
  let analyzing = $state(false)
  let series = $state<ModSeriesInfo[]>([])
  let data = $state<VolumeConfigData | null>(null)
  let rows = $state<VolumeRowItem[]>([])
  let saveState = $state<'idle' | 'saving' | 'saved'>('idle')
  let baseline = new Map<number, number>()
  let loadToken = 0

  // ── Web Audio preview (mirrors the Avalonia VolumeSampleProvider) ──
  let audioCtx: AudioContext | null = null
  let sourceNode: AudioBufferSourceNode | null = null
  let gainNode: GainNode | null = null
  let playingIndex = $state<number | null>(null)
  let previewLoadingIndex = $state<number | null>(null)
  const bufferCache = new Map<string, AudioBuffer>()

  const seriesPath = $derived(modsStore.activeSeriesPath)
  const selectedSeries = $derived(series.find((e) => e.path === modsStore.activeSeriesPath) ?? null)
  const isDirty = $derived(rows.some((r) => Math.abs((baseline.get(r.originalIndex) ?? r.userOverride) - r.userOverride) > 0.0001))

  function effective(row: VolumeRowItem): number {
    const global = data?.globalVolumeMultiplier ?? 1
    return global * row.autoGain * row.userOverride
  }

  function log(level: 'info' | 'warn' | 'error', message: string) {
    logStore.push({ timestamp: new Date().toLocaleTimeString('en-GB', { hour12: false }), level, message })
  }

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
      const next = await window.electron.umb.listModSeries(modPath)
      if (token !== loadToken) return
      series = next
      if (!next.some((e) => e.path === modsStore.activeSeriesPath)) {
        modsStore.activeSeriesPath = next[0]?.path ?? null
      }
    } finally {
      if (token === loadToken) loading = false
    }
  }

  async function loadConfig(path: string | null) {
    stopPreview()
    bufferCache.clear()
    if (!path) {
      data = null
      rows = []
      return
    }

    analyzing = true
    saveState = 'idle'
    try {
      const result = await window.electron.umb.loadVolumeConfig(path)
      if (path !== modsStore.activeSeriesPath) return
      data = result
      rows = result.items.map((item) => ({ ...item }))
      baseline = new Map(result.items.map((item) => [item.originalIndex, item.userOverride]))
    } catch (err) {
      log('error', `Volume analysis failed: ${err instanceof Error ? err.message : String(err)}`)
      data = null
      rows = []
    } finally {
      analyzing = false
    }
  }

  function selectSeries(path: string) {
    if (path === modsStore.activeSeriesPath) return
    modsStore.activeSeriesPath = path
    void loadConfig(path)
  }

  function handleReload() {
    void loadSeries(activeMod?.path ?? null)
    if (seriesPath) void loadConfig(seriesPath)
  }

  function clampOverride(value: number): number {
    if (Number.isNaN(value)) return 1
    return Math.max(0, Math.min(10, value))
  }

  function onOverrideInput(row: VolumeRowItem, raw: string) {
    const next = clampOverride(parseFloat(raw))
    rows = rows.map((r) => (r.originalIndex === row.originalIndex ? { ...r, userOverride: next } : r))
    // Live update: if this row is currently playing, retarget the gain node.
    if (playingIndex === row.originalIndex && gainNode) {
      gainNode.gain.value = (data?.globalVolumeMultiplier ?? 1) * row.autoGain * next
    }
    if (saveState === 'saved') saveState = 'idle'
  }

  async function handleSave() {
    if (!data || !seriesPath) return
    saveState = 'saving'
    try {
      await window.electron.umb.saveVolumeConfig(
        seriesPath,
        rows.map((r) => ({ originalIndex: r.originalIndex, volume: r.userOverride }))
      )
      baseline = new Map(rows.map((r) => [r.originalIndex, r.userOverride]))
      saveState = 'saved'
    } catch (err) {
      log('error', `Save failed: ${err instanceof Error ? err.message : String(err)}`)
      saveState = 'idle'
    }
  }

  // ── preview playback ──

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
          log('warn', `Could not decode "${row.title}" for preview.`)
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
      gainNode.gain.value = effective(row)
      sourceNode = audioCtx.createBufferSource()
      sourceNode.buffer = buffer
      sourceNode.connect(gainNode).connect(audioCtx.destination)
      sourceNode.onended = () => {
        if (playingIndex === row.originalIndex) stopPreview()
      }
      sourceNode.start()
      playingIndex = row.originalIndex
    } catch (err) {
      log('error', `Preview failed: ${err instanceof Error ? err.message : String(err)}`)
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
      data = null
      rows = []
      void (async () => {
        await loadSeries(modPath)
        if (modsStore.activeSeriesPath) await loadConfig(modsStore.activeSeriesPath)
      })()
    })
  })

  // Tear down audio when the view is destroyed.
  $effect(() => () => {
    stopPreview()
    if (audioCtx) {
      void audioCtx.close()
      audioCtx = null
    }
  })
</script>

<div class="flex-1 overflow-hidden">
  <div class="flex h-full min-h-0">
    <!-- Series sidebar -->
    <section class="flex h-full min-h-0 w-[280px] shrink-0 flex-col border border-border bg-card overflow-hidden">
      <div class="gradient-strip h-[3px] shrink-0"></div>
      <div class="shrink-0 border-b border-border px-4 py-3">
        <div class="flex items-center gap-2">
          <div
            class="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl border border-border"
            style="background: linear-gradient(135deg, hsl(var(--gradient-from) / .13), hsl(var(--gradient-to) / .16)); color: hsl(var(--gradient-from));"
          >
            <Folder size={18} />
          </div>
          <div class="min-w-0 flex-1">
            <h2 class="text-sm font-semibold">{$_('configVolume.seriesHeading')}</h2>
            <p class="truncate text-[12.5px] text-muted-foreground">
              {activeMod?.name ?? $_('configVolume.selectModSubtitle')}
            </p>
          </div>
          <span class="shrink-0 text-[12px] text-muted-foreground">{series.length}</span>
          <button
            onclick={handleReload}
            class="inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-lg border border-input bg-background transition-colors hover:bg-muted"
            title={$_('configVolume.reload')}
          >
            <RefreshCw size={14} class={loading || analyzing ? 'animate-spin' : ''} />
          </button>
        </div>
      </div>

      <div class="min-h-0 flex-1 overflow-auto p-2">
        {#if !activeMod}
          <div class="rounded-xl border border-dashed border-border bg-background/70 px-3 py-8 text-center text-[13px] text-muted-foreground">
            {$_('configVolume.chooseMod')}
          </div>
        {:else if loading && series.length === 0}
          <div class="rounded-xl border border-dashed border-border bg-background/70 px-3 py-8 text-center text-[13px] text-muted-foreground">
            {$_('configVolume.loadingSeries')}
          </div>
        {:else if series.length === 0}
          <div class="rounded-xl border border-dashed border-border bg-background/70 px-3 py-8 text-center text-[13px] text-muted-foreground">
            {$_('configVolume.noSeries')}
          </div>
        {:else}
          <div class="grid gap-1.5">
            {#each series as item}
              {@const isActive = modsStore.activeSeriesPath === item.path}
              <button
                onclick={() => selectSeries(item.path)}
                class="w-full rounded-lg border px-2 py-1.5 text-left transition-colors {isActive ? 'border-transparent' : 'border-border bg-background/70 hover:bg-muted'}"
                style={isActive
                  ? 'background: linear-gradient(135deg, hsl(var(--gradient-from) / .15), hsl(var(--gradient-to) / .18));'
                  : ''}
              >
                <div class="truncate text-[13.5px] font-semibold">{item.name}</div>
              </button>
            {/each}
          </div>
        {/if}
      </div>
    </section>

    <!-- Main content -->
    <section class="flex h-full min-h-0 min-w-0 flex-1 flex-col border border-border bg-card overflow-hidden">
      <div class="gradient-strip h-[3px] shrink-0"></div>

      <!-- Header -->
      <div class="shrink-0 border-b border-border px-5 py-3 flex items-center justify-between gap-4">
        <div class="flex min-w-0 items-center gap-3">
          <div
            class="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl border border-border"
            style="background: linear-gradient(135deg, hsl(var(--gradient-from) / .13), hsl(var(--gradient-to) / .16)); color: hsl(var(--gradient-from));"
          >
            <Volume2 size={18} />
          </div>
          <div class="min-w-0">
            <h2 class="truncate text-sm font-semibold">{$_('configVolume.title')}</h2>
            <p class="truncate text-[12.5px] text-muted-foreground">{selectedSeries?.name ?? ''}</p>
          </div>
        </div>

        <div class="flex items-center gap-2">
          {#if data && rows.length > 0}
            <span class="shrink-0 inline-flex h-[22px] items-center rounded-full border border-border bg-muted px-2 text-[11px] font-medium text-muted-foreground">
              {$_('configVolume.globalLabel', { values: { value: data.globalVolumeMultiplier.toFixed(2) } })}
            </span>
            <span class="shrink-0 inline-flex h-[22px] items-center rounded-full border border-border bg-muted px-2 text-[11px] font-medium text-muted-foreground">
              {$_('configVolume.targetLabel', { values: { value: data.targetLufs.toFixed(1) } })}
            </span>
          {/if}
          <button
            onclick={handleSave}
            class="shrink-0 inline-flex items-center gap-2 rounded-lg border border-input bg-background px-3 py-2 text-[12.5px] font-medium transition-colors hover:bg-muted disabled:cursor-not-allowed disabled:opacity-60"
            disabled={!data || rows.length === 0 || !isDirty || saveState === 'saving'}
          >
            <Save size={14} />
            {saveState === 'saving' ? $_('configVolume.saving') : saveState === 'saved' ? $_('configVolume.saved') : $_('configVolume.save')}
          </button>
        </div>
      </div>

      {#if analyzing}
        <div class="grid h-full min-h-[320px] place-items-center">
          <div class="flex flex-col items-center gap-4 text-center">
            <RefreshCw size={36} class="animate-spin" style="color: hsl(var(--gradient-from));" />
            <p class="text-sm font-semibold">{$_('configVolume.analyzing')}</p>
          </div>
        </div>
      {:else if !selectedSeries}
        <div class="grid h-full min-h-[320px] place-items-center">
          <div class="flex max-w-[340px] flex-col items-center gap-3 px-6 text-center">
            <div
              class="flex h-12 w-12 items-center justify-center rounded-2xl border border-border"
              style="background: linear-gradient(135deg, hsl(var(--gradient-from) / .13), hsl(var(--gradient-to) / .16)); color: hsl(var(--gradient-from));"
            >
              <Volume2 size={20} />
            </div>
            <p class="text-[13px] text-muted-foreground">{$_('configVolume.chooseSeries')}</p>
          </div>
        </div>
      {:else if rows.length === 0}
        <div class="grid h-full min-h-[320px] place-items-center">
          <p class="max-w-[340px] px-6 text-center text-[13px] text-muted-foreground">{$_('configVolume.empty')}</p>
        </div>
      {:else}
        <!-- Scrollable body -->
        <div class="min-h-0 flex-1 overflow-auto bg-background px-5 py-3">
          <p class="mb-3 max-w-[820px] text-[12.5px] leading-relaxed text-muted-foreground">{$_('configVolume.intro')}</p>

          {#if data && !data.ffmpegAvailable}
            <div class="mb-3 flex items-start gap-2 rounded-lg border px-3 py-2 text-[12px]" style="border-color: hsl(38 92% 50% / .4); background: hsl(38 92% 50% / .1); color: hsl(38 92% 35%);">
              <AlertTriangle size={15} class="mt-0.5 shrink-0" />
              <span>{$_('configVolume.ffmpegWarning')}</span>
            </div>
          {/if}

          <div class="overflow-hidden rounded-xl border border-border bg-card">
            <table class="w-full border-collapse text-[12.5px]">
              <thead class="sticky top-0">
                <tr style="background: hsl(var(--muted) / .5);">
                  <th class="px-3 py-2 text-left text-[10px] font-semibold uppercase tracking-wider text-muted-foreground">{$_('configVolume.colTrack')}</th>
                  <th class="px-3 py-2 text-right text-[10px] font-semibold uppercase tracking-wider text-muted-foreground">{$_('configVolume.colMeasured')}</th>
                  <th class="px-3 py-2 text-right text-[10px] font-semibold uppercase tracking-wider text-muted-foreground">{$_('configVolume.colAutoGain')}</th>
                  <th class="px-3 py-2 text-right text-[10px] font-semibold uppercase tracking-wider text-muted-foreground">{$_('configVolume.colOverride')}</th>
                  <th class="px-3 py-2 text-right text-[10px] font-semibold uppercase tracking-wider text-muted-foreground">{$_('configVolume.colFinal')}</th>
                  <th class="px-3 py-2 text-right text-[10px] font-semibold uppercase tracking-wider text-muted-foreground"></th>
                </tr>
              </thead>
              <tbody>
                {#each rows as row (row.originalIndex)}
                  {@const isPlaying = playingIndex === row.originalIndex}
                  {@const isPreviewLoading = previewLoadingIndex === row.originalIndex}
                  <tr class="border-t border-border transition-colors hover:bg-muted/40">
                    <td class="px-3 py-2">
                      <div class="flex min-w-0 flex-col">
                        <span class="truncate font-semibold">{row.title}</span>
                        <span class="truncate text-[11px] text-muted-foreground">{row.filename}</span>
                      </div>
                    </td>
                    <td class="px-3 py-2 text-right font-mono text-[12px]">
                      {row.hasMeasurement ? `${row.measuredLufs.toFixed(1)} LUFS` : '—'}
                    </td>
                    <td class="px-3 py-2 text-right">
                      <div class="flex items-center justify-end gap-1.5">
                        {#if row.wasClamped}
                          <span title={$_('configVolume.clampWarning')} style="color: hsl(38 92% 45%);">
                            <AlertTriangle size={13} />
                          </span>
                        {/if}
                        <span class="font-mono text-[12px]">{row.hasMeasurement ? `${row.autoGain.toFixed(2)}×` : '—'}</span>
                      </div>
                    </td>
                    <td class="px-3 py-2 text-right">
                      <input
                        type="number"
                        min="0"
                        max="10"
                        step="0.1"
                        value={row.userOverride}
                        oninput={(e) => onOverrideInput(row, e.currentTarget.value)}
                        class="h-8 w-[88px] rounded-lg border border-input bg-background px-2 text-right font-mono text-[12px] focus:outline-none focus:ring-2 focus:ring-ring"
                      />
                    </td>
                    <td class="px-3 py-2 text-right font-mono text-[12px] font-semibold">{effective(row).toFixed(2)}×</td>
                    <td class="px-3 py-2 text-right">
                      <button
                        onclick={() => togglePreview(row)}
                        disabled={isPreviewLoading}
                        title={isPlaying ? $_('configVolume.stop') : $_('configVolume.play')}
                        class="inline-flex h-8 w-8 items-center justify-center rounded-lg border-0 text-white disabled:opacity-60"
                        style="background: linear-gradient(135deg, hsl(var(--gradient-from)), hsl(var(--gradient-to))); box-shadow: 0 4px 14px -2px hsl(var(--gradient-from) / .35);"
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
