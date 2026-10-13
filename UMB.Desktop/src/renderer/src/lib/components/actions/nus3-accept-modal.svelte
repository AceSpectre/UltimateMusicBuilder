<script lang="ts">
  import Trash2 from '@lucide/svelte/icons/trash-2'
  import X from '@lucide/svelte/icons/x'
  import { _ } from 'svelte-i18n'

  // Accept & save: asks whether to delete the source audio files.
  let { onClose, onAccept }: { onClose: () => void; onAccept: (deleteSources: boolean) => void } = $props()
</script>

<!-- svelte-ignore a11y_no_static_element_interactions -->
<div
  class="fixed inset-0 z-50 flex items-center justify-center bg-black/50"
  onkeydown={(e) => { if (e.key === 'Escape') onClose() }}
  onclick={(e) => { if (e.target === e.currentTarget) onClose() }}
>
  <div class="w-[460px] rounded-xl border border-border bg-card shadow-overlay">
    <div class="flex items-center justify-between border-b border-border px-5 py-3">
      <h2 class="text-[13.5px] font-semibold">{$_('nus3Convert.acceptModalTitle')}</h2>
      <button
        onclick={onClose}
        class="inline-flex h-[26px] w-[26px] items-center justify-center rounded text-dim transition-colors duration-100 hover:bg-accent hover:text-foreground"
      >
        <X size={14} />
      </button>
    </div>
    <div class="px-5 py-4">
      <p class="text-[13px] text-muted-foreground">{$_('nus3Convert.acceptModalBody')}</p>
    </div>
    <div class="flex justify-end gap-2 border-t border-border px-5 py-3">
      <button
        onclick={onClose}
        class="btn"
      >
        {$_('nus3Convert.acceptModalCancel')}
      </button>
      <button
        onclick={() => onAccept(false)}
        class="btn"
      >
        {$_('nus3Convert.acceptModalKeep')}
      </button>
      <button
        onclick={() => onAccept(true)}
        class="btn-danger"
      >
        <Trash2 size={13} />
        {$_('nus3Convert.acceptModalDelete')}
      </button>
    </div>
  </div>
</div>
