<script lang="ts">
  import { ArrowUpDown, Folder, GripVertical, Lock, Music4, RefreshCw, Save } from '@lucide/svelte'
  import type { ModInfo, ModSeriesInfo, TrackOrderData, TrackOrderItem } from '$lib/types/electron'

  let { activeMod }: { activeMod: ModInfo | null } = $props()

  let loading = $state(false)
  let series = $state<ModSeriesInfo[]>([])
  let selectedSeriesPath = $state<string | null>(null)
  let orderData = $state<TrackOrderData | null>(null)
  let orderLoading = $state(false)
  let saveState = $state<'idle' | 'saving' | 'saved'>('idle')
  let draggedItemId = $state<string | null>(null)
  let loadToken = 0

  const selectedSeries = $derived(
    series.find((entry) => entry.path === selectedSeriesPath) ?? null
  )

  async function loadSeries(modPath: string | null) {
    loadToken += 1
    const currentToken = loadToken

    if (!modPath) {
      series = []
      selectedSeriesPath = null
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
      if (!nextSeries.some((entry) => entry.path === selectedSeriesPath)) {
        selectedSeriesPath = nextSeries[0]?.path ?? null
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
    } finally {
      orderLoading = false
    }
  }

  function handleReload() {
    void loadSeries(activeMod?.path ?? null)
    void loadOrderData(selectedSeriesPath)
  }

  function moveDraggedItem(targetId: string) {
    if (!orderData || !draggedItemId || draggedItemId === targetId) {
      return
    }

    const sourceIndex = orderData.items.findIndex((item) => item.id === draggedItemId)
    const targetIndex = orderData.items.findIndex((item) => item.id === targetId)
    if (sourceIndex < 0 || targetIndex < 0) {
      return
    }

    const sourceItem = orderData.items[sourceIndex]
    if (sourceItem.isLocked) {
      return
    }

    const nextItems = [...orderData.items]
    nextItems.splice(sourceIndex, 1)
    nextItems.splice(targetIndex, 0, sourceItem)
    orderData = { ...orderData, items: nextItems }
    saveState = 'idle'
  }

  async function handleSave() {
    if (!orderData || !selectedSeriesPath) {
      return
    }

    saveState = 'saving'
    orderData = await window.electron.umb.saveTrackOrder(selectedSeriesPath, orderData.items.map((item) => item.id))
    saveState = 'saved'
  }

  $effect(() => {
    void loadSeries(activeMod?.path ?? null)
  })

  $effect(() => {
    void loadOrderData(selectedSeriesPath)
  })
</script>

<div class="flex-1 overflow-hidden p-6">
  <div class="flex h-full min-h-0 gap-5">
    <section class="w-[280px] shrink-0 rounded-2xl border border-border bg-card overflow-hidden">
      <div class="gradient-strip h-[3px]"></div>
      <div class="border-b border-border px-4 py-3">
        <div class="flex items-center gap-2">
          <div
            class="flex h-9 w-9 items-center justify-center rounded-xl border border-border"
            style="background: linear-gradient(135deg, hsl(var(--gradient-from) / .13), hsl(var(--gradient-to) / .16)); color: hsl(var(--gradient-from));"
          >
            <Folder size={18} />
          </div>
          <div class="min-w-0">
            <h2 class="text-sm font-semibold">Series</h2>
            <p class="truncate text-[12.5px] text-muted-foreground">
              {activeMod?.name ?? 'Select a mod to browse series.'}
            </p>
          </div>
        </div>
      </div>

      <div class="flex items-center justify-between border-b border-border px-4 py-2.5">
        <span class="text-[12px] text-muted-foreground">{series.length} available</span>
        <button
          onclick={handleReload}
          class="inline-flex h-8 w-8 items-center justify-center rounded-lg border border-input bg-background transition-colors hover:bg-muted"
          title="Reload series"
        >
          <RefreshCw size={14} class={loading ? 'animate-spin' : ''} />
        </button>
      </div>

      <div class="min-h-0 overflow-auto p-3">
        {#if !activeMod}
          <div class="rounded-xl border border-dashed border-border bg-background/70 px-3 py-8 text-center text-[13px] text-muted-foreground">
            Choose a mod from the app bar to start.
          </div>
        {:else if loading && series.length === 0}
          <div class="rounded-xl border border-dashed border-border bg-background/70 px-3 py-8 text-center text-[13px] text-muted-foreground">
            Loading series...
          </div>
        {:else if series.length === 0}
          <div class="rounded-xl border border-dashed border-border bg-background/70 px-3 py-8 text-center text-[13px] text-muted-foreground">
            No series with tracks.csv were found in this mod.
          </div>
        {:else}
          <div class="grid gap-2">
            {#each series as item}
              {@const isActive = selectedSeriesPath === item.path}
              <button
                onclick={() => selectedSeriesPath = item.path}
                class="w-full rounded-xl border p-2 text-left transition-colors {isActive ? 'border-transparent' : 'border-border bg-background/70 hover:bg-muted'}"
                style={isActive
                  ? 'background: linear-gradient(135deg, hsl(var(--gradient-from) / .15), hsl(var(--gradient-to) / .18));'
                  : ''}
              >
                <div class="flex items-start justify-between gap-3">
                  <div class="min-w-0">
                    <div class="truncate text-[13.5px] font-semibold">{item.name}</div>
                    <div class="truncate pt-0.5 text-[12px] text-muted-foreground">{item.path.split('\\').at(-1)}</div>
                  </div>
                  <span class="rounded-full border border-border bg-background/80 px-2 py-1 text-[10.5px] text-muted-foreground">
                    Series
                  </span>
                </div>
              </button>
            {/each}
          </div>
        {/if}
      </div>
    </section>

    <section class="min-w-0 flex-1 rounded-2xl border border-border bg-card overflow-hidden">
      <div class="gradient-strip h-[3px]"></div>
      <div class="border-b border-border px-5 py-4 flex items-center justify-between gap-4">
        <div class="flex min-w-0 items-center gap-3">
          <div
            class="flex h-10 w-10 items-center justify-center rounded-xl border border-border"
            style="background: linear-gradient(135deg, hsl(var(--gradient-from) / .13), hsl(var(--gradient-to) / .16)); color: hsl(var(--gradient-from));"
          >
            <ArrowUpDown size={18} />
          </div>
          <div class="min-w-0">
            <h2 class="truncate text-sm font-semibold">{orderData?.seriesName ?? selectedSeries?.name ?? 'Order Tracks'}</h2>
            <p class="truncate text-[12.5px] text-muted-foreground">
              {selectedSeries
                ? orderData?.hasSongOrder
                  ? 'Drag tracks to reorder them. Locked vanilla entries are preserved from song_order.toml.'
                  : 'Drag tracks to reorder the tracks.csv entries for this series.'
                : 'Pick a series from the left to start ordering tracks.'}
            </p>
          </div>
        </div>

        <button
          onclick={handleSave}
          class="shrink-0 inline-flex items-center gap-2 rounded-lg border border-input bg-background px-3 py-2 text-[12.5px] font-medium transition-colors hover:bg-muted disabled:cursor-not-allowed disabled:opacity-60"
          disabled={!orderData || saveState === 'saving'}
        >
          <Save size={14} />
          {saveState === 'saving' ? 'Saving...' : saveState === 'saved' ? 'Saved' : 'Save Order'}
        </button>
      </div>

      <div class="flex-1 overflow-auto p-5">
        {#if orderLoading}
          <div class="grid h-full min-h-[320px] place-items-center rounded-2xl border border-dashed border-border bg-background/60 text-[13px] text-muted-foreground">
            Loading order data...
          </div>
        {:else if orderData}
          <div class="grid gap-3">
            {#each orderData.items as item, index (item.id)}
              <div
                class="flex items-center gap-3 rounded-xl border border-border bg-background/75 px-4 py-3 {item.isLocked ? 'opacity-80' : 'cursor-move'}"
                role="listitem"
                draggable={!item.isLocked}
                ondragstart={() => draggedItemId = item.id}
                ondragend={() => draggedItemId = null}
                ondragover={(event) => event.preventDefault()}
                ondrop={() => moveDraggedItem(item.id)}
              >
                <div class="flex h-8 w-8 items-center justify-center rounded-lg {item.isLocked ? 'bg-muted/70 text-muted-foreground' : 'bg-muted text-muted-foreground'}">
                  {#if item.isLocked}
                    <Lock size={14} />
                  {:else}
                    <GripVertical size={15} />
                  {/if}
                </div>
                <div class="flex min-w-0 flex-1 flex-col">
                  <span class="truncate text-[13.5px] font-medium">{item.title}</span>
                  <span class="truncate text-[12px] text-muted-foreground">{item.subtitle}</span>
                </div>
                <div class="flex items-center gap-2">
                  {#if item.isLocked}
                    <span class="rounded-full border border-border bg-background/80 px-2 py-1 text-[10.5px] text-muted-foreground">Locked</span>
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
                <h3 class="text-sm font-semibold">Pick a series</h3>
                <p class="pt-1 text-[13px] text-muted-foreground">
                  This panel will show the selected series order and drag handles once a series is selected.
                </p>
              </div>
            </div>
          </div>
        {/if}
      </div>
    </section>
  </div>
</div>