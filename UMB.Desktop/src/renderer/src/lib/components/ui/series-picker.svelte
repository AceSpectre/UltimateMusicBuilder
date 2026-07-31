<script lang="ts">
  import type { Snippet } from 'svelte'
  import { Folder, PanelLeftClose, PanelLeftOpen, RefreshCw } from '@lucide/svelte'
  import GradientIcon from './gradient-icon.svelte'
  import IconButton from './icon-button.svelte'
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
    subtitleFallback,
    chooseModText,
    loadingText,
    emptyText,
    reloadTitle,
    spinning = loading,
    collapsible = false,
    collapseTitle = '',
    expandTitle = '',
    actions
  }: {
    activeMod: ModInfo | null
    series: ModSeriesInfo[]
    loading: boolean
    activePath: string | null
    onSelect: (path: string) => void
    onReload: () => void
    heading: string
    subtitleFallback: string
    chooseModText: string
    loadingText: string
    emptyText: string
    reloadTitle: string
    spinning?: boolean
    collapsible?: boolean
    collapseTitle?: string
    expandTitle?: string
    actions?: Snippet
  } = $props()

  let collapsed = $state(false)
</script>

<section
  class="flex h-full min-h-0 shrink-0 flex-col border border-border bg-card overflow-hidden transition-[width] duration-150 {collapsed ? 'w-[46px]' : 'w-[280px]'}"
>
  <div class="gradient-strip h-[3px] shrink-0"></div>
  {#if collapsed}
    <div class="flex flex-1 flex-col items-center gap-2 py-3">
      <IconButton onclick={() => (collapsed = false)} title={expandTitle}>
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
        <div class="min-w-0 flex-1">
          <h2 class="text-sm font-semibold">{heading}</h2>
          <p class="truncate text-[12.5px] text-muted-foreground">
            {activeMod?.name ?? subtitleFallback}
          </p>
        </div>
        <span class="shrink-0 text-[12px] text-muted-foreground">{series.length}</span>
        {#if actions}
          {@render actions()}
        {/if}
        <IconButton onclick={onReload} title={reloadTitle}>
          <RefreshCw size={14} class={spinning ? 'animate-spin' : ''} />
        </IconButton>
        {#if collapsible}
          <IconButton onclick={() => (collapsed = true)} title={collapseTitle}>
            <PanelLeftClose size={14} />
          </IconButton>
        {/if}
      </div>
    </div>

    <div class="min-h-0 flex-1 overflow-auto p-2">
      {#if !activeMod}
        <div class="rounded-xl border border-dashed border-border bg-background/70 px-3 py-8 text-center text-[13px] text-muted-foreground">
          {chooseModText}
        </div>
      {:else if loading && series.length === 0}
        <div class="rounded-xl border border-dashed border-border bg-background/70 px-3 py-8 text-center text-[13px] text-muted-foreground">
          {loadingText}
        </div>
      {:else if series.length === 0}
        <div class="rounded-xl border border-dashed border-border bg-background/70 px-3 py-8 text-center text-[13px] text-muted-foreground">
          {emptyText}
        </div>
      {:else}
        <div class="grid gap-1.5">
          {#each series as item}
            {@const isActive = activePath === item.path}
            <button
              onclick={() => onSelect(item.path)}
              class="w-full rounded-lg border px-2 py-1.5 text-left transition-colors {isActive ? 'border-transparent' : 'border-border bg-background/70 hover:bg-muted'}"
              style={isActive
                ? 'background: linear-gradient(135deg, hsl(var(--gradient-from) / .15), hsl(var(--gradient-to) / .18));'
                : ''}
            >
              <div class="truncate text-[13.5px] font-semibold">{item.name}</div>
            </button>
          {/each}
        </div>
      {/if}
    </div>
  {/if}
</section>
