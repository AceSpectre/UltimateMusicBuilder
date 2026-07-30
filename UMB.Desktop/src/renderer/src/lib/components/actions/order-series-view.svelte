<script lang="ts">
  import { ListOrdered, GripVertical, Gamepad2, Image, Plus, RefreshCw, Save, Settings2, Upload, X } from '@lucide/svelte'
  import { _ } from 'svelte-i18n'
  import { flip } from 'svelte/animate'
  import { dragHandleZone, dragHandle, type DndEvent } from 'svelte-dnd-action'
  import type { ModInfo, SeriesOrderData, SeriesOrderItem } from '$lib/types/electron'

  const FLIP_MS = 180
  const RECORD_OPTIONS = ['original', 'arrange', 'new_arrange'] as const

  let { activeMod }: { activeMod: ModInfo | null } = $props()

  let panelTab = $state<'settings' | 'games'>('settings')
  let showAddGame = $state(false)
  let newGameId = $state('')
  let newGameName = $state('')

  // Mirrors recordLabel in order-tracks-view.
  function recordLabel(value: string): string {
    if (value === 'arrange') return $_('orderTracks.recordArrange')
    if (value === 'new_arrange') return $_('orderTracks.recordNewArrange')
    return $_('orderTracks.recordOriginal')
  }

  let loading = $state(false)
  let orderData = $state<SeriesOrderData | null>(null)
  let dndItems = $state<SeriesOrderItem[]>([])
  let saveState = $state<'idle' | 'saving' | 'saved'>('idle')
  let baselineSnapshot = $state('')
  let selectedItemId = $state<string | null>(null)

  function snapshot(items: SeriesOrderItem[]): string {
    return JSON.stringify(items.map((item) => ({ id: item.id, fields: item.fields })))
  }

  const isDirty = $derived(Boolean(orderData) && snapshot(dndItems) !== baselineSnapshot)
  const selectedItem = $derived(dndItems.find((item) => item.id === selectedItemId) ?? null)

  const trimmedNewGameId = $derived(newGameId.trim())
  const newGameValid = $derived(
    Boolean(trimmedNewGameId) &&
      Boolean(newGameName.trim()) &&
      !(selectedItem?.fields.games ?? []).some((game) => game.id === trimmedNewGameId)
  )

  function openAddGame() {
    newGameId = ''
    newGameName = ''
    showAddGame = true
  }

  function confirmAddGame() {
    if (!selectedItem || !newGameValid) return
    selectedItem.fields.games = [...selectedItem.fields.games, { id: trimmedNewGameId, name: newGameName.trim() }]
    showAddGame = false
  }

  const SERIES_ID_RE = /^[a-z0-9_]+$/

  let showNewSeries = $state(false)
  let nsId = $state('')
  let nsName = $state('')
  let nsPlaylist = $state('')
  let nsPlaylistDirty = $state(false)
  let nsGames = $state<{ id: string; name: string }[]>([])
  let nsGameId = $state('')
  let nsGameName = $state('')
  let nsError = $state<string | null>(null)
  let nsCreating = $state(false)
  let nsIconDataUrl = $state<string | null>(null)

  let panelIconBusy = $state(false)
  let panelIconError = $state<string | null>(null)

  function readPngDataUrl(file: File): Promise<string> {
    return new Promise((resolve, reject) => {
      if (file.type !== 'image/png') {
        reject(new Error('not-png'))
        return
      }
      const reader = new FileReader()
      reader.onload = () => resolve(reader.result as string)
      reader.onerror = () => reject(reader.error ?? new Error('read-failed'))
      reader.readAsDataURL(file)
    })
  }

  async function nsPickIcon(event: Event) {
    const input = event.target as HTMLInputElement
    const file = input.files?.[0]
    input.value = '' // allow re-picking the same file
    if (!file) return
    try {
      nsIconDataUrl = await readPngDataUrl(file)
      nsError = null
    } catch {
      nsError = $_('orderSeries.iconNotPng')
    }
  }

  async function panelPickIcon(event: Event) {
    const input = event.target as HTMLInputElement
    const file = input.files?.[0]
    input.value = ''
    if (!file || !selectedItem || !activeMod) return
    panelIconError = null
    let dataUrl: string
    try {
      dataUrl = await readPngDataUrl(file)
    } catch {
      panelIconError = $_('orderSeries.iconNotPng')
      return
    }
    panelIconBusy = true
    try {
      selectedItem.iconDataUrl = await window.electron.umb.setSeriesIcon(activeMod.path, selectedItem.seriesId, dataUrl)
    } catch (error) {
      panelIconError = error instanceof Error ? error.message : String(error)
    } finally {
      panelIconBusy = false
    }
  }

  const nsTrimmedId = $derived(nsId.trim())
  const nsIdValid = $derived(SERIES_ID_RE.test(nsTrimmedId) && nsTrimmedId !== 'etc')
  const nsIdTaken = $derived((orderData?.items ?? []).some((item) => item.seriesId === nsTrimmedId))
  const nsTrimmedGameId = $derived(nsGameId.trim())
  const nsGameValid = $derived(
    Boolean(nsTrimmedGameId) && Boolean(nsGameName.trim()) && !nsGames.some((game) => game.id === nsTrimmedGameId)
  )
  const nsCanCreate = $derived(
    nsIdValid && !nsIdTaken && Boolean(nsName.trim()) && nsGames.length > 0 && !nsCreating
  )

  // Auto-fill the playlist as bgm_<id> until the user edits it themselves.
  $effect(() => {
    const id = nsTrimmedId
    if (!nsPlaylistDirty) nsPlaylist = id ? `bgm_${id}` : ''
  })

  function openNewSeries() {
    nsId = ''
    nsName = ''
    nsPlaylist = ''
    nsPlaylistDirty = false
    nsGames = []
    nsGameId = ''
    nsGameName = ''
    nsError = null
    nsCreating = false
    nsIconDataUrl = null
    showNewSeries = true
  }

  function nsAddGame() {
    if (!nsGameValid) return
    nsGames = [...nsGames, { id: nsTrimmedGameId, name: nsGameName.trim() }]
    nsGameId = ''
    nsGameName = ''
  }

  function nsRemoveGame(id: string) {
    nsGames = nsGames.filter((game) => game.id !== id)
  }

  async function confirmNewSeries() {
    if (!activeMod || !nsCanCreate) return
    nsCreating = true
    nsError = null
    try {
      const result = await window.electron.umb.createSeries(activeMod.path, {
        seriesId: nsTrimmedId,
        name: nsName.trim(),
        seriesPlaylist: nsPlaylist.trim(),
        games: nsGames.map((game) => ({ ...game })),
        iconDataUrl: nsIconDataUrl
      })
      orderData = result
      baselineSnapshot = snapshot(result.items)
      selectedItemId = result.items.find((item) => item.seriesId === nsTrimmedId)?.id ?? null
      showNewSeries = false
    } catch (error) {
      nsError = error instanceof Error ? error.message : String(error)
    } finally {
      nsCreating = false
    }
  }

  $effect(() => {
    dndItems = orderData ? orderData.items : []
  })

  $effect(() => {
    if (isDirty && saveState === 'saved') saveState = 'idle'
  })

  $effect(() => {
    void selectedItemId
    panelIconError = null
  })

  function handleConsider(event: CustomEvent<DndEvent<SeriesOrderItem>>) {
    dndItems = event.detail.items
  }

  function handleFinalize(event: CustomEvent<DndEvent<SeriesOrderItem>>) {
    dndItems = event.detail.items
    if (orderData) orderData = { ...orderData, items: event.detail.items }
  }

  async function loadOrder(modPath: string | null) {
    if (!modPath) {
      orderData = null
      return
    }

    loading = true
    saveState = 'idle'
    selectedItemId = null
    panelTab = 'settings'
    showAddGame = false
    try {
      orderData = await window.electron.umb.loadSeriesOrder(modPath)
      baselineSnapshot = snapshot(orderData.items)
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
    // Plain objects — Svelte $state proxies cannot be structured-cloned over IPC. Deep-copy the
    // nested games array too (a shallow spread would leave it a proxy and the IPC clone would fail).
    const payload = dndItems.map((item) => ({
      id: item.id,
      fields: { ...item.fields, games: item.fields.games.map((game) => ({ ...game })) }
    }))
    try {
      orderData = await window.electron.umb.saveSeriesOrder(activeMod.path, payload)
      baselineSnapshot = snapshot(orderData.items)
      saveState = 'saved'
    } catch (error) {
      console.error('saveSeriesOrder failed', error)
      saveState = 'idle'
    }
  }

  $effect(() => {
    void loadOrder(activeMod?.path ?? null)
  })

  const inputClass =
    'w-full rounded-md border border-input bg-background px-2 py-1.5 text-[13px] outline-none focus:border-ring focus:ring-1 focus:ring-ring'
</script>

<div class="flex-1 overflow-hidden">
  <div class="flex h-full min-h-0">
    <section class="flex h-full min-h-0 min-w-0 flex-1 flex-col border border-border bg-card overflow-hidden">
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
            onclick={openNewSeries}
            disabled={!activeMod}
            class="shrink-0 inline-flex items-center gap-2 rounded-lg border border-input bg-background px-3 py-2 text-[12.5px] font-medium transition-colors hover:bg-muted disabled:cursor-not-allowed disabled:opacity-60"
          >
            <Plus size={14} />
            {$_('orderSeries.newSeries')}
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
            style="grid-template-columns: repeat(auto-fill, 128px);"
            use:dragHandleZone={{ items: dndItems, flipDurationMs: FLIP_MS, dropTargetStyle: {} }}
            onconsider={handleConsider}
            onfinalize={handleFinalize}
          >
            {#each dndItems as item (item.id)}
              {@const isSelected = item.id === selectedItemId}
              <div
                animate:flip={{ duration: FLIP_MS }}
                class="w-[128px] h-[148px] flex flex-col items-center rounded-xl border bg-background/75 p-2 transition-shadow hover:shadow-md cursor-pointer {isSelected ? 'border-transparent ring-1 ring-[hsl(var(--gradient-from))]' : 'border-border'}"
                role="listitem"
                onpointerdown={() => (selectedItemId = item.id)}
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
                      alt={item.fields.name}
                      class="w-full h-full object-contain invert dark:invert-0"
                      draggable="false"
                    />
                  {:else}
                    <Image size={28} class="text-muted-foreground/40" />
                  {/if}
                </div>

                <div class="mt-1.5 w-full text-center">
                  <span class="block truncate text-[12px] font-medium" title={item.fields.name}>{item.fields.name}</span>
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

    <aside class="flex h-full min-h-0 w-[340px] shrink-0 flex-col border border-l-0 border-border bg-card overflow-hidden">
      <div class="gradient-strip h-[3px] shrink-0"></div>
      <div class="shrink-0 border-b border-border px-4 py-3 flex items-center gap-3">
        <div
          class="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl border border-border"
          style="background: linear-gradient(135deg, hsl(var(--gradient-from) / .13), hsl(var(--gradient-to) / .16)); color: hsl(var(--gradient-from));"
        >
          <Settings2 size={18} />
        </div>
        <div class="min-w-0">
          <h2 class="truncate text-sm font-semibold">{$_('orderSeries.settingsHeading')}</h2>
          <p class="truncate text-[12.5px] text-muted-foreground">{selectedItem?.fields.name ?? $_('orderSeries.settingsHint')}</p>
        </div>
      </div>

      {#if selectedItem}
        <div class="shrink-0 flex items-center gap-1 border-b border-border px-2 py-1.5">
          <button
            onclick={() => (panelTab = 'settings')}
            class="inline-flex items-center gap-1.5 rounded-md px-3 py-1.5 text-[12.5px] font-medium transition-colors {panelTab === 'settings' ? 'bg-muted text-foreground' : 'text-muted-foreground hover:bg-muted/60'}"
          >
            <Settings2 size={13} />
            {$_('orderSeries.tabSettings')}
          </button>
          <button
            onclick={() => (panelTab = 'games')}
            class="inline-flex items-center gap-1.5 rounded-md px-3 py-1.5 text-[12.5px] font-medium transition-colors {panelTab === 'games' ? 'bg-muted text-foreground' : 'text-muted-foreground hover:bg-muted/60'}"
          >
            <Gamepad2 size={13} />
            {$_('orderSeries.tabGames')}
          </button>
        </div>
      {/if}

      <div class="min-h-0 flex-1 overflow-auto p-4">
        {#if !selectedItem}
          <div class="grid h-full min-h-[200px] place-items-center text-center">
            <div class="flex flex-col items-center gap-3 px-4">
              <div
                class="flex h-12 w-12 items-center justify-center rounded-2xl border border-border"
                style="background: linear-gradient(135deg, hsl(var(--gradient-from) / .13), hsl(var(--gradient-to) / .16)); color: hsl(var(--gradient-from));"
              >
                <Settings2 size={20} />
              </div>
              <p class="text-[13px] text-muted-foreground">{$_('orderSeries.settingsHint')}</p>
            </div>
          </div>
        {:else if panelTab === 'settings'}
          {@const f = selectedItem.fields}
          <div class="flex flex-col gap-4">
            <div class="flex items-center gap-3">
              <div class="flex h-[64px] w-[64px] shrink-0 items-center justify-center overflow-hidden rounded-lg border border-border bg-muted/30">
                {#if selectedItem.iconDataUrl}
                  <img src={selectedItem.iconDataUrl} alt={f.name} class="h-full w-full object-contain invert dark:invert-0" draggable="false" />
                {:else}
                  <Image size={26} class="text-muted-foreground/40" />
                {/if}
              </div>
              <div class="flex flex-col gap-1">
                <span class="text-[12px] font-medium text-muted-foreground">{$_('orderSeries.fldIcon')}</span>
                <label class="inline-flex cursor-pointer items-center gap-2 self-start rounded-lg border border-input bg-background px-3 py-1.5 text-[12.5px] font-medium transition-colors hover:bg-muted {panelIconBusy ? 'pointer-events-none opacity-60' : ''}">
                  <Upload size={13} />
                  {$_('orderSeries.changeIcon')}
                  <input type="file" accept="image/png" class="hidden" onchange={panelPickIcon} />
                </label>
                {#if panelIconError}
                  <span class="text-[11px] text-red-500">{panelIconError}</span>
                {:else}
                  <span class="text-[11px] text-muted-foreground">{$_('orderSeries.iconHint')}</span>
                {/if}
              </div>
            </div>

            <label class="flex flex-col gap-1">
              <span class="text-[12px] font-medium text-muted-foreground">{$_('orderSeries.fldId')}</span>
              <input type="text" class="{inputClass} opacity-60" value={selectedItem.seriesId} readonly title={$_('orderSeries.fldIdHint')} />
            </label>

            <label class="flex flex-col gap-1">
              <span class="text-[12px] font-medium text-muted-foreground">{$_('orderSeries.fldName')}</span>
              <input type="text" class={inputClass} bind:value={f.name} />
            </label>

            <label class="flex flex-col gap-1">
              <span class="text-[12px] font-medium text-muted-foreground" title={$_('orderSeries.fldSeriesPlaylistHint')}>{$_('orderSeries.fldSeriesPlaylist')}</span>
              <input type="text" class={inputClass} bind:value={f.seriesPlaylist} placeholder="bgm…" />
            </label>

            <label class="flex flex-col gap-1">
              <span class="text-[12px] font-medium text-muted-foreground">{$_('orderSeries.fldPlaylistIncidence')}</span>
              <input type="number" min="0" class={inputClass} bind:value={f.playlistIncidence} />
            </label>

            <div class="mt-1 border-t border-border pt-3">
              <h3 class="text-[12.5px] font-semibold">{$_('orderSeries.defaultsHeading')}</h3>
              <p class="pt-0.5 text-[11.5px] text-muted-foreground">{$_('orderSeries.defaultsHint')}</p>
            </div>

            <label class="flex flex-col gap-1">
              <span class="text-[12px] font-medium text-muted-foreground">{$_('orderSeries.fldDefaultGame')}</span>
              <select class={inputClass} bind:value={f.defaultGame}>
                <option value="">{$_('orderSeries.defaultGameNone')}</option>
                {#each f.games as game}
                  <option value={game.id}>{game.name}</option>
                {/each}
                {#if f.defaultGame && !f.games.some((g) => g.id === f.defaultGame)}
                  <option value={f.defaultGame}>{f.defaultGame}</option>
                {/if}
              </select>
            </label>

            <label class="flex flex-col gap-1">
              <span class="text-[12px] font-medium text-muted-foreground">{$_('orderSeries.fldDefaultAuthor')}</span>
              <input type="text" class={inputClass} bind:value={f.defaultAuthor} />
            </label>

            <label class="flex flex-col gap-1">
              <span class="text-[12px] font-medium text-muted-foreground">{$_('orderSeries.fldDefaultCopyright')}</span>
              <input type="text" class={inputClass} bind:value={f.defaultCopyright} />
            </label>

            <label class="flex flex-col gap-1">
              <span class="text-[12px] font-medium text-muted-foreground">{$_('orderSeries.fldDefaultRecord')}</span>
              <select class={inputClass} bind:value={f.defaultRecordType}>
                {#each RECORD_OPTIONS as option}
                  <option value={option}>{recordLabel(option)}</option>
                {/each}
              </select>
            </label>

            <label class="flex flex-col gap-1">
              <span class="text-[12px] font-medium text-muted-foreground">{$_('orderSeries.fldDefaultVolume')}</span>
              <input type="number" min="0" step="0.1" class={inputClass} bind:value={f.defaultVolume} />
            </label>
          </div>
        {:else}
          {@const f = selectedItem.fields}
          <div class="flex flex-col gap-3">
            <button
              onclick={openAddGame}
              class="inline-flex items-center justify-center gap-2 rounded-lg border border-input bg-background px-3 py-2 text-[12.5px] font-medium transition-colors hover:bg-muted"
            >
              <Plus size={14} />
              {$_('orderSeries.addGame')}
            </button>

            {#if f.games.length === 0}
              <p class="px-1 text-[13px] text-muted-foreground">{$_('orderSeries.noGames')}</p>
            {:else}
              {#each f.games as game (game.id)}
                <div class="flex flex-col gap-1 rounded-md border border-border bg-background/60 p-2">
                  <span class="truncate text-[11px] font-medium text-muted-foreground" title={game.id}>{game.id}</span>
                  <input type="text" class={inputClass} bind:value={game.name} aria-label={$_('orderSeries.fldGameName')} />
                </div>
              {/each}
            {/if}
          </div>
        {/if}
      </div>
    </aside>
  </div>
</div>

{#if showAddGame}
  <div class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-6">
    <div class="w-full max-w-[400px] rounded-2xl border border-border bg-card shadow-xl overflow-hidden">
      <div class="gradient-strip h-[3px]"></div>
      <div class="flex flex-col gap-3 px-5 py-4">
        <h3 class="text-sm font-semibold">{$_('orderSeries.addGameTitle')}</h3>
        <label class="flex flex-col gap-1">
          <span class="text-[12px] font-medium text-muted-foreground">{$_('orderSeries.fldGameId')}</span>
          <input type="text" class={inputClass} bind:value={newGameId} placeholder="mario_kart_8" />
        </label>
        <label class="flex flex-col gap-1">
          <span class="text-[12px] font-medium text-muted-foreground">{$_('orderSeries.fldGameName')}</span>
          <input type="text" class={inputClass} bind:value={newGameName} placeholder="Mario Kart 8" />
        </label>
      </div>
      <div class="flex justify-end gap-2 border-t border-border px-5 py-4">
        <button
          onclick={() => (showAddGame = false)}
          class="inline-flex items-center justify-center rounded-lg px-3 py-2 text-[12.5px] font-medium text-muted-foreground transition-colors hover:bg-muted"
        >
          {$_('orderSeries.cancel')}
        </button>
        <button
          onclick={confirmAddGame}
          disabled={!newGameValid}
          class="inline-flex items-center justify-center rounded-lg border border-input bg-background px-3 py-2 text-[12.5px] font-medium transition-colors hover:bg-muted disabled:cursor-not-allowed disabled:opacity-60"
        >
          {$_('orderSeries.add')}
        </button>
      </div>
    </div>
  </div>
{/if}

{#if showNewSeries}
  <div class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-6">
    <div class="flex max-h-[88vh] w-full max-w-[460px] flex-col overflow-hidden rounded-2xl border border-border bg-card shadow-xl">
      <div class="gradient-strip h-[3px] shrink-0"></div>
      <div class="min-h-0 flex-1 overflow-auto px-5 py-4">
        <h3 class="text-sm font-semibold">{$_('orderSeries.newSeriesTitle')}</h3>

        <div class="mt-3 flex flex-col gap-3">
          <label class="flex flex-col gap-1">
            <span class="text-[12px] font-medium text-muted-foreground">{$_('orderSeries.newSeriesIdLabel')}</span>
            <input type="text" class={inputClass} bind:value={nsId} placeholder="my_series" />
            {#if nsTrimmedId && !nsIdValid}
              <span class="text-[11.5px] text-red-500">{$_('orderSeries.newSeriesIdInvalid')}</span>
            {:else if nsIdTaken}
              <span class="text-[11.5px] text-red-500">{$_('orderSeries.newSeriesIdTaken')}</span>
            {:else}
              <span class="text-[11.5px] text-muted-foreground">{$_('orderSeries.newSeriesIdHint')}</span>
            {/if}
          </label>

          <label class="flex flex-col gap-1">
            <span class="text-[12px] font-medium text-muted-foreground">{$_('orderSeries.fldName')}</span>
            <input type="text" class={inputClass} bind:value={nsName} placeholder="My Series" />
          </label>

          <label class="flex flex-col gap-1">
            <span class="text-[12px] font-medium text-muted-foreground" title={$_('orderSeries.fldDefaultPlaylistHint')}>{$_('orderSeries.fldDefaultPlaylist')}</span>
            <input
              type="text"
              class={inputClass}
              bind:value={nsPlaylist}
              oninput={() => (nsPlaylistDirty = true)}
              placeholder="bgm_my_series"
            />
          </label>

          <div class="flex items-center gap-3">
            <div class="flex h-[56px] w-[56px] shrink-0 items-center justify-center overflow-hidden rounded-lg border border-border bg-muted/30">
              {#if nsIconDataUrl}
                <img src={nsIconDataUrl} alt="" class="h-full w-full object-contain invert dark:invert-0" />
              {:else}
                <Image size={22} class="text-muted-foreground/40" />
              {/if}
            </div>
            <div class="flex flex-col gap-1">
              <span class="text-[12px] font-medium text-muted-foreground">{$_('orderSeries.fldIcon')}</span>
              <label class="inline-flex cursor-pointer items-center gap-2 self-start rounded-lg border border-input bg-background px-3 py-1.5 text-[12.5px] font-medium transition-colors hover:bg-muted">
                <Upload size={13} />
                {$_('orderSeries.uploadIcon')}
                <input type="file" accept="image/png" class="hidden" onchange={nsPickIcon} />
              </label>
              <span class="text-[11px] text-muted-foreground">{$_('orderSeries.iconHint')}</span>
            </div>
          </div>

          <div class="mt-1 border-t border-border pt-3">
            <h4 class="text-[12.5px] font-semibold">{$_('orderSeries.tabGames')}</h4>
            <p class="pt-0.5 text-[11.5px] text-muted-foreground">{$_('orderSeries.newSeriesGamesHint')}</p>
          </div>

          {#if nsGames.length === 0}
            <p class="px-1 text-[13px] text-muted-foreground">{$_('orderSeries.newSeriesNoGames')}</p>
          {:else}
            <div class="flex flex-col gap-2">
              {#each nsGames as game (game.id)}
                <div class="flex items-center gap-2 rounded-md border border-border bg-background/60 p-2">
                  <div class="min-w-0 flex-1">
                    <span class="block truncate text-[11px] font-medium text-muted-foreground" title={game.id}>{game.id}</span>
                    <span class="block truncate text-[13px]">{game.name}</span>
                  </div>
                  <button
                    onclick={() => nsRemoveGame(game.id)}
                    aria-label={$_('orderSeries.removeGame')}
                    title={$_('orderSeries.removeGame')}
                    class="inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-muted hover:text-foreground"
                  >
                    <X size={14} />
                  </button>
                </div>
              {/each}
            </div>
          {/if}

          <div class="flex items-end gap-2 rounded-md border border-dashed border-border p-2">
            <label class="flex min-w-0 flex-1 flex-col gap-1">
              <span class="text-[11px] font-medium text-muted-foreground">{$_('orderSeries.fldGameId')}</span>
              <input type="text" class={inputClass} bind:value={nsGameId} placeholder="my_game" />
            </label>
            <label class="flex min-w-0 flex-1 flex-col gap-1">
              <span class="text-[11px] font-medium text-muted-foreground">{$_('orderSeries.fldGameName')}</span>
              <input type="text" class={inputClass} bind:value={nsGameName} placeholder="My Game" />
            </label>
            <button
              onclick={nsAddGame}
              disabled={!nsGameValid}
              aria-label={$_('orderSeries.addGame')}
              title={$_('orderSeries.addGame')}
              class="inline-flex h-[34px] w-[34px] shrink-0 items-center justify-center rounded-lg border border-input bg-background transition-colors hover:bg-muted disabled:cursor-not-allowed disabled:opacity-60"
            >
              <Plus size={14} />
            </button>
          </div>

          {#if nsError}
            <p class="text-[12px] text-red-500">{nsError}</p>
          {/if}
        </div>
      </div>

      <div class="flex shrink-0 justify-end gap-2 border-t border-border px-5 py-4">
        <button
          onclick={() => (showNewSeries = false)}
          class="inline-flex items-center justify-center rounded-lg px-3 py-2 text-[12.5px] font-medium text-muted-foreground transition-colors hover:bg-muted"
        >
          {$_('orderSeries.cancel')}
        </button>
        <button
          onclick={confirmNewSeries}
          disabled={!nsCanCreate}
          class="inline-flex items-center justify-center rounded-lg border border-input bg-background px-3 py-2 text-[12.5px] font-medium transition-colors hover:bg-muted disabled:cursor-not-allowed disabled:opacity-60"
        >
          {nsCreating ? $_('orderSeries.creating') : $_('orderSeries.create')}
        </button>
      </div>
    </div>
  </div>
{/if}
