<script lang="ts">
  import { Search } from '@lucide/svelte'
  import { sidebarStore } from '$lib/stores/sidebar.svelte'
  import { modsStore } from '$lib/stores/mods.svelte'

  let { open = $bindable(false) }: { open: boolean } = $props()
  let query = $state('')
  let inputEl: HTMLInputElement | undefined = $state()

  const actions = [
    { id: 'build', label: 'Build', group: 'Actions' },
    { id: 'scaffold', label: 'Scaffold', group: 'Actions' },
    { id: 'nus3-convert', label: 'Nus3 Convert', group: 'Actions' },
    { id: 'accept-nus3', label: 'Accept Nus3', group: 'Actions' },
    { id: 'config-volume', label: 'Config Volume', group: 'Actions' },
    { id: 'order-series', label: 'Order Series', group: 'Actions' },
    { id: 'order-tracks', label: 'Order Tracks', group: 'Actions' },
    { id: 'cleanup', label: 'Cleanup', group: 'Actions' },
    { id: 'convert', label: 'Import', group: 'Actions' },
    { id: 'merge', label: 'Merge', group: 'Actions' },
    { id: 'extract-icons', label: 'Extract Icons', group: 'Actions' },
    { id: 'dump-stages', label: 'Dump Stages', group: 'Actions' }
  ]

  const filtered = $derived(() => {
    if (!query.trim()) return actions
    const q = query.toLowerCase()
    return actions.filter(a => a.label.toLowerCase().includes(q))
  })

  function selectAction(id: string) {
    sidebarStore.setActive(id)
    window.electron.umb.runAction(id)
    open = false
    query = ''
  }

  function handleKeydown(e: KeyboardEvent) {
    if (e.key === 'Escape') {
      open = false
      query = ''
    }
  }

  $effect(() => {
    if (open) {
      setTimeout(() => inputEl?.focus(), 50)
    }
  })
</script>

{#if open}
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div
    class="fixed inset-0 z-50 flex items-start justify-center pt-[20vh] bg-black/50"
    onclick={() => { open = false; query = '' }}
    onkeydown={handleKeydown}
  >
    <!-- svelte-ignore a11y_no_static_element_interactions -->
    <div
      class="w-full max-w-[520px] rounded-xl border border-border bg-popover shadow-2xl overflow-hidden"
      onclick={(e) => e.stopPropagation()}
      onkeydown={handleKeydown}
    >
      <!-- Search input -->
      <div class="flex items-center gap-2 px-4 border-b border-border">
        <Search size={16} class="text-muted-foreground shrink-0" />
        <input
          bind:this={inputEl}
          bind:value={query}
          placeholder="Search actions..."
          class="flex-1 h-12 bg-transparent text-sm outline-none placeholder:text-muted-foreground"
        />
      </div>

      <!-- Results -->
      <div class="max-h-[300px] overflow-y-auto p-2">
        {#if filtered().length === 0}
          <div class="px-3 py-6 text-center text-sm text-muted-foreground">No results found.</div>
        {:else}
          <div class="px-2 py-1.5 text-[10.5px] font-semibold tracking-[.12em] uppercase text-muted-foreground">
            Actions
          </div>
          {#each filtered() as action}
            <button
              onclick={() => selectAction(action.id)}
              class="w-full flex items-center gap-3 px-3 py-2 rounded-lg text-sm text-left hover:bg-accent transition-colors
                {sidebarStore.activeTab === action.id ? 'bg-accent font-medium' : ''}"
            >
              {action.label}
            </button>
          {/each}
        {/if}

        {#if modsStore.mods.length > 0 && !query.trim()}
          <div class="mt-2 px-2 py-1.5 text-[10.5px] font-semibold tracking-[.12em] uppercase text-muted-foreground">
            Mods
          </div>
          {#each modsStore.mods as mod}
            <button
              onclick={() => { modsStore.setActive(mod); open = false }}
              class="w-full flex items-center gap-3 px-3 py-2 rounded-lg text-sm text-left hover:bg-accent transition-colors
                {modsStore.activeMod?.name === mod.name ? 'bg-accent font-medium' : ''}"
            >
              {mod.name}
            </button>
          {/each}
        {/if}
      </div>
    </div>
  </div>
{/if}
