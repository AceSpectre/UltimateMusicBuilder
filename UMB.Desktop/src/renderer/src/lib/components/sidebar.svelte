<script lang="ts">
  import { _ } from 'svelte-i18n'
  import { navGroups } from '$lib/actions'
  let {
    activeTab,
    onSelectAction
  }: {
    activeTab: string
    onSelectAction: (id: string) => void
  } = $props()
</script>

<aside class="surface-grain flex w-[176px] shrink-0 flex-col border-r border-border bg-rail">
  <nav class="flex-1 overflow-y-auto py-1.5">
    {#each navGroups as group}
      <div class="px-3 pb-1 pt-3 kicker">
        {$_('nav.groups.' + group.id)}
      </div>

      {#each group.items as id}
        {@const isActive = activeTab === id}
        <button
          onclick={() => onSelectAction(id)}
          class="flex h-7 w-full items-center gap-[9px] border-l-2 px-3 text-left text-[13px] transition-colors duration-100
            {isActive ? 'border-primary bg-accbg font-medium text-accink' : 'border-transparent text-muted-foreground hover:bg-accent hover:text-foreground'}"
        >
          <i class="w-2 not-italic {isActive ? 'text-primary' : 'text-dim'}" aria-hidden="true">{isActive ? '▸' : '·'}</i>
          <span class="truncate">{$_('nav.items.' + id)}</span>
        </button>
      {/each}
    {/each}
  </nav>
</aside>
