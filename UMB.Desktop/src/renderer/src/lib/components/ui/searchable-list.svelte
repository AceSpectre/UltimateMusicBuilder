<script lang="ts">
  import Search from '@lucide/svelte/icons/search'

  // Search box, count line and selectable rows, shared by Manage Playlists and the series sidebars.
  // Callers filter `items` themselves (see matchesSearch) so they can react to the filtered list.
  let {
    items,
    activeId,
    onSelect,
    search = $bindable(''),
    placeholder,
    countText,
    emptyText
  }: {
    /** badge: optional count shown as a pill when above zero. */
    items: { id: string; label: string; badge?: number }[]
    activeId: string | null
    onSelect: (id: string) => void
    search?: string
    placeholder: string
    countText: string
    emptyText: string
  } = $props()
</script>

<div class="flex min-h-0 flex-1 flex-col">
  <div class="shrink-0 p-3">
    <div class="flex items-center gap-2 rounded-lg border border-input bg-background px-2.5 py-1.5">
      <Search size={14} class="shrink-0 text-muted-foreground" />
      <input type="text" bind:value={search} {placeholder} class="w-full bg-transparent text-[13px] focus:outline-none" />
    </div>
    <div class="px-1 pt-2 text-[11px] text-muted-foreground">{countText}</div>
  </div>
  <div class="min-h-0 flex-1 overflow-auto px-2 pb-2">
    {#if items.length === 0}
      <p class="px-2 py-4 text-[12.5px] text-muted-foreground">{emptyText}</p>
    {:else}
      {#each items as item (item.id)}
        {@const isActive = item.id === activeId}
        <button
          onclick={() => onSelect(item.id)}
          class="mb-0.5 flex w-full items-center gap-2 rounded-lg px-2.5 py-1.5 text-left text-[13px] transition-colors {isActive ? 'font-semibold' : 'text-foreground/85 hover:bg-muted'}"
          style={isActive ? 'background: linear-gradient(135deg, hsl(var(--gradient-from) / .14), hsl(var(--gradient-to) / .14));' : ''}
        >
          <span class="min-w-0 flex-1 truncate">{item.label}</span>
          {#if item.badge}
            <span class="shrink-0 rounded-full bg-muted px-1.5 py-0.5 text-[10px] tabular-nums text-muted-foreground">{item.badge}</span>
          {/if}
        </button>
      {/each}
    {/if}
  </div>
</div>
