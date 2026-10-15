<script lang="ts">
  import type { Snippet } from 'svelte'

  // Shared modal shell; footer holds the right-aligned buttons, which are the only way to dismiss it.
  // maxHeight switches to the scrollable flex-col layout.
  let {
    maxWidth = '400px',
    maxHeight = '',
    children,
    footer
  }: { maxWidth?: string; maxHeight?: string; children: Snippet; footer?: Snippet } = $props()
</script>

<div class="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-6">
  <div
    class="shadow-overlay w-full overflow-hidden rounded-xl border border-border bg-card {maxHeight ? 'flex flex-col' : ''}"
    style="max-width: {maxWidth};{maxHeight ? ` max-height: ${maxHeight};` : ''}"
  >
    {@render children()}
    {#if footer}
      <div class="flex shrink-0 items-center justify-end gap-2 border-t border-border px-5 py-4">{@render footer()}</div>
    {/if}
  </div>
</div>
