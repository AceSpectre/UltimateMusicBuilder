<script lang="ts">
  import Plus from '@lucide/svelte/icons/plus'
  import SlidersHorizontal from '@lucide/svelte/icons/sliders-horizontal'
  import Trash2 from '@lucide/svelte/icons/trash-2'
  import { _ } from 'svelte-i18n'
  import { modsStore } from '$lib/stores/mods.svelte'
  import { guardLeave, trackUnsaved } from '$lib/stores/unsaved.svelte'
  import { logStore } from '$lib/stores/logs.svelte'
  import { createSeriesSelection } from '$lib/series-selection.svelte'
  import ActionHeader from '$lib/components/ui/action-header.svelte'
  import EmptyState from '$lib/components/ui/empty-state.svelte'
  import GameSelect from '$lib/components/ui/game-select.svelte'
  import IconButton from '$lib/components/ui/icon-button.svelte'
  import SaveButton from '$lib/components/ui/save-button.svelte'
  import SeriesPicker from '$lib/components/ui/series-picker.svelte'
  import { RECORD_OPTIONS, emptyPreset, resolvePreset, type PresetValues } from '$lib/presets'
  import { inputClass, textareaClass } from '$lib/ui-classes'
  import type { ModInfo, SongPreset, TrackOrderData } from '$lib/types/electron'

  type Kind = 'series' | 'game' | 'preset'

  const GRID_COLUMNS =
    'grid-template-columns: minmax(130px,1fr) minmax(120px,1fr) minmax(120px,1fr) minmax(120px,1fr) 130px 130px 32px;'
  const COLUMNS = ['colName', 'colGame', 'colAuthor', 'colCopyright', 'colRecord', 'colInSoundtest'] as const

  let { activeMod }: { activeMod: ModInfo | null } = $props()

  const selection = createSeriesSelection(() => activeMod)
  let data = $state<TrackOrderData | null>(null)
  let seriesDefault = $state<SongPreset>(emptyPreset())
  let gameDefaults = $state<SongPreset[]>([])
  let presets = $state<SongPreset[]>([])
  let dataLoading = $state(false)
  let saveState = $state<'idle' | 'saving' | 'saved'>('idle')
  let baselineSnapshot = $state('')

  const edited = $derived({ seriesDefault, gameDefaults, presets })
  const isDirty = $derived(Boolean(data) && JSON.stringify(edited) !== baselineSnapshot)

  $effect(() => {
    if (isDirty && saveState === 'saved') {
      saveState = 'idle'
    }
  })

  function gameName(id: string): string {
    return data?.games.find((g) => g.id === id)?.name ?? id
  }

  function soundtestLabel(value: string): string {
    return $_(value === 'False' ? 'managePresets.hidden' : 'managePresets.shown')
  }

  // Blank values: the series default falls back to built-in values, game defaults to the series default, presets to their game.
  function inherited(kind: Kind, preset: SongPreset): PresetValues {
    const series = kind === 'series' ? emptyPreset() : seriesDefault
    return resolvePreset({ seriesDefault: series, gameDefaults: kind === 'preset' ? gameDefaults : [], presets: [] }, preset.game)
  }

  function blankLabel(kind: Kind, value: string): string {
    return $_(kind === 'series' ? 'managePresets.fallback' : 'managePresets.inherit', { values: { value } })
  }

  function setFrom(result: TrackOrderData) {
    data = result
    seriesDefault = result.presets.seriesDefault
    gameDefaults = result.games.map((g) => result.presets.gameDefaults.find((d) => d.game === g.id) ?? emptyPreset({ game: g.id }))
    presets = result.presets.presets
    baselineSnapshot = JSON.stringify(edited)
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

  async function handleSave() {
    if (!data || !modsStore.activeSeriesPath) {
      return
    }

    saveState = 'saving'
    try {
      setFrom(await window.electron.umb.saveSongPresets(modsStore.activeSeriesPath, $state.snapshot(edited)))
      saveState = 'saved'
    } catch (error) {
      logStore.log('error', `Saving presets failed: ${error instanceof Error ? error.message : String(error)}`)
      saveState = 'idle'
    }
  }

  $effect(() => {
    void loadData(modsStore.activeSeriesPath)
  })

  trackUnsaved(() => isDirty, handleSave)
</script>

{#snippet row(kind: Kind, preset: SongPreset, onDelete?: () => void)}
  {@const parent = inherited(kind, preset)}
  <div class="grid items-center gap-1 rounded border border-line2 px-1 py-0.5" style={GRID_COLUMNS} role="listitem">
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
      <span class="truncate px-1.5 tabular-nums text-[11px] text-muted-foreground">{preset.game}</span>
    {:else}
      <GameSelect
        bind:value={preset.game}
        games={data?.games ?? []}
        label={$_('managePresets.colGame')}
        emptyLabel={$_(kind === 'series' ? 'managePresets.noGame' : 'managePresets.songGame')}
      />
    {/if}

    <textarea class={textareaClass} bind:value={preset.author} placeholder={parent.author} aria-label={$_('managePresets.colAuthor')}></textarea>
    <textarea class={textareaClass} bind:value={preset.copyright} placeholder={parent.copyright} aria-label={$_('managePresets.colCopyright')}></textarea>

    <select class={inputClass} bind:value={preset.record_type} aria-label={$_('managePresets.colRecord')}>
      <option value="">{blankLabel(kind, $_(`orderTracks.record.${parent.record_type}`))}</option>
      {#each RECORD_OPTIONS as option}
        <option value={option}>{$_(`orderTracks.record.${option}`)}</option>
      {/each}
    </select>

    <select class={inputClass} bind:value={preset.in_soundtest} aria-label={$_('managePresets.colInSoundtest')}>
      <option value="">{blankLabel(kind, soundtestLabel(parent.in_soundtest))}</option>
      <option value="True">{soundtestLabel('True')}</option>
      <option value="False">{soundtestLabel('False')}</option>
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
      series={selection.series}
      loading={selection.loading}
      activePath={modsStore.activeSeriesPath}
      onSelect={(path) => { if (path !== modsStore.activeSeriesPath) guardLeave(() => (modsStore.activeSeriesPath = path)) }}
      onReload={() => {
        void selection.reload()
        void loadData(modsStore.activeSeriesPath)
      }}
    />

    <section class="surface-grain flex h-full min-h-0 min-w-0 flex-1 flex-col overflow-hidden bg-card">
      <ActionHeader title={$_('managePresets.title')} subtitle={data?.seriesName ?? selection.selected?.name ?? ''}>
          <button onclick={() => presets.push(emptyPreset())} class="btn" disabled={!data}>
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
      </ActionHeader>

      <div class="min-h-0 flex-1 overflow-auto px-[22px] py-3.5">
        {#if dataLoading}
          <div class="grid h-full min-h-[320px] place-items-center rounded-xl border border-dashed border-border text-[13px] text-muted-foreground">
            {$_('managePresets.loading')}
          </div>
        {:else if data}
          <div class="min-w-[800px]">
            <div
              class="sticky top-0 z-10 grid items-center gap-1 border-b border-border bg-card px-1 py-1 text-[10.5px] font-semibold uppercase tracking-[.1em] text-dim"
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
          <EmptyState dashed title={$_('managePresets.emptyTitle')} body={$_('common.selectSeries')}>
            {#snippet icon()}<SlidersHorizontal size={20} />{/snippet}
          </EmptyState>
        {/if}
      </div>
    </section>
  </div>
</div>
