<script lang="ts">
  import SearchInput from './search-input.svelte'

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
    <SearchInput bind:value={search} {placeholder} />
    <div class="px-1 pt-2 text-[11px] text-dim">{countText}</div>
  </div>
  <div class="min-h-0 flex-1 overflow-auto px-2 pb-2">
    {#if items.length === 0}
      <p class="px-2 py-4 text-[12.5px] text-muted-foreground">{emptyText}</p>
    {:else}
      {#each items as item (item.id)}
        {@const isActive = item.id === activeId}
        <button
          onclick={() => onSelect(item.id)}
          class="mb-px flex h-7 w-full items-center gap-2 rounded px-2.5 text-left text-[13px] transition-colors duration-100 {isActive ? 'bg-accbg font-medium text-accink' : 'text-muted-foreground hover:bg-accent hover:text-foreground'}"
        >
          <span class="min-w-0 flex-1 truncate">{item.label}</span>
          {#if item.badge}
            <span class="chip h-[18px] shrink-0 px-1.5 text-[10.5px] tabular-nums">{item.badge}</span>
          {/if}
        </button>
      {/each}
    {/if}
  </div>
</div>
