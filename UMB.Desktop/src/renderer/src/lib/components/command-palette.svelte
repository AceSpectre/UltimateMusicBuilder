<script lang="ts">
  import { Search } from '@lucide/svelte'
  import { _ } from 'svelte-i18n'
  import { actions, shouldRunCliAction } from '$lib/actions'
  import type { ModInfo } from '$lib/types/electron'

  let {
    open = $bindable(false),
    activeTab,
    mods,
    activeMod,
    onSelectAction,
    onSelectMod
  }: {
    open: boolean
    activeTab: string
    mods: ModInfo[]
    activeMod: ModInfo | null
    onSelectAction: (id: string) => void
    onSelectMod: (mod: ModInfo) => void
  } = $props()
  let query = $state('')
  let inputEl: HTMLInputElement | undefined = $state()

  const filtered = $derived(() => {
    if (!query.trim()) return actions
    const q = query.toLowerCase()
    return actions.filter(a => $_('nav.items.' + a.id).toLowerCase().includes(q))
  })

  function selectAction(id: string) {
    onSelectAction(id)
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
          placeholder={$_('commandPalette.searchPlaceholder')}
          class="flex-1 h-12 bg-transparent text-sm outline-none placeholder:text-muted-foreground"
        />
      </div>

      <!-- Results -->
      <div class="max-h-[300px] overflow-y-auto p-2">
        {#if filtered().length === 0}
          <div class="px-3 py-6 text-center text-sm text-muted-foreground">{$_('commandPalette.noResults')}</div>
        {:else}
          <div class="px-2 py-1.5 text-[10.5px] font-semibold tracking-[.12em] uppercase text-muted-foreground">
            {$_('commandPalette.actions')}
          </div>
          {#each filtered() as action}
            <button
              onclick={() => selectAction(action.id)}
              class="w-full flex items-center gap-3 px-3 py-2 rounded-lg text-sm text-left hover:bg-accent transition-colors
                {activeTab === action.id ? 'bg-accent font-medium' : ''}"
            >
              {$_('nav.items.' + action.id)}
            </button>
          {/each}
        {/if}

        {#if mods.length > 0 && !query.trim()}
          <div class="mt-2 px-2 py-1.5 text-[10.5px] font-semibold tracking-[.12em] uppercase text-muted-foreground">
            {$_('commandPalette.mods')}
          </div>
          {#each mods as mod}
            <button
              onclick={() => { onSelectMod(mod); open = false }}
              class="w-full flex items-center gap-3 px-3 py-2 rounded-lg text-sm text-left hover:bg-accent transition-colors
                {activeMod?.name === mod.name ? 'bg-accent font-medium' : ''}"
            >
              {mod.name}
            </button>
          {/each}
        {/if}
      </div>
    </div>
  </div>
{/if}
