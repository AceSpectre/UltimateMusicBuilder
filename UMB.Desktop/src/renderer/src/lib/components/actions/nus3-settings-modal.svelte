<script lang="ts">
  import { X } from '@lucide/svelte'
  import { _ } from 'svelte-i18n'

  interface Nus3Settings {
    minScoreThreshold: number
    previewLength: number
    minLoopDuration: number
    disablePruning: boolean
  }

  // Edits a local copy of the settings; the parent only sees them on Save.
  let {
    initial,
    onApply,
    onClose
  }: { initial: Nus3Settings; onApply: (settings: Nus3Settings) => void; onClose: () => void } = $props()

  let minScore = $state(initial.minScoreThreshold)
  let previewLen = $state(initial.previewLength)
  let minLoopDuration = $state(initial.minLoopDuration)
  let disablePruning = $state(initial.disablePruning)
</script>

<!-- svelte-ignore a11y_no_static_element_interactions -->
<div
  class="fixed inset-0 z-50 flex items-center justify-center bg-black/50"
  onkeydown={(e) => { if (e.key === 'Escape') onClose() }}
  onclick={(e) => { if (e.target === e.currentTarget) onClose() }}
>
  <div class="w-[400px] rounded-xl border border-border bg-card shadow-2xl">
    <div class="gradient-strip h-[3px] rounded-t-xl"></div>
    <div class="flex items-center justify-between border-b border-border px-5 py-3">
      <h2 class="text-sm font-semibold">{$_('nus3Convert.settingsTitle')}</h2>
      <button
        onclick={onClose}
        class="inline-flex h-7 w-7 items-center justify-center rounded-lg hover:bg-accent transition-colors"
      >
        <X size={14} />
      </button>
    </div>
    <div class="flex flex-col gap-4 px-5 py-4">
      <div class="flex flex-col gap-1.5">
        <label for="nus3-min-score" class="text-[13px] font-medium">
          {$_('nus3Convert.settingsMinScore')}
        </label>
        <input
          id="nus3-min-score"
          type="number"
          step="0.5"
          min="0"
          max="100"
          bind:value={minScore}
          class="h-9 rounded-lg border border-input bg-background px-3 text-[13px] focus:outline-none focus:ring-2 focus:ring-ring"
        />
        <p class="text-[11px] text-muted-foreground">{$_('nus3Convert.settingsMinScoreHint')}</p>
      </div>
      <div class="flex flex-col gap-1.5">
        <label for="nus3-preview-len" class="text-[13px] font-medium">
          {$_('nus3Convert.settingsPreviewLength')}
        </label>
        <input
          id="nus3-preview-len"
          type="number"
          step="1"
          min="1"
          max="30"
          bind:value={previewLen}
          class="h-9 rounded-lg border border-input bg-background px-3 text-[13px] focus:outline-none focus:ring-2 focus:ring-ring"
        />
        <p class="text-[11px] text-muted-foreground">{$_('nus3Convert.settingsPreviewLengthHint')}</p>
      </div>
      <div class="flex flex-col gap-1.5">
        <label for="nus3-min-loop" class="text-[13px] font-medium">
          {$_('nus3Convert.settingsMinLoopDuration')}
        </label>
        <input
          id="nus3-min-loop"
          type="number"
          step="0.5"
          min="0"
          bind:value={minLoopDuration}
          class="h-9 rounded-lg border border-input bg-background px-3 text-[13px] focus:outline-none focus:ring-2 focus:ring-ring"
        />
        <p class="text-[11px] text-muted-foreground">{$_('nus3Convert.settingsMinLoopDurationHint')}</p>
      </div>
      <label class="flex items-start gap-2.5">
        <input
          type="checkbox"
          bind:checked={disablePruning}
          class="mt-0.5 h-4 w-4 shrink-0 rounded border-input"
        />
        <span class="flex flex-col gap-0.5">
          <span class="text-[13px] font-medium">{$_('nus3Convert.settingsDisablePruning')}</span>
          <span class="text-[11px] text-muted-foreground">{$_('nus3Convert.settingsDisablePruningHint')}</span>
        </span>
      </label>
    </div>
    <div class="flex justify-end gap-2 border-t border-border px-5 py-3">
      <button
        onclick={onClose}
        class="inline-flex h-8 items-center rounded-md border border-border bg-background px-3 text-[12.5px] font-medium transition-colors hover:bg-muted"
      >
        {$_('nus3Convert.settingsClose')}
      </button>
      <button
        onclick={() => onApply({ minScoreThreshold: minScore, previewLength: previewLen, minLoopDuration, disablePruning })}
        class="inline-flex h-8 items-center rounded-md border-0 px-3 text-[12.5px] font-medium text-white transition-colors"
        style="background: linear-gradient(135deg, hsl(var(--gradient-from)), hsl(var(--gradient-to))); box-shadow: 0 4px 14px -2px hsl(var(--gradient-from) / .35);"
      >
        {$_('nus3Convert.settingsSave')}
      </button>
    </div>
  </div>
</div>
