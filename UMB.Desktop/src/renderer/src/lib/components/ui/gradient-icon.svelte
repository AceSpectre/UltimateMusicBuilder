<script lang="ts">
  import type { Snippet } from 'svelte'

  // Gradient-tinted icon tile used across headers, cards and empty states.
  // sm/md/lg use the header tint; card is the slightly softer action-card tint.
  let { size = 'md', children }: { size?: 'sm' | 'md' | 'card' | 'lg'; children: Snippet } = $props()

  const box = $derived(
    {
      sm: 'h-8 w-8 rounded-lg',
      md: 'h-9 w-9 rounded-xl',
      card: 'h-10 w-10 rounded-xl',
      lg: 'h-12 w-12 rounded-2xl'
    }[size]
  )
  const tint = $derived(
    size === 'card'
      ? 'background: linear-gradient(135deg, hsl(var(--gradient-from) / .10), hsl(var(--gradient-to) / .14)); color: hsl(var(--gradient-from));'
      : 'background: linear-gradient(135deg, hsl(var(--gradient-from) / .13), hsl(var(--gradient-to) / .16)); color: hsl(var(--gradient-from));'
  )
</script>

<div class="flex shrink-0 items-center justify-center border border-border {box}" style={tint}>
  {@render children()}
</div>
