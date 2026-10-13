<script lang="ts">
  import ListPlus from '@lucide/svelte/icons/list-plus'
  import RefreshCw from '@lucide/svelte/icons/refresh-cw'
  import Search from '@lucide/svelte/icons/search'
  import Plus from '@lucide/svelte/icons/plus'
  import X from '@lucide/svelte/icons/x'
  import Eye from '@lucide/svelte/icons/eye'
  import EyeOff from '@lucide/svelte/icons/eye-off'
  import GripVertical from '@lucide/svelte/icons/grip-vertical'
  import Trash2 from '@lucide/svelte/icons/trash-2'
  import RotateCcw from '@lucide/svelte/icons/rotate-ccw'
  import TriangleAlert from '@lucide/svelte/icons/triangle-alert'
  import { flip } from 'svelte/animate'
  import { dragHandleZone, dragHandle, type DndEvent } from 'svelte-dnd-action'
  import { _ } from 'svelte-i18n'
  import SaveButton from '$lib/components/ui/save-button.svelte'
  import GradientIcon from '$lib/components/ui/gradient-icon.svelte'
  import Modal from '$lib/components/ui/modal.svelte'
  import SearchableList from '$lib/components/ui/searchable-list.svelte'
  import { trackUnsaved } from '$lib/stores/unsaved.svelte'
  import { matchesSearch } from '$lib/search'
  import { inputClass, toolbarButtonClass } from '$lib/ui-classes'
  import {
    ORDER_SLOTS,
    addSongs,
    excludedTracks,
    isChanged,
    playlistDefaults,
    playlistIdError,
    playlistIdFromName,
    playlistsToSave,
    removeTrack,
    resetToDefault,
    setIncluded,
    setSlotOrder,
    silentStages,
    slotTracks,
    stagesToSave
  } from '$lib/playlists'
  import type { ModInfo, ManagePlaylistsData, ManagedPlaylist, PlaylistKind, PlaylistTrack } from '$lib/types/electron'

  let { activeMod }: { activeMod: ModInfo | null } = $props()

  const FLIP_MS = 180
  const KIND_LABELS: Record<PlaylistKind, string> = {
    custom: 'managePlaylists.kindCustom',
    series: 'managePlaylists.kindSeries',
    vanilla: 'managePlaylists.kindVanilla'
  }

  let data = $state<ManagePlaylistsData | null>(null)
  let loading = $state(false)
  let saveState = $state<'idle' | 'saving' | 'saved'>('idle')
  let baselineSnapshot = $state('')
  let defaults = $state.raw<Record<string, string>>({})
  let showMigrate = $state(false)
  let tab = $state<'playlists' | 'stages'>('playlists')
  let selectedPlaylistId = $state<string | null>(null)
  let slot = $state(0)
  let search = $state('')
  let stageSearch = $state('')
  let showAddSong = $state(false)
  let addSearch = $state('')
  let creating = $state<{ name: string; id: string } | null>(null)

  function snapshot(d: ManagePlaylistsData | null): string {
    return JSON.stringify([d?.playlists, d?.stages.map((s) => [s.id, s.playlistId, s.order])])
  }

  const isDirty = $derived(Boolean(data) && snapshot(data) !== baselineSnapshot)
  const songsById = $derived(new Map((data?.songs ?? []).map((s) => [s.bgmId, s])))
  const filteredPlaylists = $derived((data?.playlists ?? []).filter((p) => matchesSearch(search, p.name, p.id)))
  const selectedPlaylist = $derived<ManagedPlaylist | null>(data?.playlists.find((p) => p.id === selectedPlaylistId) ?? null)
  const playlistStages = $derived((data?.stages ?? []).filter((s) => s.playlistId === selectedPlaylistId))
  const silent = $derived(data ? silentStages(data.stages, data.playlists) : [])
  const silentIds = $derived(new Set(silent.map((s) => s.id)))
  const filteredStages = $derived((data?.stages ?? []).filter((s) => matchesSearch(stageSearch, s.name, s.id, s.seriesName)))
  const playlistNames = $derived(new Map((data?.playlists ?? []).map((p) => [p.id, p.name])))

  // svelte-dnd-action needs its own list while dragging; it follows the slot otherwise.
  let dndItems = $state<{ id: string; track: PlaylistTrack }[]>([])
  $effect(() => {
    dndItems = selectedPlaylist ? slotTracks(selectedPlaylist, slot).map((track) => ({ id: track.bgmId, track })) : []
  })

  const addablePool = $derived(
    (() => {
      if (!data || !selectedPlaylist) return []
      const taken = new Set(selectedPlaylist.tracks.map((t) => t.bgmId))
      return data.songs.filter((s) => !taken.has(s.bgmId) && matchesSearch(addSearch, s.title, s.seriesName, s.bgmId)).slice(0, 200)
    })()
  )

  const createError = $derived(creating ? playlistIdError(creating.id, (data?.playlists ?? []).map((p) => p.id)) : null)

  $effect(() => {
    void loadData(activeMod?.path ?? null)
  })

  $effect(() => {
    if (isDirty && saveState === 'saved') saveState = 'idle'
  })

  // Keep a valid selection as the filtered list changes.
  $effect(() => {
    if (filteredPlaylists.length > 0 && !filteredPlaylists.some((p) => p.id === selectedPlaylistId)) {
      selectedPlaylistId = filteredPlaylists[0].id
    }
  })

  function setData(next: ManagePlaylistsData) {
    data = next
    baselineSnapshot = snapshot(next)
    defaults = playlistDefaults(next.playlists)
    showMigrate = next.legacyBlocks
  }

  async function loadData(modPath: string | null) {
    if (!modPath) {
      data = null
      return
    }
    loading = true
    saveState = 'idle'
    showAddSong = false
    try {
      setData(await window.electron.umb.loadManagePlaylists(modPath))
    } finally {
      loading = false
    }
  }

  function title(bgmId: string) {
    return songsById.get(bgmId)?.title ?? bgmId
  }

  function selectPlaylist(id: string) {
    selectedPlaylistId = id
    const stage = data?.stages.find((s) => s.playlistId === id)
    slot = stage?.order ?? 0
  }

  function handleConsider(event: CustomEvent<DndEvent<{ id: string; track: PlaylistTrack }>>) {
    dndItems = event.detail.items
  }

  function handleFinalize(event: CustomEvent<DndEvent<{ id: string; track: PlaylistTrack }>>) {
    dndItems = event.detail.items
    if (selectedPlaylist) setSlotOrder(selectedPlaylist, slot, event.detail.items.map((i) => i.track))
  }

  function createPlaylist() {
    if (!data || !creating || createError) return
    data.playlists = [{ id: creating.id, name: creating.name.trim() || creating.id, kind: 'custom', series: [], tracks: [], defaultTracks: null }, ...data.playlists]
    search = ''
    selectPlaylist(creating.id)
    creating = null
  }

  function deletePlaylist(playlist: ManagedPlaylist) {
    if (!data) return
    data.playlists = data.playlists.filter((p) => p.id !== playlist.id)
  }

  async function handleSave() {
    if (!data || !activeMod) return
    saveState = 'saving'
    const plain = $state.snapshot(data)
    for (const track of plain.playlists.flatMap((p) => p.tracks)) {
      track.incidences = track.incidences.map((i) => Math.max(0, Math.min(65535, Math.trunc(Number(i)) || 0)))
    }
    try {
      showMigrate = false
      setData(await window.electron.umb.saveManagePlaylists(activeMod.path, playlistsToSave(plain.playlists, defaults), stagesToSave(plain.stages)))
      saveState = 'saved'
    } catch (error) {
      console.error('saveManagePlaylists failed', error)
      saveState = 'idle'
    }
  }

  trackUnsaved(() => isDirty, handleSave)
</script>

<div class="flex-1 overflow-hidden">
  <div class="flex h-full min-h-0">
    <section class="flex h-full min-h-0 w-full flex-col border border-border bg-card overflow-hidden">
      <div class="gradient-strip h-[3px] shrink-0"></div>

      <div class="shrink-0 border-b border-border px-5 py-3 flex items-center justify-between gap-4">
        <div class="flex min-w-0 items-center gap-3">
          <GradientIcon>
            <ListPlus size={18} />
          </GradientIcon>
          <div class="min-w-0">
            <h2 class="truncate text-sm font-semibold">{$_('managePlaylists.title')}</h2>
            <p class="truncate text-[12.5px] text-muted-foreground">
              {activeMod ? $_('managePlaylists.subtitle') : $_('managePlaylists.chooseModBody')}
            </p>
          </div>
        </div>

        <div class="flex items-center gap-2">
          {#if data}
            <div class="flex rounded-lg border border-input p-0.5" role="tablist">
              {#each [['playlists', 'managePlaylists.tabPlaylists'], ['stages', 'managePlaylists.tabStages']] as [id, label] (id)}
                <button
                  role="tab"
                  aria-selected={tab === id}
                  onclick={() => (tab = id as 'playlists' | 'stages')}
                  class="rounded-md px-3 py-1 text-[12.5px] font-medium transition-colors {tab === id ? 'bg-muted' : 'text-muted-foreground hover:text-foreground'}"
                >
                  {$_(label)}
                </button>
              {/each}
            </div>
          {/if}
          <button
            onclick={() => void loadData(activeMod?.path ?? null)}
            class="inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-lg border border-input bg-background transition-colors hover:bg-muted"
            title={$_('managePlaylists.reload')}
          >
            <RefreshCw size={14} class={loading ? 'animate-spin' : ''} />
          </button>
          <SaveButton
            {saveState}
            onclick={handleSave}
            disabled={!data || data.legacyBlocks || !isDirty || saveState === 'saving'}
            save={$_('managePlaylists.save')}
            saving={$_('managePlaylists.saving')}
            saved={$_('managePlaylists.saved')}
          />
        </div>
      </div>

      {#if data?.legacyBlocks}
        <div class="flex shrink-0 items-center gap-2 border-b border-border bg-amber-500/10 px-5 py-2 text-[12.5px] text-amber-700 dark:text-amber-300">
          <TriangleAlert size={14} class="shrink-0" />
          <span class="flex-1">{$_('managePlaylists.legacyBanner')}</span>
          <button onclick={() => (showMigrate = true)} class={toolbarButtonClass}>{$_('managePlaylists.migrate')}</button>
        </div>
      {/if}

      {#if data && silent.length > 0}
        <div class="flex shrink-0 items-center gap-2 border-b border-border bg-amber-500/10 px-5 py-2 text-[12.5px] text-amber-700 dark:text-amber-300">
          <TriangleAlert size={14} class="shrink-0" />
          {$_('managePlaylists.silentWarning', { values: { count: silent.length } })}
        </div>
      {/if}

      <div class="min-h-0 flex-1 overflow-hidden">
        {#if loading}
          <div class="flex h-full items-center justify-center gap-2 text-[13px] text-muted-foreground">
            <RefreshCw size={15} class="animate-spin" />
            {$_('managePlaylists.loading')}
          </div>
        {:else if !data}
          <div class="grid h-full place-items-center p-8 text-center">
            <div class="flex max-w-[340px] flex-col items-center gap-3">
              <GradientIcon size="lg">
                <ListPlus size={20} />
              </GradientIcon>
              <div>
                <h3 class="text-sm font-semibold">{$_('managePlaylists.chooseMod')}</h3>
                <p class="pt-1 text-[13px] text-muted-foreground">{$_('managePlaylists.chooseModBody')}</p>
              </div>
            </div>
          </div>
        {:else if tab === 'playlists'}
          <div class="flex h-full min-h-0">
            <!-- Left: playlist list -->
            <div class="flex h-full w-[280px] shrink-0 flex-col border-r border-border">
              <div class="shrink-0 px-3 pt-3">
                <button onclick={() => (creating = { name: '', id: 'bgm' })} class="{toolbarButtonClass} w-full justify-center">
                  <Plus size={14} />
                  {$_('managePlaylists.newPlaylist')}
                </button>
              </div>
              <SearchableList
                items={filteredPlaylists.map((p) => ({
                  id: p.id,
                  label: p.name,
                  badge: p.tracks.length,
                  hint: p.kind === 'vanilla' ? '' : $_(KIND_LABELS[p.kind])
                }))}
                activeId={selectedPlaylistId}
                onSelect={selectPlaylist}
                bind:search
                placeholder={$_('managePlaylists.searchPlaylists')}
                countText={$_('managePlaylists.playlistCount', { values: { count: filteredPlaylists.length } })}
                emptyText={$_('managePlaylists.noPlaylistMatch')}
              />
            </div>

            <!-- Right: playlist detail -->
            <div class="min-h-0 flex-1 overflow-auto p-5">
              {#if !selectedPlaylist}
                <div class="flex h-full items-center justify-center text-center text-[13px] text-muted-foreground">
                  {$_('managePlaylists.selectPlaylist')}
                </div>
              {:else}
                {@const playlist = selectedPlaylist}
                <div class="flex flex-col gap-4">
                  <div class="flex items-start justify-between gap-3">
                    <div class="min-w-0 flex-1">
                      {#if playlist.kind === 'custom'}
                        <input bind:value={playlist.name} aria-label={$_('managePlaylists.playlistName')} class="{inputClass} max-w-[360px] text-base font-semibold" />
                      {:else}
                        <h3 class="truncate text-base font-semibold">{playlist.name}</h3>
                      {/if}
                      <p class="pt-0.5 text-[11.5px] text-muted-foreground">
                        {playlist.id} · {$_(KIND_LABELS[playlist.kind])}
                      </p>
                      {#if playlist.series.length > 0}
                        <p class="pt-1 text-[11.5px] text-muted-foreground">
                          {$_('managePlaylists.seriesFlag', { values: { series: playlist.series.map((s) => s.name).join(', ') } })}
                        </p>
                      {:else if playlist.kind === 'series'}
                        <p class="pt-1 text-[11.5px] text-muted-foreground">{$_('managePlaylists.seriesHint')}</p>
                      {/if}
                    </div>
                    <div class="flex shrink-0 items-center gap-2">
                      <button onclick={() => { addSearch = ''; showAddSong = true }} class={toolbarButtonClass}>
                        <Plus size={14} />
                        {$_('managePlaylists.addSong')}
                      </button>
                      {#if isChanged(playlist, defaults)}
                        <button
                          onclick={() => resetToDefault(playlist, defaults)}
                          class={toolbarButtonClass}
                          aria-label={$_('managePlaylists.resetPlaylist')}
                          title={$_('managePlaylists.resetPlaylistHint')}
                        >
                          <RotateCcw size={14} />
                        </button>
                      {/if}
                      {#if playlist.kind === 'custom'}
                        <button
                          onclick={() => deletePlaylist(playlist)}
                          disabled={playlistStages.length > 0}
                          class={toolbarButtonClass}
                          aria-label={$_('managePlaylists.deletePlaylist')}
                          title={playlistStages.length > 0 ? $_('managePlaylists.deleteInUse') : $_('managePlaylists.deletePlaylist')}
                        >
                          <Trash2 size={14} />
                        </button>
                      {/if}
                    </div>
                  </div>

                  <p class="text-[12px] text-muted-foreground">
                    {#if playlistStages.length === 0}
                      {$_('managePlaylists.notUsed')}
                    {:else}
                      {$_('managePlaylists.usedBy')}: {playlistStages.map((s) => `${s.name} (${s.order})`).join(', ')}
                    {/if}
                  </p>

                  <div class="flex flex-col gap-1.5">
                    <span class="text-[12.5px] font-medium text-muted-foreground" title={$_('managePlaylists.orderHint')}>{$_('managePlaylists.order')}</span>
                    <div class="flex flex-wrap gap-1" role="tablist" aria-label={$_('managePlaylists.order')}>
                      {#each ORDER_SLOTS as s (s)}
                        {@const users = playlistStages.filter((stage) => stage.order === s)}
                        <button
                          role="tab"
                          aria-selected={slot === s}
                          onclick={() => (slot = s)}
                          title={users.length > 0 ? $_('managePlaylists.orderUsedBy', { values: { order: s, stages: users.map((u) => u.name).join(', ') } }) : ''}
                          class="relative h-7 w-8 rounded-md border text-[12px] tabular-nums transition-colors {slot === s ? 'border-transparent bg-foreground text-background' : 'border-input bg-background hover:bg-muted'}"
                        >
                          {s}
                          {#if users.length > 0}
                            <span class="absolute -right-0.5 -top-0.5 h-2 w-2 rounded-full bg-[hsl(var(--gradient-from))]"></span>
                          {/if}
                        </button>
                      {/each}
                    </div>
                  </div>

                  <div class="flex items-center justify-between">
                    <span class="text-[12.5px] font-medium text-muted-foreground">{$_('managePlaylists.slotSongs', { values: { order: slot } })}</span>
                    <span class="text-[11.5px] text-muted-foreground">{$_('managePlaylists.songCount', { values: { count: dndItems.length } })}</span>
                  </div>

                  {#if dndItems.length === 0}
                    <p class="rounded-lg border border-dashed border-border bg-background/60 px-3 py-6 text-center text-[12.5px] text-muted-foreground">
                      {$_('managePlaylists.noSongs')}
                    </p>
                  {/if}
                  <div
                    class="flex flex-col gap-0.5"
                    use:dragHandleZone={{ items: dndItems, flipDurationMs: FLIP_MS, dropTargetStyle: {} }}
                    onconsider={handleConsider}
                    onfinalize={handleFinalize}
                  >
                    {#each dndItems as item, i (item.id)}
                      {@const song = songsById.get(item.id)}
                      <div animate:flip={{ duration: FLIP_MS }} class="flex items-center gap-2 rounded border border-border bg-background px-2 py-1" role="listitem">
                        <span class="w-6 text-right text-[11px] tabular-nums text-muted-foreground">{i + 1}</span>
                        <div use:dragHandle aria-label={$_('managePlaylists.dragAria')} class="flex h-6 w-6 shrink-0 cursor-grab items-center justify-center rounded bg-muted text-muted-foreground active:cursor-grabbing">
                          <GripVertical size={14} />
                        </div>
                        <div class="min-w-0 flex-1">
                          <div class="truncate text-[13px]">{title(item.id)}</div>
                          <div class="truncate text-[11px] text-muted-foreground">{song?.seriesName ?? item.id}</div>
                        </div>
                        {#if song?.isMod}
                          <span class="shrink-0 rounded-full bg-muted px-1.5 py-0.5 text-[10px] text-muted-foreground">{$_('managePlaylists.modSong')}</span>
                        {/if}
                        <label class="flex shrink-0 items-center gap-1.5" title={$_('managePlaylists.incidenceHint')}>
                          <span class="text-[11px] text-muted-foreground">{$_('managePlaylists.colIncidence')}</span>
                          <input type="number" min="0" max="65535" bind:value={item.track.incidences[slot]} class="{inputClass} w-[78px] tabular-nums" />
                        </label>
                        <button
                          onclick={() => setIncluded(playlist, slot, item.track, false)}
                          aria-label={$_('managePlaylists.exclude')}
                          title={$_('managePlaylists.exclude')}
                          class="inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-muted hover:text-foreground"
                        >
                          <EyeOff size={14} />
                        </button>
                        <button
                          onclick={() => removeTrack(playlist, item.id)}
                          disabled={item.track.autoAdded}
                          aria-label={$_('managePlaylists.removeSong')}
                          title={$_(item.track.autoAdded ? 'managePlaylists.autoAdded' : 'managePlaylists.removeSong')}
                          class="inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-muted hover:text-foreground disabled:cursor-not-allowed disabled:opacity-40"
                        >
                          <X size={14} />
                        </button>
                      </div>
                    {/each}
                  </div>

                  {#if excludedTracks(playlist, slot).length > 0}
                    <span class="pt-2 text-[12.5px] font-medium text-muted-foreground">{$_('managePlaylists.excludedHeading', { values: { order: slot } })}</span>
                    <div class="flex flex-col gap-0.5">
                      {#each excludedTracks(playlist, slot) as track (track.bgmId)}
                        <div class="flex items-center gap-2 rounded border border-dashed border-border px-2 py-1 opacity-75">
                          <div class="min-w-0 flex-1 truncate text-[13px]">{title(track.bgmId)}</div>
                          <button
                            onclick={() => setIncluded(playlist, slot, track, true)}
                            aria-label={$_('managePlaylists.include')}
                            title={$_('managePlaylists.include')}
                            class="inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-muted hover:text-foreground"
                          >
                            <Eye size={14} />
                          </button>
                          <button
                            onclick={() => removeTrack(playlist, track.bgmId)}
                            disabled={track.autoAdded}
                            aria-label={$_('managePlaylists.removeSong')}
                            title={$_(track.autoAdded ? 'managePlaylists.autoAdded' : 'managePlaylists.removeSong')}
                            class="inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-muted hover:text-foreground disabled:cursor-not-allowed disabled:opacity-40"
                          >
                            <X size={14} />
                          </button>
                        </div>
                      {/each}
                    </div>
                  {/if}
                </div>
              {/if}
            </div>
          </div>
        {:else}
          <!-- Stages: the playlist and order each stage plays -->
          <div class="flex h-full min-h-0 flex-col">
            <div class="flex shrink-0 items-center gap-3 border-b border-border px-5 py-3">
              <div class="flex w-[320px] items-center gap-2 rounded-lg border border-input bg-background px-2.5 py-1.5">
                <Search size={14} class="shrink-0 text-muted-foreground" />
                <input type="text" bind:value={stageSearch} placeholder={$_('managePlaylists.searchStages')} class="w-full bg-transparent text-[13px] focus:outline-none" />
              </div>
              <span class="text-[11.5px] text-muted-foreground">{$_('managePlaylists.stageCount', { values: { count: filteredStages.length } })}</span>
            </div>
            <div class="min-h-0 flex-1 overflow-auto px-5 py-3">
              <table class="w-full text-[12.5px]">
                <thead class="text-left text-[11.5px] text-muted-foreground">
                  <tr>
                    <th class="py-1.5 font-medium">{$_('managePlaylists.colStage')}</th>
                    <th class="py-1.5 font-medium">{$_('managePlaylists.colSeries')}</th>
                    <th class="py-1.5 font-medium">{$_('managePlaylists.colPlaylist')}</th>
                    <th class="py-1.5 font-medium">{$_('managePlaylists.colOrder')}</th>
                    <th class="w-16"></th>
                  </tr>
                </thead>
                <tbody>
                  {#each filteredStages as stage (stage.id)}
                    {@const changed = stage.playlistId !== stage.vanillaPlaylistId || stage.order !== stage.vanillaOrder}
                    <tr class="border-t border-border {stage.hidden ? 'text-muted-foreground' : ''}">
                      <td class="py-1.5 pr-3">{stage.name}</td>
                      <td class="py-1.5 pr-3 text-muted-foreground">{stage.seriesName}</td>
                      <td class="py-1.5 pr-3">
                        <select bind:value={stage.playlistId} aria-label={$_('managePlaylists.colPlaylist')} class="{inputClass} max-w-[320px]">
                          {#each data.playlists as playlist (playlist.id)}
                            <option value={playlist.id}>{playlist.name} ({playlist.id})</option>
                          {/each}
                        </select>
                      </td>
                      <td class="py-1.5 pr-3">
                        <select bind:value={stage.order} aria-label={$_('managePlaylists.colOrder')} class="{inputClass} w-[64px]">
                          {#each ORDER_SLOTS as s (s)}
                            <option value={s}>{s}</option>
                          {/each}
                        </select>
                      </td>
                      <td class="py-1.5">
                        <div class="flex items-center justify-end gap-1">
                          {#if silentIds.has(stage.id)}
                            <span title={$_('managePlaylists.silentStage')} class="text-amber-600 dark:text-amber-300"><TriangleAlert size={14} /></span>
                          {/if}
                          {#if changed}
                            <button
                              onclick={() => { stage.playlistId = stage.vanillaPlaylistId; stage.order = stage.vanillaOrder }}
                              title={$_('managePlaylists.resetStage', { values: { playlist: playlistNames.get(stage.vanillaPlaylistId) ?? stage.vanillaPlaylistId, order: stage.vanillaOrder } })}
                              aria-label={$_('managePlaylists.resetStage', { values: { playlist: stage.vanillaPlaylistId, order: stage.vanillaOrder } })}
                              class="inline-flex h-7 w-7 items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-muted hover:text-foreground"
                            >
                              <RotateCcw size={14} />
                            </button>
                          {/if}
                        </div>
                      </td>
                    </tr>
                  {/each}
                </tbody>
              </table>
            </div>
          </div>
        {/if}
      </div>
    </section>
  </div>
</div>

{#if showAddSong && selectedPlaylist}
  {@const playlist = selectedPlaylist}
  <Modal maxWidth="480px" maxHeight="80vh">
    <div class="flex flex-col gap-3 px-5 py-4">
      <h3 class="text-sm font-semibold">{$_('managePlaylists.addSongTitle')}</h3>
      <div class="flex items-center gap-2 rounded-lg border border-input bg-background px-2.5 py-1.5">
        <Search size={14} class="shrink-0 text-muted-foreground" />
        <input type="text" bind:value={addSearch} placeholder={$_('managePlaylists.searchSongs')} class="w-full bg-transparent text-[13px] focus:outline-none" />
      </div>
    </div>
    <div class="min-h-0 flex-1 overflow-auto px-5 pb-2">
      {#if addablePool.length === 0}
        <p class="px-1 py-4 text-[12.5px] text-muted-foreground">{$_('managePlaylists.noSongsToAdd')}</p>
      {:else}
        <div class="flex flex-col gap-1">
          {#each addablePool as song (song.bgmId)}
            <button
              onclick={() => addSongs(playlist, [song.bgmId])}
              class="flex w-full items-center gap-2 rounded-lg border border-border bg-background/60 px-3 py-2 text-left transition-colors hover:bg-muted"
            >
              <div class="min-w-0 flex-1">
                <div class="truncate text-[13px]">{song.title}</div>
                <div class="truncate text-[11px] text-muted-foreground">{song.seriesName}</div>
              </div>
              {#if song.isMod}
                <span class="shrink-0 rounded-full bg-muted px-1.5 py-0.5 text-[10px] text-muted-foreground">{$_('managePlaylists.modSong')}</span>
              {/if}
              <Plus size={14} class="shrink-0 text-muted-foreground" />
            </button>
          {/each}
        </div>
      {/if}
    </div>
    <div class="flex shrink-0 justify-end gap-2 border-t border-border px-5 py-4">
      <button onclick={() => (showAddSong = false)} class={toolbarButtonClass}>{$_('managePlaylists.done')}</button>
    </div>
  </Modal>
{/if}

{#if showMigrate && data}
  <Modal maxWidth="460px">
    <div class="flex flex-col gap-3 px-5 py-4">
      <h3 class="flex items-center gap-2 text-sm font-semibold">
        <TriangleAlert size={16} class="text-amber-600 dark:text-amber-300" />
        {$_('managePlaylists.migrateTitle')}
      </h3>
      <p class="text-[12.5px] text-muted-foreground">{$_('managePlaylists.migrateBody')}</p>
      <p class="text-[12.5px] font-medium">{$_('managePlaylists.migrateBackup')}</p>
    </div>
    <div class="flex justify-end gap-2 border-t border-border px-5 py-4">
      <button onclick={() => (showMigrate = false)} class={toolbarButtonClass}>{$_('managePlaylists.notNow')}</button>
      <button onclick={handleSave} disabled={saveState === 'saving'} class={toolbarButtonClass}>{$_('managePlaylists.migrate')}</button>
    </div>
  </Modal>
{/if}

{#if creating}
  {@const form = creating}
  <Modal maxWidth="420px">
    <div class="flex flex-col gap-3 px-5 py-4">
      <h3 class="text-sm font-semibold">{$_('managePlaylists.newPlaylistTitle')}</h3>
      <label class="flex flex-col gap-1 text-[12px] text-muted-foreground">
        {$_('managePlaylists.playlistName')}
        <input bind:value={form.name} oninput={() => (form.id = playlistIdFromName(form.name))} class={inputClass} />
      </label>
      <label class="flex flex-col gap-1 text-[12px] text-muted-foreground">
        {$_('managePlaylists.playlistId')}
        <input bind:value={form.id} class="{inputClass} font-mono" />
      </label>
      {#if createError}
        <p class="text-[12px] text-destructive">{$_(createError === 'taken' ? 'managePlaylists.idTaken' : 'managePlaylists.idInvalid')}</p>
      {/if}
    </div>
    <div class="flex justify-end gap-2 border-t border-border px-5 py-4">
      <button onclick={() => (creating = null)} class={toolbarButtonClass}>{$_('managePlaylists.cancel')}</button>
      <button onclick={createPlaylist} disabled={Boolean(createError)} class={toolbarButtonClass}>{$_('managePlaylists.create')}</button>
    </div>
  </Modal>
{/if}
