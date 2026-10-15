<script lang="ts">
  import Save from '@lucide/svelte/icons/save'
  import { _ } from 'svelte-i18n'

  let {
    saveState,
    disabled,
    onclick,
    save = ''
  }: {
    saveState: 'idle' | 'saving' | 'saved'
    disabled: boolean
    onclick: () => void
    save?: string
  } = $props()
</script>

<button
  {onclick}
  {disabled}
  class="{!disabled && saveState === 'idle' ? 'btn-primary' : 'btn'} min-w-24 shrink-0"
>
  <Save size={13} />
  {saveState === 'saving' ? $_('common.saving') : saveState === 'saved' ? $_('common.saved') : save || $_('common.save')}
</button>
