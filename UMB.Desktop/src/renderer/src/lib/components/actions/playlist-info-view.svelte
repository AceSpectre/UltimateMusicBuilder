<script lang="ts">
  import RefreshCw from '@lucide/svelte/icons/refresh-cw'
  import AlertTriangle from '@lucide/svelte/icons/alert-triangle'
  import { onMount } from 'svelte'
  import { _ } from 'svelte-i18n'
  import ActionHeader from '$lib/components/ui/action-header.svelte'
  import SearchInput from '$lib/components/ui/search-input.svelte'
  import type { PlaylistInfoData, StageInfo } from '$lib/types/electron'

  let data = $state<PlaylistInfoData | null>(null)
  let loading = $state(true)
  let error = $state(false)
  let tab = $state<'playlists' | 'stages'>('playlists')
  let selectedStageId = $state<string | null>(null)
  let stageSearch = $state('')

  onMount(async () => {
    try {
      data = await window.electron.umb.getPlaylistInfo()
    } catch {
      error = true
    } finally {
      loading = false
    }
  })

  const filteredStages = $derived(
    (data?.stages ?? []).filter((s) => {
      const q = stageSearch.trim().toLowerCase()
      if (!q) return true
      return s.name.toLowerCase().includes(q) || s.uiStageId.toLowerCase().includes(q)
    })
  )

  const selectedStage = $derived<StageInfo | null>(
    data?.stages.find((s) => s.uiStageId === selectedStageId) ?? null
  )

  // Auto-select the first stage once data arrives so the detail panel isn't empty.
  $effect(() => {
    if (tab === 'stages' && !selectedStageId && filteredStages.length > 0) {
      selectedStageId = filteredStages[0].uiStageId
    }
  })
</script>

<div class="flex-1 overflow-hidden">
  <div class="flex h-full min-h-0">
    <section class="surface-grain flex h-full min-h-0 w-full flex-col overflow-hidden bg-card">

      <ActionHeader title={$_('playlistInfo.title')} subtitle={$_('playlistInfo.subtitle')} />

      <div class="shrink-0 border-b border-line2 px-[22px] flex items-center gap-1">
        <button
          onclick={() => (tab = 'playlists')}
          class="relative px-3 py-2.5 text-[13px] font-medium transition-colors {tab === 'playlists' ? 'text-foreground' : 'text-muted-foreground hover:text-foreground'}"
        >
          {$_('playlistInfo.tabPlaylists')}
          {#if tab === 'playlists'}
            <div class="absolute inset-x-2 -bottom-px h-0.5 bg-primary"></div>
          {/if}
        </button>
        <button
          onclick={() => (tab = 'stages')}
          class="relative px-3 py-2.5 text-[13px] font-medium transition-colors {tab === 'stages' ? 'text-foreground' : 'text-muted-foreground hover:text-foreground'}"
        >
          {$_('playlistInfo.tabStages')}
          {#if tab === 'stages'}
            <div class="absolute inset-x-2 -bottom-px h-0.5 bg-primary"></div>
          {/if}
        </button>
      </div>

      <div class="min-h-0 flex-1 overflow-hidden">
        {#if loading}
          <div class="flex h-full items-center justify-center gap-2 text-[13px] text-muted-foreground">
            <RefreshCw size={15} class="animate-spin" />
            {$_('playlistInfo.loading')}
          </div>
        {:else if error || !data}
          <div class="flex h-full items-center justify-center gap-2 text-[13px] text-destructive">
            <AlertTriangle size={15} />
            {$_('playlistInfo.loadError')}
          </div>
        {:else if tab === 'playlists'}
          <div class="flex h-full flex-col">
            <div class="shrink-0 px-[22px] pt-3.5 pb-2 text-[12px] text-muted-foreground">
              {$_('playlistInfo.playlistCount', { values: { count: data.playlists.length } })}
            </div>
            <div class="min-h-0 flex-1 overflow-auto px-[22px] pb-5">
              <table class="w-full border-collapse text-[12.5px]">
                <thead class="sticky top-0 z-10">
                  <tr class="bg-card">
                    <th class="border-b border-border px-3 py-2 text-left text-[10.5px] font-semibold uppercase tracking-[.1em] text-dim">{$_('playlistInfo.colPlaylist')}</th>
                    <th class="border-b border-border px-3 py-2 text-left text-[10.5px] font-semibold uppercase tracking-[.1em] text-dim">{$_('playlistInfo.colSeries')}</th>
                    <th class="border-b border-border px-3 py-2 text-right text-[10.5px] font-semibold uppercase tracking-[.1em] text-dim">{$_('playlistInfo.colSongs')}</th>
                  </tr>
                </thead>
                <tbody>
                  {#each data.playlists as playlist}
                    <tr class="border-b border-line2 last:border-b-0 hover:bg-accent">
                      <td class="px-3 py-2">
                        <div class="font-medium">{playlist.name}</div>
                        <div class="text-[11px] text-muted-foreground">{playlist.id}</div>
                      </td>
                      <td class="px-3 py-2 text-muted-foreground">
                        {playlist.series.length > 0 ? playlist.series.join(', ') : $_('playlistInfo.noSeries')}
                      </td>
                      <td class="px-3 py-2 text-right tabular-nums">{playlist.songCount}</td>
                    </tr>
                  {/each}
                </tbody>
              </table>
            </div>
          </div>
        {:else}
          <div class="flex h-full min-h-0">
            <div class="flex h-full w-[280px] shrink-0 flex-col border-r border-border">
              <div class="shrink-0 p-3">
                <SearchInput bind:value={stageSearch} placeholder={$_('playlistInfo.searchStages')} />
                <div class="px-1 pt-2 text-[11px] text-dim">
                  {$_('playlistInfo.stageCount', { values: { count: filteredStages.length } })}
                </div>
              </div>
              <div class="min-h-0 flex-1 overflow-auto px-2 pb-2">
                {#if filteredStages.length === 0}
                  <p class="px-2 py-4 text-[12.5px] text-muted-foreground">{$_('playlistInfo.noStageMatch')}</p>
                {:else}
                  {#each filteredStages as stage}
                    {@const isActive = stage.uiStageId === selectedStageId}
                    <button
                      onclick={() => (selectedStageId = stage.uiStageId)}
                      class="mb-px flex h-7 w-full items-center gap-2 rounded px-2.5 text-left text-[13px] transition-colors duration-100 {isActive ? 'bg-accbg font-medium text-accink' : 'text-muted-foreground hover:bg-accent hover:text-foreground'}"
                    >
                      <span class="truncate {stage.hidden ? 'opacity-60' : ''}">{stage.name}</span>
                    </button>
                  {/each}
                {/if}
              </div>
            </div>

            <div class="min-h-0 flex-1 overflow-auto px-[22px] py-3.5">
              {#if !selectedStage}
                <div class="flex h-full items-center justify-center text-center text-[13px] text-muted-foreground">
                  {$_('playlistInfo.selectStage')}
                </div>
              {:else}
                <div class="flex flex-col gap-4">
                  <div class="flex items-center gap-2">
                    <h3 class="text-base font-semibold">{selectedStage.name}</h3>
                    {#if selectedStage.hidden}
                      <span class="chip">{$_('playlistInfo.hiddenBadge')}</span>
                    {/if}
                  </div>

                  {#if !selectedStage.playlistId}
                    <p class="text-[13px] text-muted-foreground">{$_('playlistInfo.noPlaylist')}</p>
                  {:else}
                    <div class="grid grid-cols-[auto_1fr] gap-x-6 gap-y-1.5 text-[13px]">
                      <span class="text-muted-foreground">{$_('playlistInfo.fieldPlaylist')}</span>
                      <span>{selectedStage.playlistName} <span class="text-muted-foreground">({selectedStage.playlistId})</span></span>
                      <span class="text-muted-foreground">{$_('playlistInfo.fieldOrder')}</span>
                      <span class="tabular-nums">{selectedStage.order}</span>
                      <span class="text-muted-foreground">{$_('playlistInfo.fieldSeries')}</span>
                      <span>{selectedStage.seriesName}</span>
                    </div>

                    <div>
                      <div class="mb-2 text-[12.5px] font-medium text-muted-foreground">
                        {$_('playlistInfo.songsInOrder', { values: { order: selectedStage.order } })}
                      </div>
                      {#if selectedStage.songs.length === 0}
                        <p class="text-[12.5px] text-muted-foreground">{$_('playlistInfo.noSongs')}</p>
                      {:else}
                        <div class="rounded border border-line2">
                          {#each selectedStage.songs as song, i}
                            <div class="flex items-center gap-3 border-b border-line2 px-3 py-1.5 text-[12.5px] last:border-b-0">
                              <span class="w-6 shrink-0 text-right tabular-nums text-muted-foreground">{i + 1}.</span>
                              <span class="truncate">{song.name}</span>
                            </div>
                          {/each}
                        </div>
                      {/if}
                    </div>
                  {/if}
                </div>
              {/if}
            </div>
          </div>
        {/if}
      </div>
    </section>
  </div>
</div>
