<script lang="ts">
  import Search from '@lucide/svelte/icons/search'

  let { value = $bindable(''), placeholder }: { value?: string; placeholder: string } = $props()
</script>

<div class="flex h-7 items-center gap-2 rounded border border-input bg-card px-2 focus-within:border-primary focus-within:ring-2 focus-within:ring-primary/25">
  <Search size={13} class="shrink-0 text-dim" />
  <input type="text" bind:value {placeholder} class="w-full bg-transparent text-[12.5px] focus:outline-none" />
</div>
