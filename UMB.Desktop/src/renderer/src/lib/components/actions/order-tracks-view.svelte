<script lang="ts">
  import ArrowDown from '@lucide/svelte/icons/arrow-down'
  import ArrowUp from '@lucide/svelte/icons/arrow-up'
  import ArrowUpDown from '@lucide/svelte/icons/arrow-up-down'
  import GripVertical from '@lucide/svelte/icons/grip-vertical'
  import Lock from '@lucide/svelte/icons/lock'
  import LockOpen from '@lucide/svelte/icons/lock-open'
  import Music4 from '@lucide/svelte/icons/music-4'
  import ListChecks from '@lucide/svelte/icons/list-checks'
  import Palette from '@lucide/svelte/icons/palette'
  import Wand2 from '@lucide/svelte/icons/wand-2'
  import { DropdownMenu } from 'bits-ui'
  import { _ } from 'svelte-i18n'
  import { flip } from 'svelte/animate'
  import { dragHandleZone, dragHandle, type DndEvent } from 'svelte-dnd-action'
  import { modsStore } from '$lib/stores/mods.svelte'
  import { guardLeave, trackUnsaved } from '$lib/stores/unsaved.svelte'
  import { createSeriesSelection } from '$lib/series-selection.svelte'
  import GradientIcon from '$lib/components/ui/gradient-icon.svelte'
  import EmptyState from '$lib/components/ui/empty-state.svelte'
  import SaveButton from '$lib/components/ui/save-button.svelte'
  import SeriesPicker from '$lib/components/ui/series-picker.svelte'
  import GameSelect from '$lib/components/ui/game-select.svelte'
  import { inSoundtest, overlayLegend, overlayStyle, seriesGameIds } from '$lib/song-overlay'
  import { nextSort, sortBy, type SortKey, type SortState } from '$lib/table-sort'
  import { RECORD_OPTIONS, applyChoice, missingDefaults, presetChoices, type PresetChoice } from '$lib/presets'
  import { inputClass, secondaryButtonClass, textareaClass, toolbarButtonClass } from '$lib/ui-classes'
  import type { ModInfo, TrackOrderData, TrackOrderItem } from '$lib/types/electron'

  const FLIP_MS = 180
  const PINCH_CATEGORY = 'sf_situationlink'
  const GRID_COLUMNS =
    'grid-template-columns: 52px minmax(160px,1.4fr) minmax(130px,1fr) minmax(110px,1fr) minmax(140px,1fr) 116px minmax(160px,1fr) 84px;'
  // Header columns in grid order; `index` is the series' real track order.
  const COLUMNS = [
    { id: 'index', label: 'orderTracks.colIndex' },
    { id: 'title', label: 'orderTracks.colTitle' },
    { id: 'game', label: 'orderTracks.colGame' },
    { id: 'author', label: 'orderTracks.colAuthor' },
    { id: 'copyright', label: 'orderTracks.colCopyright' },
    { id: 'record', label: 'orderTracks.colRecord' },
    { id: 'pinchSong', label: 'orderTracks.colPinchSong' },
    { id: 'soundtest', label: 'orderTracks.colInSoundtest' }
  ] as const
  type SortColumn = (typeof COLUMNS)[number]['id']
  const CENTERED_COLUMNS = new Set<SortColumn>(['soundtest'])

  let { activeMod }: { activeMod: ModInfo | null } = $props()

  const selection = createSeriesSelection(() => activeMod)
  let orderData = $state<TrackOrderData | null>(null)
  let dndItems = $state<TrackOrderItem[]>([])
  let orderLoading = $state(false)
  let saveState = $state<'idle' | 'saving' | 'saved'>('idle')
  let baselineSnapshot = $state('')
  let selectedItemId = $state<string | null>(null)
  let overlayOn = $state(false)
  // Locked vanilla songs are read-only and keep their place, as before they were editable.
  let vanillaLocked = $state(true)
  const isReadOnly = (item: TrackOrderItem): boolean => item.isLocked || (item.isVanilla && vanillaLocked)
  const lockedCellClass = 'self-center whitespace-pre-line break-words px-1.5 py-0.5 text-[12.5px] text-muted-foreground'
  let sort = $state<SortState<SortColumn>>({ column: 'index', desc: false })

  function snapshot(items: TrackOrderItem[]): string {
    return JSON.stringify(items.map((item) => ({ id: item.id, fields: item.fields })))
  }

  const isDirty = $derived(Boolean(orderData) && snapshot(dndItems) !== baselineSnapshot)

  // Filenames referenced as some other song's pinch variant — those rows are pinch targets.
  const pinchTargets = $derived(
    new Set(
      dndItems
        .map((item) => item.fields?.info1?.trim())
        .filter((info1): info1 is string => Boolean(info1) && !info1.startsWith('info_'))
    )
  )

  // Songs selectable as a pinch target. Mod tracks store their filename in info1; vanilla
  // songs store their base-game info id (info_*). Both share one searchable dropdown.
  const modPinchOptions = $derived(
    dndItems
      .filter((item) => item.fields !== null && item.filename && !item.isVanilla)
      .map((item) => ({ value: item.filename, title: item.fields?.title || item.filename }))
  )
  const vanillaPinchOptions = $derived(
    (orderData?.vanillaSongs ?? []).map((song) => ({ value: song.infoId, title: song.name }))
  )
  const pinchOptions = $derived([...modPinchOptions, ...vanillaPinchOptions])

  function pinchTitle(value: string): string {
    return pinchOptions.find((o) => o.value === value)?.title ?? value
  }

  function gameName(id: string): string {
    return orderData?.games.find((g) => g.id === id)?.name ?? id
  }

  const sortKeys: Record<SortColumn, (item: TrackOrderItem, index: number) => SortKey> = {
    index: (_, index) => index,
    title: (item) => item.fields?.title ?? item.title,
    game: (item) => gameName(item.fields?.game ?? ''),
    author: (item) => item.fields?.author ?? '',
    copyright: (item) => item.fields?.copyright ?? '',
    record: (item) => (item.fields ? recordLabel(item.fields.record_type) : ''),
    pinchSong: (item) => pinchTitle(item.fields?.info1 ?? ''),
    soundtest: (item) => Boolean(item.fields && inSoundtest(item.fields))
  }

  // Sorting is display-only; dragging needs the list in real track order.
  const canDrag = $derived(sort.column === 'index' && !sort.desc)
  const hasVanilla = $derived(dndItems.some((item) => item.isVanilla))
  const displayItems = $derived.by(() => {
    if (canDrag) return dndItems
    return sortBy(dndItems, sort.desc, sortKeys[sort.column])
  })


  function isPinchTarget(item: TrackOrderItem): boolean {
    return Boolean(item.filename) && pinchTargets.has(item.filename)
  }

  function hasForeignCategory(item: TrackOrderItem): boolean {
    const cat = item.fields?.special_category ?? ''
    return cat !== '' && cat !== PINCH_CATEGORY
  }

  function pinchDisabled(item: TrackOrderItem): boolean {
    return isPinchTarget(item) || hasForeignCategory(item)
  }

  // `value` is a mod track filename or a vanilla info_* id (empty clears the link).
  function setPinchSong(item: TrackOrderItem, value: string) {
    if (!item.fields) {
      return
    }

    if (!value) {
      item.fields.info1 = ''
      if (item.fields.special_category === PINCH_CATEGORY) {
        item.fields.special_category = ''
      }
      return
    }

    item.fields.info1 = value
    item.fields.special_category = PINCH_CATEGORY

    // A chosen mod track cannot itself point at a pinch song (vanilla targets have no row).
    if (!value.startsWith('info_')) {
      const target = dndItems.find((other) => other.filename === value && other.fields)
      if (target?.fields?.info1) {
        target.fields.info1 = ''
        if (target.fields.special_category === PINCH_CATEGORY) {
          target.fields.special_category = ''
        }
      }
    }
  }

  const gameIds = $derived(seriesGameIds(orderData?.games ?? [], dndItems))
  const legend = $derived(overlayOn ? overlayLegend(gameIds, gameName) : [])

  const selectedItem = $derived(dndItems.find((item) => item.id === selectedItemId) ?? null)

  const choices = $derived(
    orderData && selectedItem?.fields ? presetChoices(orderData.presets, orderData.games, selectedItem.fields.game) : []
  )

  function applyPreset(choice: PresetChoice) {
    if (!orderData || !selectedItem?.fields) {
      return
    }
    Object.assign(selectedItem.fields, applyChoice(orderData.presets, selectedItem.fields.game, choice))
  }

  // Fills only the fields each song leaves empty.
  function fillDefaults() {
    if (!orderData) {
      return
    }
    for (const item of dndItems) {
      if (!item.fields) continue
      Object.assign(item.fields, missingDefaults(orderData.presets, item.fields))
    }
  }


  $effect(() => {
    dndItems = orderData ? orderData.items : []
  })

  $effect(() => {
    if (isDirty && saveState === 'saved') {
      saveState = 'idle'
    }
  })

  function handleConsider(event: CustomEvent<DndEvent<TrackOrderItem>>) {
    dndItems = event.detail.items
  }

  function handleFinalize(event: CustomEvent<DndEvent<TrackOrderItem>>) {
    dndItems = event.detail.items
    if (orderData) {
      orderData = { ...orderData, items: event.detail.items }
    }
  }

  async function loadOrderData(seriesPath: string | null) {
    if (!seriesPath) {
      orderData = null
      return
    }

    orderLoading = true
    saveState = 'idle'
    selectedItemId = null
    try {
      orderData = await window.electron.umb.loadTrackOrder(seriesPath)
      baselineSnapshot = snapshot(orderData.items)
    } finally {
      orderLoading = false
    }
  }

  function requestSelectSeries(path: string) {
    if (path !== modsStore.activeSeriesPath) guardLeave(() => (modsStore.activeSeriesPath = path))
  }

  function handleReload() {
    void selection.reload()
    void loadOrderData(modsStore.activeSeriesPath)
  }

  async function handleSave() {
    if (!orderData || !modsStore.activeSeriesPath) {
      return
    }

    saveState = 'saving'
    // Send plain objects — Svelte $state proxies cannot be structured-cloned over IPC.
    const payload = dndItems.map((item) => ({
      id: item.id,
      fields: item.fields ? { ...item.fields } : null
    }))
    try {
      orderData = await window.electron.umb.saveTrackOrder(modsStore.activeSeriesPath, payload)
      baselineSnapshot = snapshot(orderData.items)
      saveState = 'saved'
    } catch (error) {
      console.error('saveTrackOrder failed', error)
      saveState = 'idle'
    }
  }

  $effect(() => {
    void loadOrderData(modsStore.activeSeriesPath)
  })

  const recordLabel = (value: string): string => $_(`orderTracks.record.${value}`, { default: value })

  trackUnsaved(() => isDirty, handleSave)
</script>

<div class="flex-1 overflow-hidden">
  <div class="flex h-full min-h-0">
    <SeriesPicker
      {activeMod}
      series={selection.series}
      loading={selection.loading}
      activePath={modsStore.activeSeriesPath}
      onSelect={requestSelectSeries}
      onReload={handleReload}
    />

    <section class="flex h-full min-h-0 min-w-0 flex-1 flex-col border border-border bg-card overflow-hidden">
      <div class="gradient-strip h-[3px] shrink-0"></div>
      <div class="shrink-0 border-b border-border px-4 py-3 flex items-center justify-between gap-4">
        <div class="flex min-w-0 items-center gap-3">
          <GradientIcon>
            <ArrowUpDown size={18} />
          </GradientIcon>
          <h2 class="min-w-0 truncate text-sm font-semibold">{orderData?.seriesName ?? selection.selected?.name ?? $_('orderTracks.title')}</h2>
        </div>

        <div class="flex shrink-0 items-center gap-2">
          <button
            onclick={() => (overlayOn = !overlayOn)}
            class="{toolbarButtonClass} {overlayOn ? 'bg-muted' : ''}"
            disabled={!orderData}
            aria-pressed={overlayOn}
            title={$_('orderTracks.overlayHint')}
          >
            <Palette size={14} />
            {$_('orderTracks.overlay')}
          </button>

          <button
            onclick={() => (vanillaLocked = !vanillaLocked)}
            class="{toolbarButtonClass} w-[140px] justify-center"
            disabled={!hasVanilla}
            aria-pressed={!vanillaLocked}
          >
            {#if vanillaLocked}<LockOpen size={14} />{:else}<Lock size={14} />{/if}
            {$_(vanillaLocked ? 'orderTracks.unlockVanilla' : 'orderTracks.lockVanilla')}
          </button>

          <button
            onclick={fillDefaults}
            class={secondaryButtonClass}
            disabled={!orderData}
            title={$_('orderTracks.fillDefaultsHint')}
          >
            <ListChecks size={14} />
            {$_('orderTracks.fillDefaults')}
          </button>

          <DropdownMenu.Root>
            <DropdownMenu.Trigger class={toolbarButtonClass} disabled={!selectedItem?.fields} title={$_('orderTracks.applyPresetHint')}>
              <Wand2 size={14} />
              {$_('orderTracks.applyPreset')}
            </DropdownMenu.Trigger>
            <DropdownMenu.Portal>
              <DropdownMenu.Content align="end" sideOffset={4} class="z-50 min-w-[160px] rounded-lg border border-border bg-card p-1 text-[12.5px] shadow-lg">
                {#each choices as choice, index}
                  {#if index > 0 && choices[index - 1].kind !== choice.kind}
                    <DropdownMenu.Separator class="my-1 h-px bg-border" />
                  {/if}
                  <DropdownMenu.Item
                    class="cursor-pointer rounded px-2 py-1.5 outline-none data-[highlighted]:bg-muted"
                    onSelect={() => applyPreset(choice)}
                  >
                    {choice.kind === 'series' ? $_('orderTracks.presetSeriesDefault') : choice.label || $_('managePresets.unnamed')}
                  </DropdownMenu.Item>
                {/each}
              </DropdownMenu.Content>
            </DropdownMenu.Portal>
          </DropdownMenu.Root>

          <SaveButton
            {saveState}
            onclick={handleSave}
            disabled={!orderData || !isDirty || saveState === 'saving'}
            save={$_('orderTracks.save')}
            saving={$_('orderTracks.saving')}
            saved={$_('orderTracks.saved')}
          />
        </div>
      </div>

      {#if orderData && legend.length > 0}
        <div class="flex shrink-0 flex-wrap items-center gap-1.5 border-b border-border px-4 py-1.5">
          {#each legend as entry}
            <span class="rounded border border-border bg-background px-2 py-0.5 text-[11px]" style={entry.style}>{entry.label}</span>
          {/each}
        </div>
      {/if}

      <div class="min-h-0 flex-1 overflow-auto p-1">
        {#if orderLoading}
          <div class="grid h-full min-h-[320px] place-items-center rounded-2xl border border-dashed border-border bg-background/60 text-[13px] text-muted-foreground">
            {$_('orderTracks.loadingOrder')}
          </div>
        {:else if orderData}
          <datalist id="pinch-candidates">
            {#each pinchOptions as option}
              <option value={option.title}></option>
            {/each}
          </datalist>

          <div class="min-w-[1000px]">
            <div
              class="sticky z-10 grid items-center gap-1 border-b border-border bg-card px-1 py-1 text-[11px] font-semibold uppercase tracking-wide text-muted-foreground"
              style="{GRID_COLUMNS} top: -5px"
            >
              {#each COLUMNS as column}
                <button
                  type="button"
                  class="inline-flex items-center gap-1 uppercase tracking-wide hover:text-foreground {CENTERED_COLUMNS.has(column.id) ? 'justify-center' : ''}"
                  onclick={() => (sort = nextSort(sort, column.id))}
                >
                  {$_(column.label)}
                  {#if sort.column === column.id}
                    {#if sort.desc}<ArrowDown size={11} />{:else}<ArrowUp size={11} />{/if}
                  {/if}
                </button>
              {/each}
            </div>

            <div
              class="grid gap-0.5"
              use:dragHandleZone={{ items: displayItems, flipDurationMs: FLIP_MS, dropTargetStyle: {}, dragDisabled: !canDrag }}
              onconsider={handleConsider}
              onfinalize={handleFinalize}
            >
              {#each displayItems as item (item.id)}
                {@const readOnly = isReadOnly(item)}
                {@const isSelected = !readOnly && item.id === selectedItemId}
                <div
                  animate:flip={{ duration: FLIP_MS }}
                  class="grid items-center gap-1 rounded border px-1 py-0.5 {readOnly ? 'border-border bg-background/75 opacity-80' : isSelected ? 'border-transparent bg-background ring-1 ring-[hsl(var(--gradient-from))]' : 'border-border bg-background/75'}"
                  style="{GRID_COLUMNS} {overlayOn ? overlayStyle(item, gameIds) : ''}"
                  role="listitem"
                  onfocusincapture={() => { if (!readOnly) selectedItemId = item.id }}
                  onpointerdown={() => { if (!readOnly) selectedItemId = item.id }}
                >
                  <div class="flex items-center gap-1">
                    <span class="w-6 text-right text-[11px] tabular-nums text-muted-foreground">{dndItems.indexOf(item) + 1}</span>
                    {#if readOnly}
                      <div class="flex h-6 w-6 shrink-0 items-center justify-center rounded bg-muted/70 text-muted-foreground" title={$_('orderTracks.locked')}>
                        <Lock size={13} />
                      </div>
                    {:else}
                      <div
                        use:dragHandle
                        aria-label={$_('orderTracks.dragAria')}
                        title={canDrag ? '' : $_('orderTracks.sortedDragHint')}
                        class="flex h-6 w-6 shrink-0 items-center justify-center rounded bg-muted text-muted-foreground {canDrag ? 'cursor-grab active:cursor-grabbing' : 'cursor-not-allowed opacity-50'}"
                      >
                        <GripVertical size={14} />
                      </div>
                    {/if}
                  </div>

                  {#if item.fields && !readOnly}
                    {@const f = item.fields}
                    <textarea
                      class="{textareaClass} {item.isVanilla ? 'italic' : ''}"
                      bind:value={f.title}
                      title={item.isVanilla ? $_('orderTracks.vanillaHint') : ''}
                      aria-label={$_('orderTracks.colTitle')}
                    ></textarea>

                    {#if orderData.games.length > 0}
                      <GameSelect bind:value={f.game} games={orderData.games} label={$_('orderTracks.colGame')} />
                    {:else}
                      <input type="text" class={inputClass} bind:value={f.game} aria-label={$_('orderTracks.colGame')} />
                    {/if}

                    <textarea class={textareaClass} bind:value={f.author} aria-label={$_('orderTracks.colAuthor')}></textarea>
                    <textarea class={textareaClass} bind:value={f.copyright} aria-label={$_('orderTracks.colCopyright')}></textarea>

                    <select class={inputClass} bind:value={f.record_type} aria-label={$_('orderTracks.colRecord')}>
                      {#each RECORD_OPTIONS as option}
                        <option value={option}>{recordLabel(option)}</option>
                      {/each}
                    </select>

                    <input
                      type="text"
                      list="pinch-candidates"
                      class={inputClass}
                      placeholder={pinchDisabled(item) ? '' : $_('orderTracks.pinchNone')}
                      disabled={pinchDisabled(item)}
                      title={hasForeignCategory(item)
                        ? `${$_('orderTracks.specialCategoryHint')} (${f.special_category})`
                        : isPinchTarget(item)
                          ? $_('orderTracks.pinchTargetHint')
                          : ''}
                      value={pinchTitle(f.info1)}
                      onchange={(event) => {
                        const text = event.currentTarget.value.trim()
                        const match = pinchOptions.find((o) => o.title === text && o.value !== item.filename)
                        setPinchSong(item, text === '' ? '' : match?.value ?? '')
                        // Reset display so an unmatched free-text entry doesn't linger.
                        event.currentTarget.value = pinchTitle(f.info1)
                      }}
                    />

                    <div class="flex items-center justify-center">
                      <input
                        type="checkbox"
                        class="h-4 w-4 accent-[hsl(var(--gradient-from))]"
                        checked={inSoundtest(f)}
                        aria-label={$_('orderTracks.colInSoundtest')}
                        onchange={(event) => {
                          f.in_soundtest = event.currentTarget.checked ? 'True' : 'False'
                        }}
                      />
                    </div>
                  {:else}
                    <!-- Locked row: its current values, read-only, in the same grid as editable rows -->
                    {@const f = item.fields}
                    <span class="{lockedCellClass} font-medium {item.isVanilla ? 'italic' : ''}">{f?.title ?? item.title}</span>
                    <span class={lockedCellClass}>{f ? gameName(f.game) : ''}</span>
                    <span class={lockedCellClass}>{f?.author ?? ''}</span>
                    <span class={lockedCellClass}>{f?.copyright ?? ''}</span>
                    <span class={lockedCellClass}>{f ? recordLabel(f.record_type) : ''}</span>
                    <span class={lockedCellClass}>{f ? pinchTitle(f.info1) : ''}</span>
                    <div class="flex items-center justify-center">
                      <input type="checkbox" class="h-4 w-4" checked={f ? inSoundtest(f) : false} disabled aria-label={$_('orderTracks.colInSoundtest')} />
                    </div>
                  {/if}
                </div>
              {/each}
            </div>
          </div>
        {:else}
          <EmptyState dashed title={$_('orderTracks.emptyTitle')} body={$_('orderTracks.emptyBody')}>
            {#snippet icon()}<Music4 size={20} />{/snippet}
          </EmptyState>
        {/if}
      </div>
    </section>
  </div>
</div>
