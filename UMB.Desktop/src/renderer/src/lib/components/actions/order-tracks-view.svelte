<script lang="ts">
  import { ArrowUpDown, Folder, GripVertical, Lock, Music4, RefreshCw, Save } from '@lucide/svelte'
  import { _ } from 'svelte-i18n'
  import { flip } from 'svelte/animate'
  import { dragHandleZone, dragHandle, type DndEvent } from 'svelte-dnd-action'
  import { modsStore } from '$lib/stores/mods.svelte'
  import type { ModInfo, ModSeriesInfo, TrackOrderData, TrackOrderItem } from '$lib/types/electron'

  const FLIP_MS = 180

  let { activeMod }: { activeMod: ModInfo | null } = $props()

  let loading = $state(false)
  let series = $state<ModSeriesInfo[]>([])
  let orderData = $state<TrackOrderData | null>(null)
  let dndItems = $state<TrackOrderItem[]>([])
  let orderLoading = $state(false)
  let saveState = $state<'idle' | 'saving' | 'saved'>('idle')
  let isDirty = $state(false)
  let pendingSeriesPath = $state<string | null>(null)
  let baselineIds: string[] = []
  let loadToken = 0

  function matchesBaseline(items: TrackOrderItem[]): boolean {
    return items.length === baselineIds.length
      && items.every((item, index) => item.id === baselineIds[index])
  }

  const selectedSeries = $derived(
    series.find((entry) => entry.path === modsStore.activeSeriesPath) ?? null
  )

  $effect(() => {
    dndItems = orderData ? orderData.items : []
  })

  function handleConsider(event: CustomEvent<DndEvent<TrackOrderItem>>) {
    dndItems = event.detail.items
  }

  function handleFinalize(event: CustomEvent<DndEvent<TrackOrderItem>>) {
    const items = event.detail.items
    dndItems = items
    if (!orderData) {
      return
    }

    orderData = { ...orderData, items }
    isDirty = !matchesBaseline(items)
    if (isDirty) {
      saveState = 'idle'
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
    try {
      orderData = await window.electron.umb.loadTrackOrder(seriesPath)
      baselineIds = orderData.items.map((item) => item.id)
      isDirty = false
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
    isDirty = false
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
    orderData = await window.electron.umb.saveTrackOrder(modsStore.activeSeriesPath, orderData.items.map((item) => item.id))
    baselineIds = orderData.items.map((item) => item.id)
    saveState = 'saved'
    isDirty = false
  }

  $effect(() => {
    void loadSeries(activeMod?.path ?? null)
  })

  $effect(() => {
    void loadOrderData(modsStore.activeSeriesPath)
  })
</script>

<div class="flex-1 overflow-hidden">
  <div class="flex h-full min-h-0">
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

        <button
          onclick={handleSave}
          class="shrink-0 inline-flex items-center gap-2 rounded-lg border border-input bg-background px-3 py-2 text-[12.5px] font-medium transition-colors hover:bg-muted disabled:cursor-not-allowed disabled:opacity-60"
          disabled={!orderData || !isDirty || saveState === 'saving'}
        >
          <Save size={14} />
          {saveState === 'saving' ? $_('orderTracks.saving') : saveState === 'saved' ? $_('orderTracks.saved') : $_('orderTracks.save')}
        </button>
      </div>

      <div class="min-h-0 flex-1 overflow-auto p-2">
        {#if orderLoading}
          <div class="grid h-full min-h-[320px] place-items-center rounded-2xl border border-dashed border-border bg-background/60 text-[13px] text-muted-foreground">
            {$_('orderTracks.loadingOrder')}
          </div>
        {:else if orderData}
          <div
            class="grid gap-1.5"
            use:dragHandleZone={{ items: dndItems, flipDurationMs: FLIP_MS, dropTargetStyle: {} }}
            onconsider={handleConsider}
            onfinalize={handleFinalize}
          >
            {#each dndItems as item, index (item.id)}
              <div
                animate:flip={{ duration: FLIP_MS }}
                class="flex items-center gap-2.5 rounded-lg border border-border bg-background/75 px-3 py-1.5 {item.isLocked ? 'opacity-80' : ''}"
                role="listitem"
              >
                {#if item.isLocked}
                  <div class="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-muted/70 text-muted-foreground">
                    <Lock size={14} />
                  </div>
                {:else}
                  <div
                    use:dragHandle
                    aria-label={$_('orderTracks.dragAria')}
                    class="flex h-7 w-7 shrink-0 cursor-grab items-center justify-center rounded-lg bg-muted text-muted-foreground active:cursor-grabbing"
                  >
                    <GripVertical size={15} />
                  </div>
                {/if}
                <div class="flex min-w-0 flex-1 flex-col">
                  <span class="truncate text-[13.5px] font-medium">{item.title}</span>
                  <span class="truncate text-[12px] text-muted-foreground">{item.subtitle}</span>
                </div>
                <div class="flex items-center gap-2">
                  {#if item.isLocked}
                    <span class="rounded-full border border-border bg-background/80 px-2 py-1 text-[10.5px] text-muted-foreground">{$_('orderTracks.locked')}</span>
                  {/if}
                  <span class="rounded-full border border-border bg-muted px-2 py-1 text-[11px] text-muted-foreground">{String(index + 1).padStart(2, '0')}</span>
                </div>
              </div>
            {/each}
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