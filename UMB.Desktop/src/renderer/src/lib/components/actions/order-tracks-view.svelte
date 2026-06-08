<script lang="ts">
  import { ArrowUpDown, Folder, GripVertical, Lock, Music4, PanelLeftClose, PanelLeftOpen, RefreshCw, Save, Volume2, Wand2 } from '@lucide/svelte'
  import { _ } from 'svelte-i18n'
  import { flip } from 'svelte/animate'
  import { dragHandleZone, dragHandle, type DndEvent } from 'svelte-dnd-action'
  import { modsStore } from '$lib/stores/mods.svelte'
  import type { ModInfo, ModSeriesInfo, TrackOrderData, TrackOrderItem } from '$lib/types/electron'

  const FLIP_MS = 180
  const PINCH_CATEGORY = 'sf_situationlink'
  const RECORD_OPTIONS = ['original', 'arrange', 'new_arrange'] as const

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
  let seriesCollapsed = $state(false)
  let selectedItemId = $state<string | null>(null)
  let loadToken = 0

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

  // Clear "saved" once the user starts editing again.
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
    loadToken += 1
    const currentToken = loadToken

    if (!modPath) {
      series = []
      modsStore.activeSeriesPath = null
      orderData = null
      return
    }

    loading = true
    try {
      const nextSeries = await window.electron.umb.listModSeries(modPath)
      if (currentToken !== loadToken) {
        return
      }

      series = nextSeries
      if (!nextSeries.some((entry) => entry.path === modsStore.activeSeriesPath)) {
        modsStore.activeSeriesPath = nextSeries[0]?.path ?? null
      }
    } finally {
      if (currentToken === loadToken) {
        loading = false
      }
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
</script>

<div class="flex-1 overflow-hidden">
  <div class="flex h-full min-h-0">
    <section
      class="flex h-full min-h-0 shrink-0 flex-col border border-border bg-card overflow-hidden transition-[width] duration-150 {seriesCollapsed ? 'w-[46px]' : 'w-[280px]'}"
    >
      <div class="gradient-strip h-[3px] shrink-0"></div>
      {#if seriesCollapsed}
        <div class="flex flex-1 flex-col items-center gap-2 py-3">
          <button
            onclick={() => (seriesCollapsed = false)}
            class="inline-flex h-8 w-8 items-center justify-center rounded-lg border border-input bg-background transition-colors hover:bg-muted"
            title={$_('orderTracks.expandSeries')}
          >
            <PanelLeftOpen size={15} />
          </button>
          <div
            class="flex h-8 w-8 items-center justify-center rounded-lg border border-border"
            style="background: linear-gradient(135deg, hsl(var(--gradient-from) / .13), hsl(var(--gradient-to) / .16)); color: hsl(var(--gradient-from));"
          >
            <Folder size={16} />
          </div>
          <span class="text-[11px] text-muted-foreground">{series.length}</span>
        </div>
      {:else}
        <div class="shrink-0 border-b border-border px-4 py-3">
          <div class="flex items-center gap-2">
            <div
              class="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl border border-border"
              style="background: linear-gradient(135deg, hsl(var(--gradient-from) / .13), hsl(var(--gradient-to) / .16)); color: hsl(var(--gradient-from));"
            >
              <Folder size={18} />
            </div>
            <div class="min-w-0 flex-1">
              <h2 class="text-sm font-semibold">{$_('orderTracks.seriesHeading')}</h2>
              <p class="truncate text-[12.5px] text-muted-foreground">
                {activeMod?.name ?? $_('orderTracks.selectModSubtitle')}
              </p>
            </div>
            <span class="shrink-0 text-[12px] text-muted-foreground">{series.length}</span>
            <button
              onclick={handleReload}
              class="inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-lg border border-input bg-background transition-colors hover:bg-muted"
              title={$_('orderTracks.reload')}
            >
              <RefreshCw size={14} class={loading ? 'animate-spin' : ''} />
            </button>
            <button
              onclick={() => (seriesCollapsed = true)}
              class="inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-lg border border-input bg-background transition-colors hover:bg-muted"
              title={$_('orderTracks.collapseSeries')}
            >
              <PanelLeftClose size={14} />
            </button>
          </div>
        </div>

        <div class="min-h-0 flex-1 overflow-auto p-2">
          {#if !activeMod}
          <div class="rounded-xl border border-dashed border-border bg-background/70 px-3 py-8 text-center text-[13px] text-muted-foreground">
            {$_('orderTracks.chooseMod')}
          </div>
        {:else if loading && series.length === 0}
          <div class="rounded-xl border border-dashed border-border bg-background/70 px-3 py-8 text-center text-[13px] text-muted-foreground">
            {$_('orderTracks.loadingSeries')}
          </div>
        {:else if series.length === 0}
          <div class="rounded-xl border border-dashed border-border bg-background/70 px-3 py-8 text-center text-[13px] text-muted-foreground">
            {$_('orderTracks.noSeries')}
          </div>
        {:else}
          <div class="grid gap-1.5">
            {#each series as item}
              {@const isActive = modsStore.activeSeriesPath === item.path}
              <button
                onclick={() => requestSelectSeries(item.path)}
                class="w-full rounded-lg border px-2 py-1.5 text-left transition-colors {isActive ? 'border-transparent' : 'border-border bg-background/70 hover:bg-muted'}"
                style={isActive
                  ? 'background: linear-gradient(135deg, hsl(var(--gradient-from) / .15), hsl(var(--gradient-to) / .18));'
                  : ''}
              >
                <div class="flex items-start justify-between gap-3">
                  <div class="flex flex-row justify-content-evenly gap-3">
                    <div class="truncate text-[13.5px] font-semibold">{item.name}</div>
                  </div>
                </div>
              </button>
            {/each}
          </div>
          {/if}
        </div>
      {/if}
    </section>

    <section class="flex h-full min-h-0 min-w-0 flex-1 flex-col border border-border bg-card overflow-hidden">
      <div class="gradient-strip h-[3px] shrink-0"></div>
      <div class="shrink-0 border-b border-border px-4 py-3 flex items-center justify-between gap-4">
        <div class="flex min-w-0 items-center gap-3">
          <div
            class="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl border border-border"
            style="background: linear-gradient(135deg, hsl(var(--gradient-from) / .13), hsl(var(--gradient-to) / .16)); color: hsl(var(--gradient-from));"
          >
            <ArrowUpDown size={18} />
          </div>
          <div class="min-w-0">
            <h2 class="truncate text-sm font-semibold">{orderData?.seriesName ?? selectedSeries?.name ?? $_('orderTracks.title')}</h2>
            <p class="truncate text-[12.5px] text-muted-foreground">
              {selectedSeries
                ? orderData?.hasSongOrder
                  ? $_('orderTracks.dragHintLocked')
                  : $_('orderTracks.dragHint')
                : $_('orderTracks.pickSeriesHint')}
            </p>
          </div>
        </div>

        <div class="flex shrink-0 items-center gap-2">
          <button
            onclick={openVolConfig}
            class="inline-flex items-center gap-2 rounded-lg border border-input bg-background px-3 py-2 text-[12.5px] font-medium transition-colors hover:bg-muted disabled:cursor-not-allowed disabled:opacity-60"
            disabled={!orderData}
            title={$_('orderTracks.volConfigHint')}
          >
            <Volume2 size={14} />
            {$_('orderTracks.volConfig')}
          </button>

          <button
            onclick={applyDefaults}
            class="inline-flex items-center gap-2 rounded-lg border border-input bg-background px-3 py-2 text-[12.5px] font-medium transition-colors hover:bg-muted disabled:cursor-not-allowed disabled:opacity-60"
            disabled={!canUseDefaults}
            title={$_('orderTracks.useDefaultsHint')}
          >
            <Wand2 size={14} />
            {$_('orderTracks.useDefaults')}
          </button>

          <button
            onclick={handleSave}
            class="inline-flex items-center gap-2 rounded-lg border border-input bg-background px-3 py-2 text-[12.5px] font-medium transition-colors hover:bg-muted disabled:cursor-not-allowed disabled:opacity-60"
            disabled={!orderData || !isDirty || saveState === 'saving'}
          >
            <Save size={14} />
            {saveState === 'saving' ? $_('orderTracks.saving') : saveState === 'saved' ? $_('orderTracks.saved') : $_('orderTracks.save')}
          </button>
        </div>
      </div>

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
            <!-- Header row -->
            <div
              class="sticky z-10 grid items-center gap-1 border-b border-border bg-card px-1 py-1 text-[11px] font-semibold uppercase tracking-wide text-muted-foreground"
              style="grid-template-columns: 24px minmax(160px,1.4fr) minmax(130px,1fr) minmax(110px,1fr) minmax(140px,1fr) 116px 60px minmax(160px,1fr) 84px; top: -5px"
            >
              <span></span>
              <span>{$_('orderTracks.colTitle')}</span>
              <span>{$_('orderTracks.colGame')}</span>
              <span>{$_('orderTracks.colAuthor')}</span>
              <span>{$_('orderTracks.colCopyright')}</span>
              <span>{$_('orderTracks.colRecord')}</span>
              <span class="text-center">{$_('orderTracks.colIsPinch')}</span>
              <span>{$_('orderTracks.colPinchSong')}</span>
              <span class="text-center">{$_('orderTracks.colInSoundtest')}</span>
            </div>

            <div
              class="grid gap-0.5"
              use:dragHandleZone={{ items: dndItems, flipDurationMs: FLIP_MS, dropTargetStyle: {} }}
              onconsider={handleConsider}
              onfinalize={handleFinalize}
            >
              {#each dndItems as item, index (item.id)}
                {@const isSelected = !item.isLocked && item.id === selectedItemId}
                <div
                  animate:flip={{ duration: FLIP_MS }}
                  class="grid items-center gap-1 rounded border px-1 py-0.5 {item.isLocked ? 'border-border bg-background/75 opacity-80' : isSelected ? 'border-transparent bg-background ring-1 ring-[hsl(var(--gradient-from))]' : 'border-border bg-background/75'}"
                  style="grid-template-columns: 24px minmax(160px,1.4fr) minmax(130px,1fr) minmax(110px,1fr) minmax(140px,1fr) 116px 60px minmax(160px,1fr) 84px;"
                  role="listitem"
                  onfocusincapture={() => { if (!item.isLocked) selectedItemId = item.id }}
                  onpointerdown={() => { if (!item.isLocked) selectedItemId = item.id }}
                >
                  {#if item.isLocked}
                    <div class="flex h-6 w-6 shrink-0 items-center justify-center rounded bg-muted/70 text-muted-foreground" title={$_('orderTracks.locked')}>
                      <Lock size={13} />
                    </div>
                  {:else}
                    <div
                      use:dragHandle
                      aria-label={$_('orderTracks.dragAria')}
                      class="flex h-6 w-6 shrink-0 cursor-grab items-center justify-center rounded bg-muted text-muted-foreground active:cursor-grabbing"
                    >
                      <GripVertical size={14} />
                    </div>
                  {/if}

                  {#if item.fields}
                    {@const f = item.fields}
                    <input
                      type="text"
                      class={inputClass}
                      bind:value={f.title}
                      aria-label={$_('orderTracks.colTitle')}
                    />

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

                    <input type="text" class={inputClass} bind:value={f.author} aria-label={$_('orderTracks.colAuthor')} />
                    <input type="text" class={inputClass} bind:value={f.copyright} aria-label={$_('orderTracks.colCopyright')} />

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
                      value={pinchOptions.find((o) => o.value === f.info1)?.title ?? f.info1}
                      onchange={(event) => {
                        const text = event.currentTarget.value.trim()
                        const match = pinchOptions.find((o) => o.title === text && o.value !== item.filename)
                        setPinchSong(item, text === '' ? '' : match?.value ?? '')
                        // Reset display so an unmatched free-text entry doesn't linger.
                        event.currentTarget.value = pinchOptions.find((o) => o.value === f.info1)?.title ?? f.info1
                      }}
                    />

                    <div class="flex items-center justify-center">
                      <input
                        type="checkbox"
                        class="h-4 w-4 accent-[hsl(var(--gradient-from))]"
                        checked={f.in_soundtest.toLowerCase() === 'true'}
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
          <div class="grid h-full min-h-[320px] place-items-center rounded-2xl border border-dashed border-border bg-background/60">
            <div class="flex max-w-[340px] flex-col items-center gap-3 px-6 text-center">
              <div
                class="flex h-12 w-12 items-center justify-center rounded-2xl border border-border"
                style="background: linear-gradient(135deg, hsl(var(--gradient-from) / .13), hsl(var(--gradient-to) / .16)); color: hsl(var(--gradient-from));"
              >
                <Music4 size={20} />
              </div>
              <div>
                <h3 class="text-sm font-semibold">{$_('orderTracks.emptyTitle')}</h3>
                <p class="pt-1 text-[13px] text-muted-foreground">
                  {$_('orderTracks.emptyBody')}
                </p>
              </div>
            </div>
          </div>
        {/if}
      </div>
    </section>
  </div>
</div>

{#if pendingSeriesPath}
  <div class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-6">
    <div class="w-full max-w-[400px] rounded-2xl border border-border bg-card shadow-xl overflow-hidden">
      <div class="gradient-strip h-[3px]"></div>
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
    </div>
  </div>
{/if}
