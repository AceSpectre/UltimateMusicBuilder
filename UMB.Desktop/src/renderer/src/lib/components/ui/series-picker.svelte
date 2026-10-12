<script lang="ts">
  import type { Snippet } from 'svelte'
  import { Folder, PanelLeftClose, PanelLeftOpen, RefreshCw } from '@lucide/svelte'
  import { _ } from 'svelte-i18n'
  import GradientIcon from './gradient-icon.svelte'
  import IconButton from './icon-button.svelte'
  import SearchableList from './searchable-list.svelte'
  import { matchesSearch } from '$lib/search'
  import type { ModInfo, ModSeriesInfo } from '$lib/types/electron'

  // Left-hand series sidebar shared by Manage Songs / Volume Config / Nus3 Convert.
  // All display strings come in as props so each view keeps its own locale keys.
  let {
    activeMod,
    series,
    loading,
    activePath,
    onSelect,
    onReload,
    heading,
    chooseModText,
    loadingText,
    emptyText,
    reloadTitle,
    spinning = loading,
    actions
  }: {
    activeMod: ModInfo | null
    series: ModSeriesInfo[]
    loading: boolean
    activePath: string | null
    onSelect: (path: string) => void
    onReload: () => void
    heading: string
    chooseModText: string
    loadingText: string
    emptyText: string
    reloadTitle: string
    spinning?: boolean
    actions?: Snippet
  } = $props()

  let collapsed = $state(false)
  let search = $state('')
  const filtered = $derived(series.filter((item) => matchesSearch(search, item.name)))
</script>

<section
  class="flex h-full min-h-0 shrink-0 flex-col border border-border bg-card overflow-hidden transition-[width] duration-150 {collapsed ? 'w-[46px]' : 'w-[220px]'}"
>
  <div class="gradient-strip h-[3px] shrink-0"></div>
  {#if collapsed}
    <div class="flex flex-1 flex-col items-center gap-2 py-3">
      <IconButton onclick={() => (collapsed = false)} title={$_('seriesPicker.expand')}>
        <PanelLeftOpen size={15} />
      </IconButton>
      <GradientIcon size="sm">
        <Folder size={16} />
      </GradientIcon>
      <span class="text-[11px] text-muted-foreground">{series.length}</span>
    </div>
  {:else}
    <div class="shrink-0 border-b border-border px-4 py-3">
      <div class="flex items-center gap-2">
        <GradientIcon>
          <Folder size={18} />
        </GradientIcon>
        <h2 class="min-w-0 flex-1 truncate text-sm font-semibold">{heading}</h2>
        {#if actions}
          {@render actions()}
        {/if}
        <IconButton onclick={onReload} title={reloadTitle}>
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
        {#if !activeMod}
          <div class="rounded-xl border border-dashed border-border bg-background/70 px-3 py-8 text-center text-[13px] text-muted-foreground">
            {chooseModText}
          </div>
        {:else if loading}
          <div class="rounded-xl border border-dashed border-border bg-background/70 px-3 py-8 text-center text-[13px] text-muted-foreground">
            {loadingText}
          </div>
        {:else}
          <div class="rounded-xl border border-dashed border-border bg-background/70 px-3 py-8 text-center text-[13px] text-muted-foreground">
            {emptyText}
          </div>
        {/if}
      </div>
    {/if}
  {/if}
</section>
