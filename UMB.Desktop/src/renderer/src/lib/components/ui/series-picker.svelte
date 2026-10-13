<script lang="ts">
  import type { Snippet } from 'svelte'
  import Folder from '@lucide/svelte/icons/folder'
  import PanelLeftClose from '@lucide/svelte/icons/panel-left-close'
  import PanelLeftOpen from '@lucide/svelte/icons/panel-left-open'
  import RefreshCw from '@lucide/svelte/icons/refresh-cw'
  import { _ } from 'svelte-i18n'
  import IconButton from './icon-button.svelte'
  import SearchableList from './searchable-list.svelte'
  import { matchesSearch } from '$lib/search'
  import type { ModInfo, ModSeriesInfo } from '$lib/types/electron'

  // Left-hand series sidebar shared by Manage Songs / Config Volume / Nus3 Convert.
  let {
    activeMod,
    series,
    loading,
    activePath,
    onSelect,
    onReload,
    spinning = loading,
    actions
  }: {
    activeMod: ModInfo | null
    series: ModSeriesInfo[]
    loading: boolean
    activePath: string | null
    onSelect: (path: string) => void
    onReload: () => void
    spinning?: boolean
    actions?: Snippet
  } = $props()

  let collapsed = $state(false)
  let search = $state('')
  const filtered = $derived(series.filter((item) => matchesSearch(search, item.name)))
</script>

<section
  class="surface-grain flex h-full min-h-0 shrink-0 flex-col overflow-hidden border-r border-border bg-rail {collapsed ? 'w-[46px]' : 'w-[220px]'}"
>
  {#if collapsed}
    <div class="flex flex-1 flex-col items-center gap-2 py-3">
      <IconButton onclick={() => (collapsed = false)} title={$_('seriesPicker.expand')}>
        <PanelLeftOpen size={15} />
      </IconButton>
      <Folder size={14} class="text-dim" />
      <span class="text-[11px] text-dim">{series.length}</span>
    </div>
  {:else}
    <div class="shrink-0 border-b border-line2 px-3 py-2">
      <div class="flex items-center gap-1.5">
        <h2 class="min-w-0 flex-1 truncate text-[10.5px] font-semibold uppercase tracking-[.1em] text-dim">{$_('seriesPicker.heading')}</h2>
        {#if actions}
          {@render actions()}
        {/if}
        <IconButton onclick={onReload} title={$_('seriesPicker.reload')}>
          <RefreshCw size={14} class={spinning ? 'animate-spin' : ''} />
        </IconButton>
        <IconButton onclick={() => (collapsed = true)} title={$_('seriesPicker.collapse')}>
          <PanelLeftClose size={14} />
        </IconButton>
      </div>
    </div>

    {#if activeMod && series.length > 0}
      <SearchableList
        items={filtered.map((item) => ({ id: item.path, label: item.name }))}
        activeId={activePath}
        {onSelect}
        bind:search
        placeholder={$_('seriesPicker.search')}
        countText={$_('seriesPicker.count', { values: { count: filtered.length } })}
        emptyText={$_('seriesPicker.noMatch')}
      />
    {:else}
      <div class="min-h-0 flex-1 overflow-auto p-2">
        <div class="rounded-xl border border-dashed border-border px-3 py-8 text-center text-[12.5px] text-muted-foreground">
          {$_(!activeMod ? 'common.chooseMod' : loading ? 'seriesPicker.loading' : 'seriesPicker.noSeries')}
        </div>
      </div>
    {/if}
  {/if}
</section>
