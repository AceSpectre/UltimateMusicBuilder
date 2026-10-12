<script lang="ts">
  import { Plus, SlidersHorizontal, Trash2 } from '@lucide/svelte'
  import { _ } from 'svelte-i18n'
  import { modsStore } from '$lib/stores/mods.svelte'
  import { logStore } from '$lib/stores/logs.svelte'
  import { createSeriesLoader } from '$lib/series-loader'
  import GradientIcon from '$lib/components/ui/gradient-icon.svelte'
  import EmptyState from '$lib/components/ui/empty-state.svelte'
  import IconButton from '$lib/components/ui/icon-button.svelte'
  import SaveButton from '$lib/components/ui/save-button.svelte'
  import SeriesPicker from '$lib/components/ui/series-picker.svelte'
  import { RECORD_OPTIONS, emptyPreset, gameDefaultRows, pinchSongOptions, recordLabelKey, resolvePreset, type PresetValues } from '$lib/presets'
  import type { ModInfo, ModSeriesInfo, SongPreset, TrackOrderData } from '$lib/types/electron'

  type Kind = 'series' | 'game' | 'preset'

  const GRID_COLUMNS =
    'grid-template-columns: minmax(130px,1fr) minmax(120px,1fr) minmax(120px,1fr) minmax(120px,1fr) 130px 130px minmax(140px,1fr) 32px;'
  const COLUMNS = ['colName', 'colGame', 'colAuthor', 'colCopyright', 'colRecord', 'colInSoundtest', 'colPinchSong'] as const
  const inputClass =
    'w-full rounded border border-input bg-background px-1.5 py-0.5 text-[12.5px] outline-none focus:border-ring focus:ring-1 focus:ring-ring'
  // Multiline: credits support newlines in-game.
  const textareaClass = `${inputClass} resize-none overflow-hidden whitespace-pre [field-sizing:content]`

  let { activeMod }: { activeMod: ModInfo | null } = $props()

  let loading = $state(false)
  let series = $state<ModSeriesInfo[]>([])
  let data = $state<TrackOrderData | null>(null)
  let seriesDefault = $state<SongPreset>(emptyPreset())
  let gameDefaults = $state<SongPreset[]>([])
  let presets = $state<SongPreset[]>([])
  let dataLoading = $state(false)
  let saveState = $state<'idle' | 'saving' | 'saved'>('idle')
  let baselineSnapshot = $state('')
  const seriesLoader = createSeriesLoader((v) => (loading = v))

  const edited = $derived({ seriesDefault, gameDefaults, presets })
  const isDirty = $derived(Boolean(data) && JSON.stringify(edited) !== baselineSnapshot)
  const pinchOptions = $derived(data ? pinchSongOptions(data.items, data.vanillaSongs) : [])
  const selectedSeries = $derived(series.find((entry) => entry.path === modsStore.activeSeriesPath) ?? null)

  $effect(() => {
    if (isDirty && saveState === 'saved') {
      saveState = 'idle'
    }
  })

  function gameName(id: string): string {
    return data?.games.find((g) => g.id === id)?.name ?? id
  }

  function recordLabel(value: string): string {
    return $_(recordLabelKey(value))
  }

  function pinchLabel(value: string): string {
    return pinchOptions.find((o) => o.value === value)?.title ?? value
  }

  function soundtestLabel(value: string): string {
    return $_(value === 'False' ? 'managePresets.hidden' : 'managePresets.shown')
  }

  // What an empty field falls back to: game defaults inherit from the series default, presets from their game.
  function inherited(kind: Kind, preset: SongPreset): PresetValues | null {
    if (kind === 'series') return null
    if (kind === 'game') return resolvePreset({ seriesDefault, gameDefaults: [], presets: [] }, preset.game)
    return resolvePreset({ seriesDefault, gameDefaults, presets }, preset.game)
  }

  function setFrom(result: TrackOrderData) {
    data = result
    seriesDefault = result.presets.seriesDefault
    gameDefaults = gameDefaultRows(result.games, result.presets.gameDefaults)
    presets = result.presets.presets
    baselineSnapshot = JSON.stringify(edited)
  }

  async function loadSeries(modPath: string | null) {
    if (!modPath) {
      seriesLoader.invalidate()
      series = []
      modsStore.activeSeriesPath = null
      data = null
      return
    }

    const nextSeries = await seriesLoader.load(modPath)
    if (nextSeries === null) {
      return
    }

    series = nextSeries
    if (!nextSeries.some((entry) => entry.path === modsStore.activeSeriesPath)) {
      modsStore.activeSeriesPath = nextSeries[0]?.path ?? null
    }
  }

  async function loadData(seriesPath: string | null) {
    if (!seriesPath) {
      data = null
      return
    }

    dataLoading = true
    saveState = 'idle'
    try {
      const result = await window.electron.umb.loadTrackOrder(seriesPath)
      if (seriesPath === modsStore.activeSeriesPath) setFrom(result)
    } finally {
      dataLoading = false
    }
  }

  function handleReload() {
    void loadSeries(activeMod?.path ?? null)
    void loadData(modsStore.activeSeriesPath)
  }

  async function handleSave() {
    if (!data || !modsStore.activeSeriesPath) {
      return
    }

    saveState = 'saving'
    try {
      // Plain objects: Svelte $state proxies cannot be structured-cloned over IPC.
      setFrom(await window.electron.umb.saveSongPresets(modsStore.activeSeriesPath, $state.snapshot(edited)))
      saveState = 'saved'
    } catch (error) {
      logStore.log('error', `Saving presets failed: ${error instanceof Error ? error.message : String(error)}`)
      saveState = 'idle'
    }
  }

  $effect(() => {
    void loadSeries(activeMod?.path ?? null)
  })

  $effect(() => {
    void loadData(modsStore.activeSeriesPath)
  })
</script>

{#snippet row(kind: Kind, preset: SongPreset, onDelete?: () => void)}
  {@const parent = inherited(kind, preset)}
  <div class="grid items-center gap-1 rounded border border-border bg-background/75 px-1 py-0.5" style={GRID_COLUMNS} role="listitem">
    {#if kind === 'preset'}
      <input
        type="text"
        class={inputClass}
        bind:value={preset.name}
        placeholder={$_('managePresets.unnamed')}
        aria-label={$_('managePresets.colName')}
      />
    {:else}
      <span class="truncate px-1.5 text-[12.5px] font-medium">
        {kind === 'series' ? $_('managePresets.seriesDefault') : gameName(preset.game)}
      </span>
    {/if}

    {#if kind === 'game'}
      <span class="truncate px-1.5 font-mono text-[11px] text-muted-foreground">{preset.game}</span>
    {:else}
      <select class={inputClass} bind:value={preset.game} aria-label={$_('managePresets.colGame')}>
        <option value="">{$_(kind === 'series' ? 'managePresets.noGame' : 'managePresets.songGame')}</option>
        {#each data?.games ?? [] as game}
          <option value={game.id}>{game.name}</option>
        {/each}
        {#if preset.game && !data?.games.some((g) => g.id === preset.game)}
          <option value={preset.game}>{preset.game}</option>
        {/if}
      </select>
    {/if}

    <textarea class={textareaClass} bind:value={preset.author} placeholder={parent?.author} aria-label={$_('managePresets.colAuthor')}></textarea>
    <textarea class={textareaClass} bind:value={preset.copyright} placeholder={parent?.copyright} aria-label={$_('managePresets.colCopyright')}></textarea>

    <select class={inputClass} bind:value={preset.record_type} aria-label={$_('managePresets.colRecord')}>
      {#if parent}
        <option value="">{$_('managePresets.inherit', { values: { value: recordLabel(parent.record_type) } })}</option>
      {/if}
      {#each RECORD_OPTIONS as option}
        <option value={option}>{recordLabel(option)}</option>
      {/each}
    </select>

    <select class={inputClass} bind:value={preset.in_soundtest} aria-label={$_('managePresets.colInSoundtest')}>
      {#if parent}
        <option value="">{$_('managePresets.inherit', { values: { value: soundtestLabel(parent.in_soundtest) } })}</option>
      {/if}
      <option value="True">{soundtestLabel('True')}</option>
      <option value="False">{soundtestLabel('False')}</option>
    </select>

    <select class={inputClass} bind:value={preset.pinch_song} aria-label={$_('managePresets.colPinchSong')}>
      <option value="">
        {parent ? $_('managePresets.inherit', { values: { value: parent.pinch_song ? pinchLabel(parent.pinch_song) : $_('managePresets.noPinch') } }) : $_('managePresets.noPinch')}
      </option>
      {#each pinchOptions as option}
        <option value={option.value}>{option.title}</option>
      {/each}
      {#if preset.pinch_song && !pinchOptions.some((o) => o.value === preset.pinch_song)}
        <option value={preset.pinch_song}>{preset.pinch_song}</option>
      {/if}
    </select>

    {#if onDelete}
      <IconButton onclick={onDelete} title={$_('managePresets.delete')}>
        <Trash2 size={14} />
      </IconButton>
    {:else}
      <span></span>
    {/if}
  </div>
{/snippet}

{#snippet heading(title: string, hint: string)}
  <div class="px-1 pb-1 pt-3">
    <h3 class="text-[12.5px] font-semibold">{title}</h3>
    <p class="text-[11.5px] text-muted-foreground">{hint}</p>
  </div>
{/snippet}

<div class="flex-1 overflow-hidden">
  <div class="flex h-full min-h-0">
    <SeriesPicker
      {activeMod}
      {series}
      {loading}
      activePath={modsStore.activeSeriesPath}
      onSelect={(path) => (modsStore.activeSeriesPath = path)}
      onReload={handleReload}
    />

    <section class="flex h-full min-h-0 min-w-0 flex-1 flex-col border border-border bg-card overflow-hidden">
      <div class="gradient-strip h-[3px] shrink-0"></div>
      <div class="shrink-0 border-b border-border px-4 py-3 flex items-center justify-between gap-4">
        <div class="flex min-w-0 items-center gap-3">
          <GradientIcon>
            <SlidersHorizontal size={18} />
          </GradientIcon>
          <h2 class="min-w-0 truncate text-sm font-semibold">{data?.seriesName ?? selectedSeries?.name ?? $_('managePresets.title')}</h2>
        </div>

        <div class="flex shrink-0 items-center gap-2">
          <button
            onclick={() => presets.push(emptyPreset())}
            class="inline-flex items-center gap-2 rounded-lg border border-input bg-background px-3 py-2 text-[12.5px] font-medium transition-colors hover:bg-muted disabled:cursor-not-allowed disabled:opacity-60"
            disabled={!data}
          >
            <Plus size={14} />
            {$_('managePresets.add')}
          </button>

          <SaveButton
            {saveState}
            onclick={handleSave}
            disabled={!data || !isDirty || saveState === 'saving'}
            save={$_('managePresets.save')}
            saving={$_('managePresets.saving')}
            saved={$_('managePresets.saved')}
          />
        </div>
      </div>

      <div class="min-h-0 flex-1 overflow-auto p-1">
        {#if dataLoading}
          <div class="grid h-full min-h-[320px] place-items-center rounded-2xl border border-dashed border-border bg-background/60 text-[13px] text-muted-foreground">
            {$_('managePresets.loading')}
          </div>
        {:else if data}
          <div class="min-w-[950px]">
            <div
              class="sticky top-0 z-10 grid items-center gap-1 border-b border-border bg-card px-1 py-1 text-[11px] font-semibold uppercase tracking-wide text-muted-foreground"
              style={GRID_COLUMNS}
            >
              {#each COLUMNS as column}
                <span>{$_(`managePresets.${column}`)}</span>
              {/each}
            </div>

            {@render heading($_('managePresets.defaultsHeading'), $_('managePresets.defaultsHint'))}
            <div class="grid gap-0.5" role="list">
              {@render row('series', seriesDefault)}
              {#each gameDefaults as preset (preset.game)}
                {@render row('game', preset)}
              {/each}
            </div>

            {@render heading($_('managePresets.presetsHeading'), $_('managePresets.presetsHint'))}
            <div class="grid gap-0.5" role="list">
              {#each presets as preset, index}
                {@render row('preset', preset, () => presets.splice(index, 1))}
              {:else}
                <p class="px-1 py-2 text-[12px] text-muted-foreground">{$_('managePresets.noPresets')}</p>
              {/each}
            </div>
          </div>
        {:else}
          <EmptyState dashed title={$_('managePresets.emptyTitle')} body={$_('managePresets.emptyBody')}>
            {#snippet icon()}<SlidersHorizontal size={20} />{/snippet}
          </EmptyState>
        {/if}
      </div>
    </section>
  </div>
</div>
