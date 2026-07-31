<script lang="ts">
  import { ListMusic, RefreshCw, Search, AlertTriangle } from '@lucide/svelte'
  import { onMount } from 'svelte'
  import { _ } from 'svelte-i18n'
  import GradientIcon from '$lib/components/ui/gradient-icon.svelte'
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
    <section class="flex h-full min-h-0 w-full flex-col border border-border bg-card overflow-hidden">
      <div class="gradient-strip h-[3px] shrink-0"></div>

      <div class="shrink-0 border-b border-border px-5 py-3 flex items-center gap-3">
        <GradientIcon>
          <ListMusic size={18} />
          </GradientIcon>
        <div class="min-w-0">
          <h2 class="truncate text-sm font-semibold">{$_('playlistInfo.title')}</h2>
          <p class="truncate text-[12.5px] text-muted-foreground">{$_('playlistInfo.subtitle')}</p>
        </div>
      </div>

      <div class="shrink-0 border-b border-border px-5 flex items-center gap-1">
        <button
          onclick={() => (tab = 'playlists')}
          class="relative px-3 py-2.5 text-[13px] font-medium transition-colors {tab === 'playlists' ? 'text-foreground' : 'text-muted-foreground hover:text-foreground'}"
        >
          {$_('playlistInfo.tabPlaylists')}
          {#if tab === 'playlists'}
            <div class="absolute inset-x-2 bottom-0 h-[2px] rounded-t gradient-bg"></div>
          {/if}
        </button>
        <button
          onclick={() => (tab = 'stages')}
          class="relative px-3 py-2.5 text-[13px] font-medium transition-colors {tab === 'stages' ? 'text-foreground' : 'text-muted-foreground hover:text-foreground'}"
        >
          {$_('playlistInfo.tabStages')}
          {#if tab === 'stages'}
            <div class="absolute inset-x-2 bottom-0 h-[2px] rounded-t gradient-bg"></div>
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
          <div class="flex h-full items-center justify-center gap-2 text-[13px] text-red-500">
            <AlertTriangle size={15} />
            {$_('playlistInfo.loadError')}
          </div>
        {:else if tab === 'playlists'}
          <div class="flex h-full flex-col">
            <div class="shrink-0 px-5 pt-4 pb-2 text-[12px] text-muted-foreground">
              {$_('playlistInfo.playlistCount', { values: { count: data.playlists.length } })}
            </div>
            <div class="min-h-0 flex-1 overflow-auto px-5 pb-5">
              <table class="w-full border-collapse text-[12.5px]">
                <thead class="sticky top-0 z-10">
                  <tr class="bg-card">
                    <th class="border-b border-border px-3 py-2 text-left font-medium text-muted-foreground">{$_('playlistInfo.colPlaylist')}</th>
                    <th class="border-b border-border px-3 py-2 text-left font-medium text-muted-foreground">{$_('playlistInfo.colSeries')}</th>
                    <th class="border-b border-border px-3 py-2 text-right font-medium text-muted-foreground">{$_('playlistInfo.colSongs')}</th>
                  </tr>
                </thead>
                <tbody>
                  {#each data.playlists as playlist}
                    <tr class="border-b border-border last:border-b-0 hover:bg-muted/50">
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
                <div class="flex items-center gap-2 rounded-lg border border-input bg-background px-2.5 py-1.5">
                  <Search size={14} class="shrink-0 text-muted-foreground" />
                  <input
                    type="text"
                    bind:value={stageSearch}
                    placeholder={$_('playlistInfo.searchStages')}
                    class="w-full bg-transparent text-[13px] focus:outline-none"
                  />
                </div>
                <div class="px-1 pt-2 text-[11px] text-muted-foreground">
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
                      class="mb-0.5 flex w-full items-center gap-2 rounded-lg px-2.5 py-1.5 text-left text-[13px] transition-colors {isActive ? 'font-semibold' : 'text-foreground/85 hover:bg-muted'}"
                      style={isActive
                        ? 'background: linear-gradient(135deg, hsl(var(--gradient-from) / .14), hsl(var(--gradient-to) / .14));'
                        : ''}
                    >
                      <span class="truncate {stage.hidden ? 'text-muted-foreground' : ''}">{stage.name}</span>
                    </button>
                  {/each}
                {/if}
              </div>
            </div>

            <div class="min-h-0 flex-1 overflow-auto p-5">
              {#if !selectedStage}
                <div class="flex h-full items-center justify-center text-center text-[13px] text-muted-foreground">
                  {$_('playlistInfo.selectStage')}
                </div>
              {:else}
                <div class="flex flex-col gap-4">
                  <div class="flex items-center gap-2">
                    <h3 class="text-base font-semibold">{selectedStage.name}</h3>
                    {#if selectedStage.hidden}
                      <span class="rounded border border-border px-1.5 py-0.5 text-[10px] uppercase tracking-wide text-muted-foreground">{$_('playlistInfo.hiddenBadge')}</span>
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
                        <div class="rounded-lg border border-border bg-background">
                          {#each selectedStage.songs as song, i}
                            <div class="flex items-center gap-3 border-b border-border px-3 py-1.5 text-[12.5px] last:border-b-0">
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
