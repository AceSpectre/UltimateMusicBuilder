<script lang="ts">
  import type { Snippet } from 'svelte'
  import GradientIcon from './gradient-icon.svelte'

  // Centered icon + title + body placeholder for empty/loading panes.
  let {
    icon,
    title = '',
    body = '',
    dashed = false,
    children
  }: { icon: Snippet; title?: string; body?: string; dashed?: boolean; children?: Snippet } = $props()
</script>

<div class="grid h-full min-h-[320px] place-items-center {dashed ? 'rounded-2xl border border-dashed border-border bg-background/60' : ''}">
  <div class="flex max-w-[340px] flex-col items-center gap-3 px-6 text-center">
    <GradientIcon size="lg">{@render icon()}</GradientIcon>
    <div>
      {#if title}
        <h3 class="text-sm font-semibold">{title}</h3>
      {/if}
      {#if body}
        <p class="pt-1 text-[13px] text-muted-foreground">{body}</p>
      {/if}
    </div>
    {#if children}
      {@render children()}
    {/if}
  </div>
</div>
