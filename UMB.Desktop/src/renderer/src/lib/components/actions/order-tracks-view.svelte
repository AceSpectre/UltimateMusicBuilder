<script lang="ts">
  import { ArrowDown, ArrowUp, ArrowUpDown, GripVertical, Eye, EyeOff, Lock, Music4, Palette, Volume2, Wand2 } from '@lucide/svelte'
  import { _ } from 'svelte-i18n'
  import { flip } from 'svelte/animate'
  import { dragHandleZone, dragHandle, type DndEvent } from 'svelte-dnd-action'
  import { modsStore } from '$lib/stores/mods.svelte'
  import { createSeriesLoader } from '$lib/series-loader'
  import GradientIcon from '$lib/components/ui/gradient-icon.svelte'
  import EmptyState from '$lib/components/ui/empty-state.svelte'
  import SaveButton from '$lib/components/ui/save-button.svelte'
  import SeriesPicker from '$lib/components/ui/series-picker.svelte'
  import Modal from '$lib/components/ui/modal.svelte'
  import { inSoundtest, overlayLegend, overlayStyle, seriesGameIds } from '$lib/song-overlay'
  import { nextSort, sortBy, type SortKey, type SortState } from '$lib/table-sort'
  import type { ModInfo, ModSeriesInfo, TrackOrderData, TrackOrderItem } from '$lib/types/electron'

  const FLIP_MS = 180
  const PINCH_CATEGORY = 'sf_situationlink'
  const RECORD_OPTIONS = ['original', 'arrange', 'new_arrange'] as const
  const GRID_COLUMNS =
    'grid-template-columns: 52px minmax(160px,1.4fr) minmax(130px,1fr) minmax(110px,1fr) minmax(140px,1fr) 116px 60px minmax(160px,1fr) 84px;'
  // Header columns in grid order; `index` is the series' real track order.
  const COLUMNS = [
    { id: 'index', label: 'orderTracks.colIndex' },
    { id: 'title', label: 'orderTracks.colTitle' },
    { id: 'game', label: 'orderTracks.colGame' },
    { id: 'author', label: 'orderTracks.colAuthor' },
    { id: 'copyright', label: 'orderTracks.colCopyright' },
    { id: 'record', label: 'orderTracks.colRecord' },
    { id: 'isPinch', label: 'orderTracks.colIsPinch' },
    { id: 'pinchSong', label: 'orderTracks.colPinchSong' },
    { id: 'soundtest', label: 'orderTracks.colInSoundtest' }
  ] as const
  type SortColumn = (typeof COLUMNS)[number]['id']
  const CENTERED_COLUMNS = new Set<SortColumn>(['isPinch', 'soundtest'])
  const toolbarButtonClass =
    'inline-flex items-center gap-2 rounded-lg border border-input bg-background px-3 py-2 text-[12.5px] font-medium transition-colors hover:bg-muted disabled:cursor-not-allowed disabled:opacity-60'

  let {
    activeMod,
    onNavigate
  }: { activeMod: ModInfo | null; onNavigate?: (actionId: string) => void } = $props()

  let loading = $state(false)
  let series = $state<ModSeriesInfo[]>([])
  let orderData = $state<TrackOrderData | null>(null)
  let dndItems = $state<TrackOrderItem[]>([])
  let orderLoading = $state(false)
  let saveState = $state<'idle' | 'saving' | 'saved'>('idle')
  let pendingSeriesPath = $state<string | null>(null)
  let baselineSnapshot = $state('')
  let selectedItemId = $state<string | null>(null)
  let overlayOn = $state(false)
  let hideVanilla = $state(false)
  let sort = $state<SortState<SortColumn>>({ column: 'index', desc: false })
  const seriesLoader = createSeriesLoader((v) => (loading = v))

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
      .filter((item) => item.fields !== null && item.filename)
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
    isPinch: (item) => isPinchTarget(item),
    pinchSong: (item) => pinchTitle(item.fields?.info1 ?? ''),
    soundtest: (item) => Boolean(item.fields && inSoundtest(item.fields))
  }

  // Sorting and hiding are display-only; dragging needs the full list in real track order.
  const canDrag = $derived(sort.column === 'index' && !sort.desc && !hideVanilla)
  const hasVanilla = $derived(dndItems.some((item) => !item.fields))
  const displayItems = $derived.by(() => {
    if (canDrag) return dndItems
    const visible = hideVanilla ? dndItems.filter((item) => item.fields) : dndItems
    return sortBy(visible, sort.desc, sortKeys[sort.column])
  })

  const selectedSeries = $derived(
    series.find((entry) => entry.path === modsStore.activeSeriesPath) ?? null
  )

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
  const canUseDefaults = $derived(Boolean(orderData?.defaultTrackData) && selectedItem?.fields != null)

  function applyDefaults() {
    const defaults = orderData?.defaultTrackData
    if (!defaults || !selectedItem?.fields) {
      return
    }
    selectedItem.fields.game = defaults.game
    selectedItem.fields.author = defaults.author
    selectedItem.fields.copyright = defaults.copyright
    selectedItem.fields.record_type = defaults.record_type
  }

  function openVolConfig() {
    if (!modsStore.activeSeriesPath) {
      return
    }
    modsStore.pendingConfigVolumePath = modsStore.activeSeriesPath
    onNavigate?.('config-volume')
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

  async function loadSeries(modPath: string | null) {
    if (!modPath) {
      seriesLoader.invalidate()
      series = []
      modsStore.activeSeriesPath = null
      orderData = null
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
    if (path === modsStore.activeSeriesPath) {
      return
    }

    if (isDirty) {
      pendingSeriesPath = path
      return
    }

    modsStore.activeSeriesPath = path
  }

  async function confirmSaveAndSwitch() {
    const target = pendingSeriesPath
    pendingSeriesPath = null
    await handleSave()
    if (target) {
      modsStore.activeSeriesPath = target
    }
  }

  function discardAndSwitch() {
    const target = pendingSeriesPath
    pendingSeriesPath = null
    baselineSnapshot = snapshot(dndItems)
    if (target) {
      modsStore.activeSeriesPath = target
    }
  }

  function cancelSwitch() {
    pendingSeriesPath = null
  }

  function handleReload() {
    void loadSeries(activeMod?.path ?? null)
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
    void loadSeries(activeMod?.path ?? null)
  })

  $effect(() => {
    void loadOrderData(modsStore.activeSeriesPath)
  })

  function recordLabel(value: string): string {
    if (value === 'arrange') return $_('orderTracks.recordArrange')
    if (value === 'new_arrange') return $_('orderTracks.recordNewArrange')
    return $_('orderTracks.recordOriginal')
  }

  const inputClass =
    'w-full rounded border border-input bg-background px-1.5 py-0.5 text-[12.5px] outline-none focus:border-ring focus:ring-1 focus:ring-ring disabled:cursor-not-allowed disabled:opacity-50'
  // Multiline: credits support newlines in-game.
  const textareaClass = `${inputClass} resize-none overflow-hidden whitespace-pre [field-sizing:content]`
</script>

<div class="flex-1 overflow-hidden">
  <div class="flex h-full min-h-0">
    <SeriesPicker
      {activeMod}
      {series}
      {loading}
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
          <h2 class="min-w-0 truncate text-sm font-semibold">{orderData?.seriesName ?? selectedSeries?.name ?? $_('orderTracks.title')}</h2>
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
            onclick={() => (hideVanilla = !hideVanilla)}
            class="{toolbarButtonClass} w-[130px] justify-center"
            disabled={!hasVanilla}
            aria-pressed={hideVanilla}
          >
            {#if hideVanilla}<Eye size={14} />{:else}<EyeOff size={14} />{/if}
            {$_(hideVanilla ? 'orderTracks.showVanilla' : 'orderTracks.hideVanilla')}
          </button>

          <button
            onclick={openVolConfig}
            class={toolbarButtonClass}
            disabled={!orderData}
            title={$_('orderTracks.volConfigHint')}
          >
            <Volume2 size={14} />
            {$_('orderTracks.volConfig')}
          </button>

          <button
            onclick={applyDefaults}
            class={toolbarButtonClass}
            disabled={!canUseDefaults}
            title={$_('orderTracks.useDefaultsHint')}
          >
            <Wand2 size={14} />
            {$_('orderTracks.useDefaults')}
          </button>

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
                {@const isSelected = !item.isLocked && item.id === selectedItemId}
                <div
                  animate:flip={{ duration: FLIP_MS }}
                  class="grid items-center gap-1 rounded border px-1 py-0.5 {item.isLocked ? 'border-border bg-background/75 opacity-80' : isSelected ? 'border-transparent bg-background ring-1 ring-[hsl(var(--gradient-from))]' : 'border-border bg-background/75'}"
                  style="{GRID_COLUMNS} {overlayOn ? overlayStyle(item, gameIds) : ''}"
                  role="listitem"
                  onfocusincapture={() => { if (!item.isLocked) selectedItemId = item.id }}
                  onpointerdown={() => { if (!item.isLocked) selectedItemId = item.id }}
                >
                  <div class="flex items-center gap-1">
                    <span class="w-6 text-right text-[11px] tabular-nums text-muted-foreground">{dndItems.indexOf(item) + 1}</span>
                    {#if item.isLocked}
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

                  {#if item.fields}
                    {@const f = item.fields}
                    <textarea
                      class={textareaClass}
                      bind:value={f.title}
                      aria-label={$_('orderTracks.colTitle')}
                    ></textarea>

                    {#if orderData.games.length > 0}
                      <select class={inputClass} bind:value={f.game} aria-label={$_('orderTracks.colGame')}>
                        {#each orderData.games as game}
                          <option value={game.id}>{game.name}</option>
                        {/each}
                        {#if !orderData.games.some((g) => g.id === f.game)}
                          <option value={f.game}>{f.game}</option>
                        {/if}
                      </select>
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

                    <div class="flex items-center justify-center">
                      <input
                        type="checkbox"
                        class="h-4 w-4 accent-[hsl(var(--gradient-from))]"
                        checked={isPinchTarget(item)}
                        disabled
                        aria-label={$_('orderTracks.isPinchAria')}
                        title={isPinchTarget(item) ? $_('orderTracks.pinchTargetHint') : ''}
                      />
                    </div>

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
                    <!-- Locked vanilla row: read-only, same grid layout/height as editable rows -->
                    <div class="truncate rounded border border-transparent px-1.5 py-0.5 text-[12.5px] font-medium" title={item.title}>{item.title}</div>
                    <span class="truncate self-center px-1.5 text-[11px] uppercase tracking-wide text-muted-foreground">{$_('orderTracks.vanillaTag')}</span>
                    <span></span>
                    <span></span>
                    <span></span>
                    <span></span>
                    <span></span>
                    <span class="justify-self-center self-center rounded-full border border-border bg-background/80 px-2 py-0.5 text-[10.5px] text-muted-foreground">{$_('orderTracks.locked')}</span>
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

{#if pendingSeriesPath}
  <Modal>
      <div class="px-5 py-4">
        <h3 class="text-sm font-semibold">{$_('orderTracks.unsavedTitle')}</h3>
        <p class="pt-1.5 text-[13px] text-muted-foreground">
          {$_('orderTracks.unsavedBodyBefore')}
          <span class="font-medium text-foreground">{orderData?.seriesName ?? selectedSeries?.name}</span>{$_('orderTracks.unsavedBodyAfter')}
        </p>
      </div>
      <div class="flex flex-col gap-2 border-t border-border px-5 py-4">
        <button
          onclick={confirmSaveAndSwitch}
          class="inline-flex items-center justify-center rounded-lg border border-input bg-background px-3 py-2 text-[12.5px] font-medium transition-colors hover:bg-muted"
        >
          {$_('orderTracks.saveAndSwitch')}
        </button>
        <button
          onclick={discardAndSwitch}
          class="inline-flex items-center justify-center rounded-lg border border-input bg-background px-3 py-2 text-[12.5px] font-medium transition-colors hover:bg-muted"
        >
          {$_('orderTracks.discard')}
        </button>
        <button
          onclick={cancelSwitch}
          class="inline-flex items-center justify-center rounded-lg px-3 py-2 text-[12.5px] font-medium text-muted-foreground transition-colors hover:bg-muted"
        >
          {$_('orderTracks.continue')}
        </button>
      </div>
  </Modal>
{/if}
