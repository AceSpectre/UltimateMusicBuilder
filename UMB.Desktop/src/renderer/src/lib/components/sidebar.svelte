<script lang="ts">
  import { navGroups } from '$lib/actions'
  let {
    activeTab,
    onSelectAction
  }: {
    activeTab: string
    onSelectAction: (id: string) => void
  } = $props()

  let collapsed = $state(localStorage.getItem('umb-sidebar-collapsed') === 'true')

  function handleClick(id: string) {
    onSelectAction(id)
  }
</script>

<aside
  class="shrink-0 flex flex-col bg-card border-r border-border transition-[width] duration-200 overflow-hidden"
  style="width: {collapsed ? '60px' : '232px'}"
>
  <nav class="flex-1 overflow-y-auto py-2">
    {#each navGroups as group}
      <div class="py-1">
        {#if !collapsed}
          <div class="px-3.5 pt-2.5 pb-1 text-[10.5px] font-semibold tracking-[.12em] uppercase text-muted-foreground">
            {group.label}
          </div>
        {/if}

        {#each group.items as item}
          {@const isActive = activeTab === item.id}
          <button
            onclick={() => handleClick(item.id)}
            class="relative flex items-center gap-2.5 mx-2 px-2.5 py-[7px] rounded-[7px] w-[calc(100%-16px)] text-left transition-colors duration-100
              {isActive
                ? 'font-semibold'
                : 'font-medium text-foreground/80 hover:bg-accent hover:text-foreground'}"
            style={isActive
              ? 'background: linear-gradient(135deg, hsl(var(--gradient-from) / .14), hsl(var(--gradient-to) / .14));'
              : ''}
            title={collapsed ? item.label : undefined}
          >
            {#if isActive}
              <div
                class="absolute left-[-1px] top-1.5 bottom-1.5 w-[3px] rounded-r-[3px] gradient-bg"
              ></div>
            {/if}
            <item.icon size={16} class="shrink-0 {isActive ? 'opacity-100' : 'opacity-85'}" />

            {#if !collapsed}
              <span class="text-[13.5px] truncate">{item.label}</span>
            {/if}
          </button>
        {/each}
      </div>
    {/each}
  </nav>
</aside>
