<script lang="ts">
  import { Save } from '@lucide/svelte'

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
  class="shrink-0 inline-flex items-center gap-2 rounded-lg border border-input bg-background px-3 py-2 text-[12.5px] font-medium transition-colors hover:bg-muted disabled:cursor-not-allowed disabled:opacity-60"
>
  <Save size={14} />
  {saveState === 'saving' ? saving : saveState === 'saved' ? saved : save}
</button>
