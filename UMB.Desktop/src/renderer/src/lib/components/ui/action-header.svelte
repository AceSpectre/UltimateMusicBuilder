<script lang="ts">
  import type { Snippet } from 'svelte'

  // Title + subtitle bar at the top of every action view; children render as right-aligned actions.
  let { title, subtitle = '', children }: { title: string; subtitle?: string; children?: Snippet } = $props()
</script>

<header class="surface-grain flex shrink-0 items-center gap-3 border-b border-border bg-card px-[22px] pb-3.5 pt-4">
  <div class="min-w-0 flex-1">
    <h1 class="truncate text-[20px] font-semibold leading-tight tracking-[-.025em]">{title}</h1>
    {#if subtitle}
      <p class="mt-[5px] truncate text-[12.5px] text-muted-foreground">{subtitle}</p>
    {/if}
  </div>
  {#if children}
    <div class="flex shrink-0 items-center gap-2">{@render children()}</div>
  {/if}
</header>
