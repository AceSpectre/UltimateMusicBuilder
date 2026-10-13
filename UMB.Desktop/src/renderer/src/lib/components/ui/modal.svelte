<script lang="ts">
  import type { Snippet } from 'svelte'

  // Shared modal shell. Dismissal is left to the caller's footer buttons.
  // maxHeight switches to the scrollable flex-col layout.
  let {
    maxWidth = '400px',
    maxHeight = '',
    children
  }: { maxWidth?: string; maxHeight?: string; children: Snippet } = $props()
</script>

<div class="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-6">
  <div
    class="shadow-overlay w-full overflow-hidden rounded-xl border border-border bg-card {maxHeight ? 'flex flex-col' : ''}"
    style="max-width: {maxWidth};{maxHeight ? ` max-height: ${maxHeight};` : ''}"
  >
    {@render children()}
  </div>
</div>
