<script lang="ts">
  import ArrowUpDown from '@lucide/svelte/icons/arrow-up-down'
  import ListOrdered from '@lucide/svelte/icons/list-ordered'
  import GripVertical from '@lucide/svelte/icons/grip-vertical'
  import Gamepad2 from '@lucide/svelte/icons/gamepad-2'
  import Image from '@lucide/svelte/icons/image'
  import Plus from '@lucide/svelte/icons/plus'
  import RefreshCw from '@lucide/svelte/icons/refresh-cw'
  import Settings2 from '@lucide/svelte/icons/settings-2'
  import Upload from '@lucide/svelte/icons/upload'
  import Volume2 from '@lucide/svelte/icons/volume-2'
  import X from '@lucide/svelte/icons/x'
  import { _ } from 'svelte-i18n'
  import { flip } from 'svelte/animate'
  import { dragHandleZone, dragHandle, type DndEvent } from 'svelte-dnd-action'
  import ActionHeader from '$lib/components/ui/action-header.svelte'
  import { trackUnsaved } from '$lib/stores/unsaved.svelte'
  import EmptyState from '$lib/components/ui/empty-state.svelte'
  import IconButton from '$lib/components/ui/icon-button.svelte'
  import SaveButton from '$lib/components/ui/save-button.svelte'
  import Modal from '$lib/components/ui/modal.svelte'
  import { modsStore } from '$lib/stores/mods.svelte'
  import { formatDb } from '$lib/volume'
  import type { ModInfo, SeriesOrderData, SeriesOrderItem } from '$lib/types/electron'

  const FLIP_MS = 180

  let {
    activeMod,
    onNavigate
  }: { activeMod: ModInfo | null; onNavigate?: (actionId: string) => void } = $props()

  // A vanilla series gets its folder in the mod the first time another view needs it.
  async function seriesPathOf(item: SeriesOrderItem): Promise<string> {
    return item.seriesPath || (await window.electron.umb.ensureVanillaSeries(activeMod!.path, item.seriesId))
  }

  async function openIn(item: SeriesOrderItem, actionId: 'config-volume' | 'order-tracks') {
    try {
      const path = await seriesPathOf(item)
      if (actionId === 'config-volume') modsStore.pendingConfigVolumePath = path
      else modsStore.activeSeriesPath = path
      onNavigate?.(actionId)
    } catch (error) {
      panelError = error instanceof Error ? error.message : String(error)
    }
  }

  let panelTab = $state<'settings' | 'games'>('settings')
  let showAddGame = $state(false)
  let newGameId = $state('')
  let newGameName = $state('')

  let loading = $state(false)
  let orderData = $state<SeriesOrderData | null>(null)
  let dndItems = $state<SeriesOrderItem[]>([])
  let vanillaItems = $state<SeriesOrderItem[]>([])
  let saveState = $state<'idle' | 'saving' | 'saved'>('idle')
  let baselineSnapshot = $state('')
  let selectedItemId = $state<string | null>(null)

  function snapshot(items: SeriesOrderItem[]): string {
    return JSON.stringify(items.map((item) => ({ id: item.id, fields: item.fields })))
  }

  const isDirty = $derived(Boolean(orderData) && snapshot([...dndItems, ...vanillaItems]) !== baselineSnapshot)
  const selectedItem = $derived([...dndItems, ...vanillaItems].find((item) => item.id === selectedItemId) ?? null)

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
    lastGameErrorId = null
    showAddGame = false
  }

  // A custom series keeps at least one game; removing the last one shows an error on it instead.
  let lastGameErrorId = $state<string | null>(null)

  function removeGame(item: SeriesOrderItem, gameId: string) {
    if (!item.isVanilla && item.fields.games.length === 1) {
      lastGameErrorId = gameId
      return
    }
    item.fields.games = item.fields.games.filter((g) => g.id !== gameId)
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
  let panelError = $state<string | null>(null)

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
    panelError = null
    let dataUrl: string
    try {
      dataUrl = await readPngDataUrl(file)
    } catch {
      panelError = $_('orderSeries.iconNotPng')
      return
    }
    panelIconBusy = true
    try {
      selectedItem.iconDataUrl = await window.electron.umb.setSeriesIcon(activeMod.path, selectedItem.seriesId, dataUrl)
    } catch (error) {
      panelError = error instanceof Error ? error.message : String(error)
    } finally {
      panelIconBusy = false
    }
  }

  const nsTrimmedId = $derived(nsId.trim())
  const nsIdValid = $derived(SERIES_ID_RE.test(nsTrimmedId) && nsTrimmedId !== 'etc')
  const nsIdTaken = $derived([...dndItems, ...vanillaItems].some((item) => item.seriesId === nsTrimmedId))
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
      baselineSnapshot = snapshot([...result.items, ...result.vanillaItems])
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
    vanillaItems = orderData ? orderData.vanillaItems : []
  })

  $effect(() => {
    if (isDirty && saveState === 'saved') saveState = 'idle'
  })

  $effect(() => {
    void selectedItemId
    panelError = null
    lastGameErrorId = null
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
      baselineSnapshot = snapshot([...orderData.items, ...orderData.vanillaItems])
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
    const payload = [...dndItems, ...vanillaItems].map((item) => ({
      id: item.id,
      fields: { ...item.fields, games: item.fields.games.map((game) => ({ ...game })) }
    }))
    try {
      orderData = await window.electron.umb.saveSeriesOrder(activeMod.path, payload)
      baselineSnapshot = snapshot([...orderData.items, ...orderData.vanillaItems])
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
    'field w-full'

  // Read-only values are plain text so they can't be clicked into like an input.
  const readonlyClass = 'block truncate px-2 py-1 text-[12.5px] text-muted-foreground'

  const cardClass = (item: SeriesOrderItem): string =>
    `w-[128px] h-[148px] flex flex-col items-center rounded-xl border p-2 transition-colors hover:border-dim cursor-pointer ${item.id === selectedItemId ? 'border-primary bg-accbg/40' : 'border-line2'}`

  trackUnsaved(() => isDirty, handleSave)
</script>

{#snippet seriesCard(item: SeriesOrderItem)}
  <div class="w-[80px] h-[80px] rounded-lg border border-line2 flex items-center justify-center overflow-hidden shrink-0">
    {#if item.iconDataUrl}
      <img src={item.iconDataUrl} alt={item.fields.name} class="w-full h-full object-contain invert dark:invert-0" draggable="false" />
    {:else}
      <Image size={28} class="text-dim" />
    {/if}
  </div>

  <div class="mt-1.5 w-full text-center">
    <span class="block truncate text-[12px] font-medium" title={item.fields.name}>{item.fields.name}</span>
  </div>
{/snippet}

<ActionHeader
  title={$_('orderSeries.title')}
  subtitle={orderData ? $_('orderSeries.dragHint') : activeMod ? $_('orderSeries.noCustomSeries') : $_('orderSeries.chooseMod')}
>
  <IconButton onclick={handleReload} title={$_('orderSeries.reload')}>
    <RefreshCw size={14} class={loading ? 'animate-spin' : ''} />
  </IconButton>

  <button
    onclick={openNewSeries}
    disabled={!activeMod}
    class="btn shrink-0"
  >
    <Plus size={14} />
    {$_('orderSeries.newSeries')}
  </button>

  <SaveButton
    {saveState}
    onclick={handleSave}
    disabled={!orderData || !isDirty || saveState === 'saving'}
    save={$_('orderSeries.save')}
    saving={$_('orderSeries.saving')}
    saved={$_('orderSeries.saved')}
  />
</ActionHeader>

<div class="surface-grain min-h-0 flex-1 overflow-hidden bg-card">
  <div class="flex h-full min-h-0">
    <!-- Five series cards wide (5 x 128px + gaps, padding, scrollbar); settings fill the rest. -->
    <section class="flex h-full min-h-0 w-[730px] shrink-0 flex-col overflow-hidden">
      <div class="min-h-0 flex-1 overflow-auto p-4">
        {#if loading}
          <div class="grid h-full min-h-[320px] place-items-center rounded-xl border border-dashed border-border text-[13px] text-muted-foreground">
            {$_('orderSeries.loading')}
          </div>
        {:else if orderData && (orderData.items.length > 0 || orderData.vanillaItems.length > 0)}
          {#if dndItems.length === 0}
            <p class="pb-3 text-[12.5px] text-muted-foreground">{$_('orderSeries.noCustomSeries')}</p>
          {/if}
          <div
            class="grid gap-3"
            style="grid-template-columns: repeat(auto-fill, 128px);"
            use:dragHandleZone={{ items: dndItems, flipDurationMs: FLIP_MS, dropTargetStyle: {} }}
            onconsider={handleConsider}
            onfinalize={handleFinalize}
          >
            {#each dndItems as item (item.id)}
              <div animate:flip={{ duration: FLIP_MS }} class={cardClass(item)} role="listitem" onpointerdown={() => (selectedItemId = item.id)}>
                <div
                  use:dragHandle
                  aria-label={$_('orderSeries.dragAria')}
                  class="w-full flex justify-center cursor-grab active:cursor-grabbing mb-1"
                >
                  <GripVertical size={14} class="text-dim" />
                </div>
                {@render seriesCard(item)}
              </div>
            {/each}
          </div>

          {#if vanillaItems.length > 0}
            <div class="mt-6 border-t border-line2 pt-3 pb-3">
              <h3 class="text-[12.5px] font-semibold">{$_('orderSeries.vanillaHeading')}</h3>
              <p class="pt-0.5 text-[11.5px] text-muted-foreground">{$_('orderSeries.vanillaHint')}</p>
            </div>
            <div class="grid gap-3" style="grid-template-columns: repeat(auto-fill, 128px);" aria-label={$_('orderSeries.vanillaHeading')}>
              {#each vanillaItems as item (item.id)}
                <div class={cardClass(item)} role="listitem" onpointerdown={() => (selectedItemId = item.id)}>
                  <span class="mb-1 text-[10.5px] font-semibold uppercase tracking-[.1em] text-dim">{$_('orderSeries.vanillaTag')}</span>
                  {@render seriesCard(item)}
                </div>
              {/each}
            </div>
          {/if}
        {:else if orderData}
          <EmptyState dashed title={$_('orderSeries.emptyTitle')} body={$_('orderSeries.emptyBody')}>
            {#snippet icon()}<ListOrdered size={20} />{/snippet}
          </EmptyState>
        {:else}
          <EmptyState dashed title={$_('orderSeries.chooseMod')} body={$_('common.chooseMod')}>
            {#snippet icon()}<ListOrdered size={20} />{/snippet}
          </EmptyState>
        {/if}
      </div>
    </section>

    <aside class="flex h-full min-h-0 min-w-[340px] flex-1 flex-col overflow-hidden border-l border-border">
      <div class="shrink-0 border-b border-line2 px-4 py-3">
        <h2 class="truncate text-[13.5px] font-semibold tracking-[-.01em]">{$_('orderSeries.settingsHeading')}</h2>
        <p class="mt-[3px] truncate text-[12.5px] text-muted-foreground">{selectedItem?.fields.name ?? $_('orderSeries.settingsHint')}</p>
      </div>

      {#if selectedItem}
        <div class="shrink-0 flex items-center gap-1 border-b border-line2 px-2 py-1.5">
          <button
            onclick={() => (panelTab = 'settings')}
            class="inline-flex items-center gap-1.5 rounded-md px-3 py-1.5 text-[12.5px] font-medium transition-colors {panelTab === 'settings' ? 'bg-accbg text-accink' : 'text-muted-foreground hover:bg-accent hover:text-foreground'}"
          >
            <Settings2 size={13} />
            {$_('orderSeries.tabSettings')}
          </button>
          <button
            onclick={() => (panelTab = 'games')}
            class="inline-flex items-center gap-1.5 rounded-md px-3 py-1.5 text-[12.5px] font-medium transition-colors {panelTab === 'games' ? 'bg-accbg text-accink' : 'text-muted-foreground hover:bg-accent hover:text-foreground'}"
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
              <Settings2 size={20} class="text-dim" />
              <p class="text-[13px] text-muted-foreground">{$_('orderSeries.settingsHint')}</p>
            </div>
          </div>
        {:else if panelTab === 'settings'}
          {@const f = selectedItem.fields}
          <div class="flex flex-col gap-4">
            <div class="flex items-center gap-3">
              <div class="flex h-[64px] w-[64px] shrink-0 items-center justify-center overflow-hidden rounded-lg border border-line2">
                {#if selectedItem.iconDataUrl}
                  <img src={selectedItem.iconDataUrl} alt={f.name} class="h-full w-full object-contain invert dark:invert-0" draggable="false" />
                {:else}
                  <Image size={26} class="text-dim" />
                {/if}
              </div>
              <div class="flex flex-col gap-1">
                <span class="text-[12px] font-medium text-muted-foreground">{$_('orderSeries.fldIcon')}</span>
                <label class="btn cursor-pointer self-start {panelIconBusy ? 'pointer-events-none opacity-45' : ''}">
                  <Upload size={13} />
                  {$_('orderSeries.changeIcon')}
                  <input type="file" accept="image/png" class="hidden" onchange={panelPickIcon} />
                </label>
                {#if panelError}
                  <span class="text-[11px] text-destructive">{panelError}</span>
                {:else}
                  <span class="text-[11px] text-muted-foreground">{$_('orderSeries.iconHint')}</span>
                {/if}
              </div>
            </div>

            <div class="flex flex-col gap-1">
              <span class="text-[12px] font-medium text-muted-foreground">{$_('orderSeries.fldId')}</span>
              <span class={readonlyClass} title={$_('orderSeries.fldIdHint')}>{selectedItem.seriesId}</span>
            </div>

            <label class="flex flex-col gap-1">
              <span class="text-[12px] font-medium text-muted-foreground">{$_('orderSeries.fldName')}</span>
              {#if selectedItem.isVanilla}
                <span class={readonlyClass} title={$_('orderSeries.vanillaNameHint')}>{f.name}</span>
              {:else}
                <input type="text" class={inputClass} bind:value={f.name} />
              {/if}
            </label>

            <label class="flex flex-col gap-1">
              <span class="text-[12px] font-medium text-muted-foreground" title={$_('orderSeries.fldSeriesPlaylistHint')}>{$_('orderSeries.fldSeriesPlaylist')}</span>
              <input type="text" class={inputClass} bind:value={f.seriesPlaylist} placeholder="bgm…" />
              {#if selectedItem.isVanilla && f.seriesPlaylist !== selectedItem.vanillaPlaylist}
                <span class="text-[11.5px] text-warn">{$_('orderSeries.customPlaylistWarning')}</span>
              {/if}
            </label>

            <label class="flex flex-col gap-1">
              <span class="text-[12px] font-medium text-muted-foreground">{$_('orderSeries.fldPlaylistIncidence')}</span>
              <input type="number" min="0" class={inputClass} bind:value={f.playlistIncidence} />
            </label>

            <div class="flex flex-col gap-1">
              <span class="text-[12px] font-medium text-muted-foreground">{$_('orderSeries.fldVolume')}</span>
              <button
                onclick={() => openIn(selectedItem, 'config-volume')}
                title={$_('orderSeries.volumeHint')}
                class="{inputClass} flex cursor-pointer items-center justify-between text-left text-muted-foreground opacity-60 transition-opacity hover:opacity-100"
              >
                <span class="tabular-nums">{formatDb(selectedItem.volumeDb)}</span>
                <span class="inline-flex items-center gap-1 text-[11.5px]"><Volume2 size={12} />{$_('orderSeries.volumeEdit')}</span>
              </button>
            </div>

            <button
              onclick={() => openIn(selectedItem, 'order-tracks')}
              class="btn"
            >
              <ArrowUpDown size={14} />
              {$_('orderSeries.editSongs')}
            </button>
          </div>
        {:else}
          {@const f = selectedItem.fields}
          <div class="flex flex-col gap-3">
            <button
              onclick={openAddGame}
              class="btn"
            >
              <Plus size={14} />
              {$_('orderSeries.addGame')}
            </button>

            {#if f.games.length === 0}
              <p class="px-1 text-[13px] text-muted-foreground">{$_('orderSeries.noGames')}</p>
            {:else}
              {#each f.games as game (game.id)}
                <div class="flex flex-col gap-1 rounded-xl border border-line2 p-2">
                  <div class="flex items-center gap-2">
                    <span class="min-w-0 flex-1 truncate text-[11px] font-medium text-muted-foreground" title={game.id}>{game.id}</span>
                    {#if !selectedItem.lockedGameIds.includes(game.id)}
                      <button
                        onclick={() => removeGame(selectedItem, game.id)}
                        aria-label={$_('orderSeries.removeGame')}
                        title={$_('orderSeries.removeGame')}
                        class="inline-flex h-6 w-6 shrink-0 items-center justify-center rounded text-dim transition-colors duration-100 hover:bg-accent hover:text-destructive"
                      >
                        <X size={13} />
                      </button>
                    {/if}
                  </div>
                  {#if selectedItem.lockedGameIds.includes(game.id)}
                    <span class={readonlyClass} title={$_('orderSeries.vanillaGameHint')}>{game.name}</span>
                  {:else}
                    <input type="text" class={inputClass} bind:value={game.name} aria-label={$_('orderSeries.fldGameName')} />
                  {/if}
                </div>
                {#if lastGameErrorId === game.id}
                  <span class="-mt-2 px-1 text-[11.5px] text-destructive">{$_('orderSeries.lastGameError')}</span>
                {/if}
              {/each}
            {/if}
          </div>
        {/if}
      </div>
    </aside>
  </div>
</div>

{#if showAddGame}
  <Modal>
      <div class="flex flex-col gap-3 px-5 py-4">
        <h3 class="text-[13.5px] font-semibold">{$_('orderSeries.addGameTitle')}</h3>
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
          class="btn border-transparent text-muted-foreground"
        >
          {$_('orderSeries.cancel')}
        </button>
        <button
          onclick={confirmAddGame}
          disabled={!newGameValid}
          class="btn-primary"
        >
          {$_('orderSeries.add')}
        </button>
      </div>
  </Modal>
{/if}

{#if showNewSeries}
  <Modal maxWidth="460px" maxHeight="88vh">
      <div class="min-h-0 flex-1 overflow-auto px-5 py-4">
        <h3 class="text-[13.5px] font-semibold">{$_('orderSeries.newSeriesTitle')}</h3>

        <div class="mt-3 flex flex-col gap-3">
          <label class="flex flex-col gap-1">
            <span class="text-[12px] font-medium text-muted-foreground">{$_('orderSeries.newSeriesIdLabel')}</span>
            <input type="text" class={inputClass} bind:value={nsId} placeholder="my_series" />
            {#if nsTrimmedId && !nsIdValid}
              <span class="text-[11.5px] text-destructive">{$_('orderSeries.newSeriesIdInvalid')}</span>
            {:else if nsIdTaken}
              <span class="text-[11.5px] text-destructive">{$_('orderSeries.newSeriesIdTaken')}</span>
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
            <div class="flex h-[56px] w-[56px] shrink-0 items-center justify-center overflow-hidden rounded-lg border border-line2">
              {#if nsIconDataUrl}
                <img src={nsIconDataUrl} alt="" class="h-full w-full object-contain invert dark:invert-0" />
              {:else}
                <Image size={22} class="text-dim" />
              {/if}
            </div>
            <div class="flex flex-col gap-1">
              <span class="text-[12px] font-medium text-muted-foreground">{$_('orderSeries.fldIcon')}</span>
              <label class="btn cursor-pointer self-start">
                <Upload size={13} />
                {$_('orderSeries.uploadIcon')}
                <input type="file" accept="image/png" class="hidden" onchange={nsPickIcon} />
              </label>
              <span class="text-[11px] text-muted-foreground">{$_('orderSeries.iconHint')}</span>
            </div>
          </div>

          <div class="mt-1 border-t border-line2 pt-3">
            <h4 class="text-[12.5px] font-semibold">{$_('orderSeries.tabGames')}</h4>
            <p class="pt-0.5 text-[11.5px] text-muted-foreground">{$_('orderSeries.newSeriesGamesHint')}</p>
          </div>

          {#if nsGames.length === 0}
            <p class="px-1 text-[13px] text-muted-foreground">{$_('orderSeries.newSeriesNoGames')}</p>
          {:else}
            <div class="flex flex-col gap-2">
              {#each nsGames as game (game.id)}
                <div class="flex items-center gap-2 rounded-xl border border-line2 p-2">
                  <div class="min-w-0 flex-1">
                    <span class="block truncate text-[11px] font-medium text-muted-foreground" title={game.id}>{game.id}</span>
                    <span class="block truncate text-[13px]">{game.name}</span>
                  </div>
                  <button
                    onclick={() => nsRemoveGame(game.id)}
                    aria-label={$_('orderSeries.removeGame')}
                    title={$_('orderSeries.removeGame')}
                    class="inline-flex h-7 w-7 shrink-0 items-center justify-center rounded text-dim transition-colors duration-100 hover:bg-accent hover:text-destructive"
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
              class="btn w-[27px] px-0 shrink-0"
            >
              <Plus size={14} />
            </button>
          </div>

          {#if nsError}
            <p class="text-[12px] text-destructive">{nsError}</p>
          {/if}
        </div>
      </div>

      <div class="flex shrink-0 justify-end gap-2 border-t border-border px-5 py-4">
        <button
          onclick={() => (showNewSeries = false)}
          class="btn border-transparent text-muted-foreground"
        >
          {$_('orderSeries.cancel')}
        </button>
        <button
          onclick={confirmNewSeries}
          disabled={!nsCanCreate}
          class="btn-primary"
        >
          {nsCreating ? $_('orderSeries.creating') : $_('orderSeries.create')}
        </button>
      </div>
  </Modal>
{/if}
