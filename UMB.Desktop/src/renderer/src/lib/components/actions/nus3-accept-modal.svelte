<script lang="ts">
  import { Trash2, X } from '@lucide/svelte'
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
  <div class="w-[460px] rounded-xl border border-border bg-card shadow-2xl">
    <div class="gradient-strip h-[3px] rounded-t-xl"></div>
    <div class="flex items-center justify-between border-b border-border px-5 py-3">
      <h2 class="text-sm font-semibold">{$_('nus3Convert.acceptModalTitle')}</h2>
      <button
        onclick={onClose}
        class="inline-flex h-7 w-7 items-center justify-center rounded-lg hover:bg-accent transition-colors"
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
        class="inline-flex h-8 items-center rounded-md border border-border bg-background px-3 text-[12.5px] font-medium transition-colors hover:bg-muted"
      >
        {$_('nus3Convert.acceptModalCancel')}
      </button>
      <button
        onclick={() => onAccept(false)}
        class="inline-flex h-8 items-center rounded-md border border-border bg-background px-3 text-[12.5px] font-medium transition-colors hover:bg-muted"
      >
        {$_('nus3Convert.acceptModalKeep')}
      </button>
      <button
        onclick={() => onAccept(true)}
        class="inline-flex h-8 items-center gap-1.5 rounded-md border-0 px-3 text-[12.5px] font-medium text-destructive-foreground transition-colors"
        style="background: hsl(var(--destructive));"
      >
        <Trash2 size={13} />
        {$_('nus3Convert.acceptModalDelete')}
      </button>
    </div>
  </div>
</div>
