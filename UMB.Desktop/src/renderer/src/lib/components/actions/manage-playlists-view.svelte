<script lang="ts">
  import ListPlus from '@lucide/svelte/icons/list-plus'
  import RefreshCw from '@lucide/svelte/icons/refresh-cw'
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
  import ActionHeader from '$lib/components/ui/action-header.svelte'
  import EmptyState from '$lib/components/ui/empty-state.svelte'
  import SearchInput from '$lib/components/ui/search-input.svelte'
  import TabBar from '$lib/components/ui/tab-bar.svelte'
  import Modal from '$lib/components/ui/modal.svelte'
  import SearchableList from '$lib/components/ui/searchable-list.svelte'
  import { trackUnsaved } from '$lib/stores/unsaved.svelte'
  import { matchesSearch } from '$lib/search'
  import { inputClass } from '$lib/ui-classes'
  import {
    ORDER_SLOTS,
    addSong,
    clampIncidence,
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
  const iconButtonClass =
    'inline-flex h-7 w-7 shrink-0 items-center justify-center rounded text-dim transition-colors duration-100 hover:bg-accent hover:text-foreground'

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

  // Plain, not $state: the load effect must not depend on it.
  let loadedPath: string | null = null

  async function loadData(modPath: string | null) {
    if (!modPath) {
      data = null
      return
    }
    // Never show (or save) another mod's data while this one loads.
    if (loadedPath !== modPath) data = null
    loading = true
    saveState = 'idle'
    showAddSong = false
    try {
      setData(await window.electron.umb.loadManagePlaylists(modPath))
      loadedPath = modPath
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
      track.incidences = track.incidences.map(clampIncidence)
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

{#snippet removeButton(playlist: ManagedPlaylist, track: PlaylistTrack)}
  <button
    onclick={() => removeTrack(playlist, track.bgmId)}
    disabled={track.autoAdded}
    aria-label={$_('managePlaylists.removeSong')}
    title={$_(track.autoAdded ? 'managePlaylists.autoAdded' : 'managePlaylists.removeSong')}
    class="{iconButtonClass} disabled:cursor-not-allowed disabled:opacity-40"
  >
    <X size={14} />
  </button>
{/snippet}

<div class="flex-1 overflow-hidden">
  <div class="flex h-full min-h-0">
    <section class="surface-grain flex h-full min-h-0 w-full flex-col overflow-hidden bg-card">
      <ActionHeader inline title={$_('managePlaylists.title')} subtitle={activeMod ? $_('managePlaylists.subtitle') : $_('common.chooseMod')}>
        <button onclick={() => void loadData(activeMod?.path ?? null)} class="btn w-[27px] shrink-0 px-0" title={$_('managePlaylists.reload')}>
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
      </ActionHeader>

      {#if data}
        <TabBar
          bind:active={tab}
          tabs={[{ id: 'playlists', label: $_('managePlaylists.tabPlaylists') }, { id: 'stages', label: $_('managePlaylists.tabStages') }]}
        />
      {/if}

      {#if data?.legacyBlocks}
        <div class="flex shrink-0 items-center gap-2 border-b border-border bg-warn/10 px-[22px] py-2 text-[12.5px] text-warn">
          <TriangleAlert size={14} class="shrink-0" />
          <span class="flex-1">{$_('managePlaylists.legacyBanner')}</span>
          <button onclick={() => (showMigrate = true)} class="btn">{$_('managePlaylists.migrate')}</button>
        </div>
      {/if}

      {#if data && silent.length > 0}
        <div class="flex shrink-0 items-center gap-2 border-b border-border bg-warn/10 px-[22px] py-2 text-[12.5px] text-warn">
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
          <EmptyState title={$_('managePlaylists.chooseMod')} body={$_('common.chooseMod')}>
            {#snippet icon()}<ListPlus size={20} />{/snippet}
          </EmptyState>
        {:else if tab === 'playlists'}
          <div class="flex h-full min-h-0">
            <!-- Left: playlist list -->
            <div class="flex h-full w-[280px] shrink-0 flex-col border-r border-border">
              <div class="shrink-0 px-3 pt-3">
                <button onclick={() => (creating = { name: '', id: 'bgm' })} class="btn w-full">
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
            <div class="min-h-0 flex-1 overflow-auto px-[22px] py-3.5">
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
                      {/if}
                    </div>
                    <div class="flex shrink-0 items-center gap-2">
                      <button onclick={() => { addSearch = ''; showAddSong = true }} class="btn">
                        <Plus size={14} />
                        {$_('managePlaylists.addSong')}
                      </button>
                      {#if isChanged(playlist, defaults)}
                        <button
                          onclick={() => resetToDefault(playlist, defaults)}
                          class="btn w-[27px] px-0"
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
                          class="btn w-[27px] px-0 hover:text-destructive"
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
                          class="relative h-7 w-8 rounded border text-[12px] tabular-nums transition-colors duration-100 {slot === s ? 'border-transparent bg-accbg font-medium text-accink' : 'border-line2 hover:border-border hover:bg-accent'}"
                        >
                          {s}
                          {#if users.length > 0}
                            <span class="absolute -right-0.5 -top-0.5 h-2 w-2 rounded-full bg-primary"></span>
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
                    <p class="rounded-lg border border-dashed border-border px-3 py-6 text-center text-[12.5px] text-muted-foreground">
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
                      <div animate:flip={{ duration: FLIP_MS }} class="flex items-center gap-2 rounded border border-line2 bg-card px-2 py-1 transition-colors duration-100 hover:border-border" role="listitem">
                        <span class="w-6 text-right text-[11px] tabular-nums text-dim">{i + 1}</span>
                        <div use:dragHandle aria-label={$_('managePlaylists.dragAria')} class="flex h-6 w-6 shrink-0 cursor-grab items-center justify-center rounded text-dim hover:bg-accent active:cursor-grabbing">
                          <GripVertical size={14} />
                        </div>
                        <div class="min-w-0 flex-1">
                          <div class="truncate text-[13px]">{title(item.id)}</div>
                          <div class="truncate text-[11px] text-muted-foreground">{song?.seriesName ?? item.id}</div>
                        </div>
                        {#if song?.isMod}
                          <span class="chip-sec shrink-0">{$_('managePlaylists.modSong')}</span>
                        {/if}
                        <label class="flex shrink-0 items-center gap-1.5" title={$_('managePlaylists.incidenceHint')}>
                          <span class="text-[11px] text-muted-foreground">{$_('managePlaylists.colIncidence')}</span>
                          <input type="number" min="0" max="65535" bind:value={item.track.incidences[slot]} class="{inputClass} w-[78px] tabular-nums" />
                        </label>
                        <button
                          onclick={() => setIncluded(playlist, slot, item.track, false)}
                          aria-label={$_('managePlaylists.exclude')}
                          title={$_('managePlaylists.exclude')}
                          class={iconButtonClass}
                        >
                          <EyeOff size={14} />
                        </button>
                        {@render removeButton(playlist, item.track)}
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
                            class={iconButtonClass}
                          >
                            <Eye size={14} />
                          </button>
                          {@render removeButton(playlist, track)}
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
            <div class="flex shrink-0 items-center gap-3 border-b border-border px-[22px] py-3">
              <div class="w-[320px]">
                <SearchInput bind:value={stageSearch} placeholder={$_('managePlaylists.searchStages')} />
              </div>
              <span class="text-[11.5px] text-muted-foreground">{$_('managePlaylists.stageCount', { values: { count: filteredStages.length } })}</span>
            </div>
            <div class="min-h-0 flex-1 overflow-auto px-[22px] py-3">
              <table class="w-full text-[12.5px]">
                <thead class="text-left text-[10.5px] uppercase tracking-[.1em] text-dim">
                  <tr>
                    <th class="py-1.5 font-semibold">{$_('managePlaylists.colStage')}</th>
                    <th class="py-1.5 font-semibold">{$_('managePlaylists.colSeries')}</th>
                    <th class="py-1.5 font-semibold">{$_('managePlaylists.colPlaylist')}</th>
                    <th class="py-1.5 font-semibold">{$_('managePlaylists.colOrder')}</th>
                    <th class="w-16"></th>
                  </tr>
                </thead>
                <tbody>
                  {#each filteredStages as stage (stage.id)}
                    {@const changed = stage.playlistId !== stage.vanillaPlaylistId || stage.order !== stage.vanillaOrder}
                    <tr class="border-t border-line2 transition-colors duration-100 hover:bg-accent {stage.hidden ? 'text-muted-foreground' : ''}">
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
                            <span title={$_('managePlaylists.silentStage')} class="text-warn"><TriangleAlert size={14} /></span>
                          {/if}
                          {#if changed}
                            <button
                              onclick={() => { stage.playlistId = stage.vanillaPlaylistId; stage.order = stage.vanillaOrder }}
                              title={$_('managePlaylists.resetStage', { values: { playlist: playlistNames.get(stage.vanillaPlaylistId) ?? stage.vanillaPlaylistId, order: stage.vanillaOrder } })}
                              aria-label={$_('managePlaylists.resetStage', { values: { playlist: stage.vanillaPlaylistId, order: stage.vanillaOrder } })}
                              class={iconButtonClass}
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
      <h3 class="text-[13.5px] font-semibold">{$_('managePlaylists.addSongTitle')}</h3>
      <SearchInput bind:value={addSearch} placeholder={$_('managePlaylists.searchSongs')} />
    </div>
    <div class="min-h-0 flex-1 overflow-auto px-5 pb-2">
      {#if addablePool.length === 0}
        <p class="px-1 py-4 text-[12.5px] text-muted-foreground">{$_('managePlaylists.noSongsToAdd')}</p>
      {:else}
        <div class="flex flex-col gap-1">
          {#each addablePool as song (song.bgmId)}
            <button
              onclick={() => addSong(playlist, song.bgmId)}
              class="flex w-full items-center gap-2 rounded-xl border border-line2 px-3 py-2 text-left transition-colors duration-100 hover:border-border hover:bg-accent"
            >
              <div class="min-w-0 flex-1">
                <div class="truncate text-[13px]">{song.title}</div>
                <div class="truncate text-[11px] text-muted-foreground">{song.seriesName}</div>
              </div>
              {#if song.isMod}
                <span class="chip-sec shrink-0">{$_('managePlaylists.modSong')}</span>
              {/if}
              <Plus size={14} class="shrink-0 text-dim" />
            </button>
          {/each}
        </div>
      {/if}
    </div>
    <div class="flex shrink-0 justify-end gap-2 border-t border-border px-5 py-4">
      <button onclick={() => (showAddSong = false)} class="btn">{$_('managePlaylists.done')}</button>
    </div>
  </Modal>
{/if}

{#if showMigrate && data}
  <Modal maxWidth="460px">
    <div class="flex flex-col gap-3 px-5 py-4">
      <h3 class="flex items-center gap-2 text-[13.5px] font-semibold">
        <TriangleAlert size={15} class="text-warn" />
        {$_('managePlaylists.migrateTitle')}
      </h3>
      <p class="text-[12.5px] text-muted-foreground">{$_('managePlaylists.migrateBody')}</p>
      <p class="text-[12.5px] font-medium">{$_('managePlaylists.migrateBackup')}</p>
    </div>
    <div class="flex justify-end gap-2 border-t border-border px-5 py-4">
      <button onclick={() => (showMigrate = false)} class="btn">{$_('managePlaylists.notNow')}</button>
      <button onclick={handleSave} disabled={saveState === 'saving'} class="btn-primary">{$_('managePlaylists.migrate')}</button>
    </div>
  </Modal>
{/if}

{#if creating}
  {@const form = creating}
  <Modal maxWidth="420px">
    <div class="flex flex-col gap-3 px-5 py-4">
      <h3 class="text-[13.5px] font-semibold">{$_('managePlaylists.newPlaylistTitle')}</h3>
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
      <button onclick={() => (creating = null)} class="btn">{$_('managePlaylists.cancel')}</button>
      <button onclick={createPlaylist} disabled={Boolean(createError)} class="btn-primary">{$_('managePlaylists.create')}</button>
    </div>
  </Modal>
{/if}
