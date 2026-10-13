<script lang="ts">
  import type { Snippet } from 'svelte'

  // Centered icon + title + body placeholder for empty/loading panes.
  let {
    icon,
    title = '',
    body = '',
    dashed = false,
    children
  }: { icon: Snippet; title?: string; body?: string; dashed?: boolean; children?: Snippet } = $props()
</script>

<div class="grid h-full min-h-[320px] place-items-center {dashed ? 'rounded-xl border border-dashed border-border' : ''}">
  <div class="flex max-w-[340px] flex-col items-center gap-3 px-6 text-center">
    <div class="text-dim">{@render icon()}</div>
    <div>
      {#if title}
        <h3 class="text-[13.5px] font-semibold tracking-[-.01em]">{title}</h3>
      {/if}
      {#if body}
        <p class="pt-1 text-[12.5px] text-muted-foreground">{body}</p>
      {/if}
    </div>
    {#if children}
      {@render children()}
    {/if}
  </div>
</div>
