<script lang="ts">
  import type { Snippet } from 'svelte'

  // One sub-action in a command list: name, description and an optional note, then a fixed-width button.
  let {
    name,
    description,
    note = '',
    tone = 'dim',
    children
  }: { name: string; description: string; note?: string; tone?: 'dim' | 'warn' | 'err'; children: Snippet } = $props()

  const toneClass = { dim: 'text-dim', warn: 'text-warn', err: 'text-destructive' }
</script>

<div class="mb-[7px] grid grid-cols-[1fr_96px] items-center gap-4 rounded-xl border border-line2 px-[13px] py-[11px] transition-colors duration-100 hover:border-border">
  <div class="min-w-0">
    <div class="text-[13.5px] font-semibold tracking-[-.01em]">{name}</div>
    <div class="mt-[3px] break-words text-[12.5px] text-muted-foreground">{description}</div>
    {#if note}
      <div class="mt-[3px] text-[12px] {toneClass[tone]}">{note}</div>
    {/if}
  </div>
  <div class="[&>*]:w-24">{@render children()}</div>
</div>
