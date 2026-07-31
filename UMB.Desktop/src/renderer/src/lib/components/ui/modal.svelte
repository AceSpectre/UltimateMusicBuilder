<script lang="ts">
  import type { Snippet } from 'svelte'

  // Shared modal shell: dark backdrop + gradient-strip card. Dismissal is left to the
  // caller's footer buttons (matches the original inline modals, which had no
  // backdrop-click close). maxHeight switches to the scrollable flex-col layout.
  let {
    maxWidth = '400px',
    maxHeight = '',
    children
  }: { maxWidth?: string; maxHeight?: string; children: Snippet } = $props()
</script>

<div class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-6">
  {#if maxHeight}
    <div
      class="flex w-full flex-col overflow-hidden rounded-2xl border border-border bg-card shadow-xl"
      style="max-width: {maxWidth}; max-height: {maxHeight};"
    >
      <div class="gradient-strip h-[3px] shrink-0"></div>
      {@render children()}
    </div>
  {:else}
    <div class="w-full rounded-2xl border border-border bg-card shadow-xl overflow-hidden" style="max-width: {maxWidth};">
      <div class="gradient-strip h-[3px]"></div>
      {@render children()}
    </div>
  {/if}
</div>
