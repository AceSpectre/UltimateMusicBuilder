<script lang="ts">
  import { inputClass } from '$lib/ui-classes'
  import type { SeriesGame } from '$lib/types/electron'

  // Series game picker; a value outside `games` stays selectable so it is never silently changed.
  let {
    value = $bindable(),
    games,
    label,
    emptyLabel
  }: { value: string; games: SeriesGame[]; label: string; emptyLabel?: string } = $props()

  const showValue = $derived(!games.some((g) => g.id === value) && !(emptyLabel !== undefined && value === ''))
</script>

<select class={inputClass} bind:value aria-label={label}>
  {#if emptyLabel !== undefined}
    <option value="">{emptyLabel}</option>
  {/if}
  {#each games as game}
    <option value={game.id}>{game.name}</option>
  {/each}
  {#if showValue}
    <option {value}>{value}</option>
  {/if}
</select>
