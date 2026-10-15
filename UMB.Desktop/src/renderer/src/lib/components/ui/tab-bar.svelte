<script lang="ts" generics="T extends string">
  // Underlined tab strip that sits under a view's ActionHeader.
  let { tabs, active = $bindable() }: { tabs: Array<{ id: T; label: string }>; active: T } = $props()
</script>

<div class="flex shrink-0 items-center gap-1 border-b border-line2 px-[22px]" role="tablist">
  {#each tabs as tab (tab.id)}
    <button
      role="tab"
      aria-selected={active === tab.id}
      onclick={() => (active = tab.id)}
      class="relative px-3 py-2.5 text-[13px] font-medium transition-colors duration-100 {active === tab.id ? 'text-foreground' : 'text-muted-foreground hover:text-foreground'}"
    >
      {tab.label}
      {#if active === tab.id}
        <span class="absolute inset-x-2 -bottom-px h-0.5 bg-primary"></span>
      {/if}
    </button>
  {/each}
</div>
