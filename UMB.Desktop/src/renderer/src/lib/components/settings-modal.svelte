<script lang="ts">
  import { Settings, Volume2 } from '@lucide/svelte'
  import { _ } from 'svelte-i18n'

  let { open, onClose }: { open: boolean; onClose: () => void } = $props()

  let globalVolumeMultiplier = $state(1.5)
  let saveState = $state<'idle' | 'saving' | 'saved'>('idle')
  let loaded = $state(false)

  $effect(() => {
    if (open && !loaded) {
      void window.electron.umb.getAppSettings().then((s) => {
        globalVolumeMultiplier = s.globalVolumeMultiplier
        loaded = true
      })
    }
    if (!open) {
      loaded = false
      saveState = 'idle'
    }
  })

  function clamp(v: number): number {
    if (Number.isNaN(v)) return 1
    return Math.max(0, Math.min(10, Math.round(v * 100) / 100))
  }

  async function handleSave() {
    saveState = 'saving'
    try {
      await window.electron.umb.saveAppSettings({
        globalVolumeMultiplier: clamp(globalVolumeMultiplier)
      })
      saveState = 'saved'
      setTimeout(() => onClose(), 600)
    } catch {
      saveState = 'idle'
    }
  }

  function handleBackdrop() {
    if (saveState !== 'saving') onClose()
  }
</script>

{#if open}
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div
    class="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4"
    onclick={handleBackdrop}
    onkeydown={(e) => { if (e.key === 'Escape' && saveState !== 'saving') onClose() }}
  >
    <!-- svelte-ignore a11y_no_static_element_interactions -->
    <div
      class="w-full max-w-[420px] rounded-xl border border-border bg-popover shadow-2xl overflow-hidden"
      onclick={(e) => e.stopPropagation()}
      onkeydown={(e) => e.stopPropagation()}
    >
      <div class="gradient-strip h-[3px] shrink-0"></div>
      <div class="border-b border-border px-5 py-4 flex items-center gap-3">
        <div
          class="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl border border-border"
          style="background: linear-gradient(135deg, hsl(var(--gradient-from) / .13), hsl(var(--gradient-to) / .16)); color: hsl(var(--gradient-from));"
        >
          <Settings size={18} />
        </div>
        <div class="min-w-0">
          <h2 class="truncate text-sm font-semibold">{$_('settings.title')}</h2>
          <p class="truncate text-[12.5px] text-muted-foreground">{$_('settings.subtitle')}</p>
        </div>
      </div>

      <div class="px-5 py-5 flex flex-col gap-4">
        <div class="flex flex-col gap-1.5">
          <label for="global-volume" class="flex items-center gap-1.5 text-[12.5px] font-medium">
            <Volume2 size={13} class="text-muted-foreground" />
            {$_('settings.globalVolume')}
          </label>
          <input
            id="global-volume"
            type="number"
            min="0"
            max="10"
            step="0.1"
            bind:value={globalVolumeMultiplier}
            oninput={() => { if (saveState === 'saved') saveState = 'idle' }}
            class="rounded-lg border border-input bg-background px-3 py-2 text-[12.5px] font-mono outline-none focus:border-ring"
          />
          <p class="text-[11.5px] text-muted-foreground">{$_('settings.globalVolumeHint')}</p>
        </div>
      </div>

      <div class="border-t border-border px-5 py-4 flex items-center justify-end gap-2">
        <button
          onclick={onClose}
          disabled={saveState === 'saving'}
          class="inline-flex items-center gap-2 rounded-lg border border-input bg-background px-4 py-2 text-[12.5px] font-medium transition-colors hover:bg-muted disabled:cursor-not-allowed disabled:opacity-60"
        >
          {$_('settings.cancel')}
        </button>
        <button
          onclick={handleSave}
          disabled={saveState === 'saving'}
          class="inline-flex items-center gap-2 rounded-lg border-0 px-4 py-2 text-[12.5px] font-medium text-white transition-colors disabled:opacity-60"
          style="background: linear-gradient(135deg, hsl(var(--gradient-from)), hsl(var(--gradient-to))); box-shadow: 0 4px 14px -2px hsl(var(--gradient-from) / .35);"
        >
          {saveState === 'saving' ? $_('settings.saving') : saveState === 'saved' ? $_('settings.saved') : $_('settings.save')}
        </button>
      </div>
    </div>
  </div>
{/if}
