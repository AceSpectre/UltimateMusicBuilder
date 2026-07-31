<script lang="ts">
  import type { Snippet } from 'svelte'

  let {
    onclick,
    title,
    disabled = false,
    children
  }: { onclick: () => void; title: string; disabled?: boolean; children: Snippet } = $props()
</script>

<button
  {onclick}
  {title}
  {disabled}
  class="inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-lg border border-input bg-background transition-colors hover:bg-muted disabled:cursor-not-allowed disabled:opacity-60"
>
  {@render children()}
</button>
