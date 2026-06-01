<script lang="ts">
  import { ListOrdered, GripVertical, Image, RefreshCw, Save } from '@lucide/svelte'
  import { _ } from 'svelte-i18n'
  import { flip } from 'svelte/animate'
  import { dragHandleZone, dragHandle, type DndEvent } from 'svelte-dnd-action'
  import type { ModInfo, SeriesOrderData, SeriesOrderItem } from '$lib/types/electron'

  const FLIP_MS = 180

  let { activeMod }: { activeMod: ModInfo | null } = $props()

  let loading = $state(false)
  let orderData = $state<SeriesOrderData | null>(null)
  let dndItems = $state<SeriesOrderItem[]>([])
  let saveState = $state<'idle' | 'saving' | 'saved'>('idle')
  let isDirty = $state(false)
  let baselineIds: string[] = []

  function matchesBaseline(items: SeriesOrderItem[]): boolean {
    return items.length === baselineIds.length
      && items.every((item, index) => item.id === baselineIds[index])
  }

  $effect(() => {
    dndItems = orderData ? orderData.items : []
  })

  function handleConsider(event: CustomEvent<DndEvent<SeriesOrderItem>>) {
    dndItems = event.detail.items
  }

  function handleFinalize(event: CustomEvent<DndEvent<SeriesOrderItem>>) {
    const items = event.detail.items
    dndItems = items
    if (!orderData) return

    orderData = { ...orderData, items }
    isDirty = !matchesBaseline(items)
    if (isDirty) {
      saveState = 'idle'
    }
  }

  async function loadOrder(modPath: string | null) {
    if (!modPath) {
      orderData = null
      return
    }

    loading = true
    saveState = 'idle'
    try {
      orderData = await window.electron.umb.loadSeriesOrder(modPath)
      baselineIds = orderData.items.map((item) => item.id)
      isDirty = false
    } finally {
      loading = false
    }
  }

  function handleReload() {
    void loadOrder(activeMod?.path ?? null)
  }

  async function handleSave() {
    if (!orderData || !activeMod) return

    saveState = 'saving'
    orderData = await window.electron.umb.saveSeriesOrder(activeMod.path, orderData.items.map((item) => item.id))
    baselineIds = orderData.items.map((item) => item.id)
    saveState = 'saved'
    isDirty = false
  }

  $effect(() => {
    void loadOrder(activeMod?.path ?? null)
  })
</script>

<div class="flex-1 overflow-hidden">
  <div class="flex h-full min-h-0 flex-col">
    <section class="flex h-full min-h-0 flex-1 flex-col border border-border bg-card overflow-hidden">
      <div class="gradient-strip h-[3px] shrink-0"></div>
      <div class="shrink-0 border-b border-border px-4 py-3 flex items-center justify-between gap-4">
        <div class="flex min-w-0 items-center gap-3">
          <div
            class="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl border border-border"
            style="background: linear-gradient(135deg, hsl(var(--gradient-from) / .13), hsl(var(--gradient-to) / .16)); color: hsl(var(--gradient-from));"
          >
            <ListOrdered size={18} />
          </div>
          <div class="min-w-0">
            <h2 class="truncate text-sm font-semibold">{$_('orderSeries.title')}</h2>
            <p class="truncate text-[12.5px] text-muted-foreground">
              {orderData
                ? $_('orderSeries.dragHint')
                : activeMod
                  ? $_('orderSeries.noCustomSeries')
                  : $_('orderSeries.chooseMod')}
            </p>
          </div>
        </div>

        <div class="flex items-center gap-2">
          <button
            onclick={handleReload}
            class="inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-lg border border-input bg-background transition-colors hover:bg-muted"
            title={$_('orderSeries.reload')}
          >
            <RefreshCw size={14} class={loading ? 'animate-spin' : ''} />
          </button>

          <button
            onclick={handleSave}
            class="shrink-0 inline-flex items-center gap-2 rounded-lg border border-input bg-background px-3 py-2 text-[12.5px] font-medium transition-colors hover:bg-muted disabled:cursor-not-allowed disabled:opacity-60"
            disabled={!orderData || !isDirty || saveState === 'saving'}
          >
            <Save size={14} />
            {saveState === 'saving' ? $_('orderSeries.saving') : saveState === 'saved' ? $_('orderSeries.saved') : $_('orderSeries.save')}
          </button>
        </div>
      </div>

      <div class="min-h-0 flex-1 overflow-auto p-4">
        {#if loading}
          <div class="grid h-full min-h-[320px] place-items-center rounded-2xl border border-dashed border-border bg-background/60 text-[13px] text-muted-foreground">
            {$_('orderSeries.loading')}
          </div>
        {:else if orderData && orderData.items.length > 0}
          <div
            class="grid gap-3"
            style="grid-template-columns: repeat(5, 128px);"
            use:dragHandleZone={{ items: dndItems, flipDurationMs: FLIP_MS, dropTargetStyle: {} }}
            onconsider={handleConsider}
            onfinalize={handleFinalize}
          >
            {#each dndItems as item (item.id)}
              <div
                animate:flip={{ duration: FLIP_MS }}
                class="w-[128px] h-[148px] flex flex-col items-center rounded-xl border border-border bg-background/75 p-2 transition-shadow hover:shadow-md"
                role="listitem"
              >
                <div
                  use:dragHandle
                  aria-label={$_('orderSeries.dragAria')}
                  class="w-full flex justify-center cursor-grab active:cursor-grabbing mb-1"
                >
                  <GripVertical size={14} class="text-muted-foreground opacity-60" />
                </div>

                <div class="w-[80px] h-[80px] rounded-lg border border-border bg-muted/30 flex items-center justify-center overflow-hidden shrink-0">
                  {#if item.iconDataUrl}
                    <img
                      src={item.iconDataUrl}
                      alt={item.name}
                      class="w-full h-full object-contain invert dark:invert-0"
                      draggable="false"
                    />
                  {:else}
                    <Image size={28} class="text-muted-foreground/40" />
                  {/if}
                </div>

                <div class="mt-1.5 w-full text-center">
                  <span class="block truncate text-[12px] font-medium" title={item.name}>{item.name}</span>
                </div>
              </div>
            {/each}
          </div>
        {:else if orderData && orderData.items.length === 0}
          <div class="grid h-full min-h-[320px] place-items-center rounded-2xl border border-dashed border-border bg-background/60">
            <div class="flex max-w-[340px] flex-col items-center gap-3 px-6 text-center">
              <div
                class="flex h-12 w-12 items-center justify-center rounded-2xl border border-border"
                style="background: linear-gradient(135deg, hsl(var(--gradient-from) / .13), hsl(var(--gradient-to) / .16)); color: hsl(var(--gradient-from));"
              >
                <ListOrdered size={20} />
              </div>
              <div>
                <h3 class="text-sm font-semibold">{$_('orderSeries.emptyTitle')}</h3>
                <p class="pt-1 text-[13px] text-muted-foreground">
                  {$_('orderSeries.emptyBody')}
                </p>
              </div>
            </div>
          </div>
        {:else}
          <div class="grid h-full min-h-[320px] place-items-center rounded-2xl border border-dashed border-border bg-background/60">
            <div class="flex max-w-[340px] flex-col items-center gap-3 px-6 text-center">
              <div
                class="flex h-12 w-12 items-center justify-center rounded-2xl border border-border"
                style="background: linear-gradient(135deg, hsl(var(--gradient-from) / .13), hsl(var(--gradient-to) / .16)); color: hsl(var(--gradient-from));"
              >
                <ListOrdered size={20} />
              </div>
              <div>
                <h3 class="text-sm font-semibold">{$_('orderSeries.chooseMod')}</h3>
                <p class="pt-1 text-[13px] text-muted-foreground">
                  {$_('orderSeries.chooseModBody')}
                </p>
              </div>
            </div>
          </div>
        {/if}
      </div>
    </section>
  </div>
</div>
