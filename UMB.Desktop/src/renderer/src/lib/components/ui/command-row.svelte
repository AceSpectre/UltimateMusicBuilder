<script lang="ts">
  import type { Snippet } from 'svelte'

  type Tone = 'dim' | 'warn' | 'err'

  // One sub-action in a command list: name, description and notes (empty ones are skipped), then a fixed-width button.
  let {
    name,
    description,
    notes = [],
    children
  }: { name: string; description: string; notes?: { text: string; tone?: Tone }[]; children: Snippet } = $props()

  const toneClass = { dim: 'text-dim', warn: 'text-warn', err: 'text-destructive' }
</script>

<div class="mb-[7px] grid grid-cols-[1fr_96px] items-center gap-4 rounded-xl border border-line2 px-[13px] py-[11px] transition-colors duration-100 hover:border-border">
  <div class="min-w-0">
    <div class="text-[13.5px] font-semibold tracking-[-.01em]">{name}</div>
    <div class="mt-[3px] break-words text-[12.5px] text-muted-foreground">{description}</div>
    {#each notes.filter((n) => n.text) as note, i (i)}
      <div class="mt-[3px] text-[12px] {toneClass[note.tone ?? 'dim']}">{note.text}</div>
    {/each}
  </div>
  <div class="[&>*]:w-24">{@render children()}</div>
</div>
