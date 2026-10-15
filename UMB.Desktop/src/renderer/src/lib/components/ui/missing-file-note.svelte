<script lang="ts">
  import { _ } from 'svelte-i18n'

  let { missing, class: className = '' }: { missing: boolean; class?: string } = $props()
</script>

{#if missing}<span class="text-[11px] text-warn {className}">{$_('common.fileNotFound')}</span>{/if}
