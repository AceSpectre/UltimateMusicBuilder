<script lang="ts">
  import type { Snippet } from 'svelte'

  let {
    onclick,
    title,
    disabled = false,
    children
  }: { onclick: () => void; title: string; disabled?: boolean; children: Snippet } = $props()
</script>

<button {onclick} {title} {disabled} class="btn w-[27px] shrink-0 px-0 text-muted-foreground">
  {@render children()}
</button>
