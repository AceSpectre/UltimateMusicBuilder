<script lang="ts">
  import Save from '@lucide/svelte/icons/save'

  let {
    saveState,
    disabled,
    onclick,
    save,
    saving,
    saved
  }: {
    saveState: 'idle' | 'saving' | 'saved'
    disabled: boolean
    onclick: () => void
    save: string
    saving: string
    saved: string
  } = $props()
</script>

<button
  {onclick}
  {disabled}
  class="{!disabled && saveState === 'idle' ? 'btn-primary' : 'btn'} min-w-24 shrink-0"
>
  <Save size={13} />
  {saveState === 'saving' ? saving : saveState === 'saved' ? saved : save}
</button>
