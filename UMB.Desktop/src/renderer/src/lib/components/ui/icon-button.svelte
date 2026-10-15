<script lang="ts">
  import type { Snippet } from 'svelte'

  let {
    onclick,
    title,
    disabled = false,
    children
  }: { onclick: () => void; title: string; disabled?: boolean; children: Snippet } = $props()
</script>

<button {onclick} {title} {disabled} class="btn-icon text-muted-foreground">
  {@render children()}
</button>
