<script lang="ts">
  import { GitMerge, RefreshCw, Check, AlertTriangle } from '@lucide/svelte'
  import { _ } from 'svelte-i18n'
  import { logStore } from '$lib/stores/logs.svelte'
  import type { ModInfo, MergeAnalysis, MergeResult } from '$lib/types/electron'

  let { mods }: { mods: ModInfo[] } = $props()

  let selected = $state<Set<string>>(new Set())
  let priorityPath = $state<string | null>(null)
  let outputName = $state('')
  let nameError = $state<string | null>(null)
  let analysis = $state<MergeAnalysis | null>(null)
  let result = $state<MergeResult | null>(null)
  let analyzing = $state(false)
  let merging = $state(false)
  let nameValidationTimer = $state<ReturnType<typeof setTimeout> | null>(null)

  const selectedMods = $derived(mods.filter(m => selected.has(m.path)))
  const hasConflicts = $derived((analysis?.conflicts.length ?? 0) > 0)
  const canAnalyze = $derived(selectedMods.length >= 2)
  const canMerge = $derived(
    analysis &&
    !nameError &&
    outputName.trim().length > 0 &&
    (!hasConflicts || priorityPath)
  )

  function log(level: 'info' | 'warn' | 'error', message: string) {
    logStore.push({ timestamp: new Date().toLocaleTimeString('en-GB', { hour12: false }), level, message })
  }

  function toggleMod(modPath: string) {
    const next = new Set(selected)
    if (next.has(modPath)) {
      next.delete(modPath)
    } else {
      next.add(modPath)
    }
    selected = next
    analysis = null
    result = null
    if (priorityPath && !next.has(priorityPath)) {
      priorityPath = null
    }
  }

  function handleOutputNameInput(e: Event) {
    const target = e.currentTarget as HTMLInputElement
    outputName = target.value
    result = null

    if (nameValidationTimer) clearTimeout(nameValidationTimer)
    nameValidationTimer = setTimeout(async () => {
      if (!outputName.trim()) {
        nameError = null
        return
      }
      try {
        nameError = await window.electron.umb.validateMergeName(outputName)
      } catch {
        nameError = 'Validation failed.'
      }
    }, 300)
  }

  function handlePriorityChange(e: Event) {
    const target = e.currentTarget as HTMLSelectElement
    priorityPath = target.value || null
  }

  async function handleAnalyze() {
    if (!canAnalyze) return
    analyzing = true
    result = null
    try {
      analysis = await window.electron.umb.analyzeMerge(selectedMods.map(m => m.path))
      if (analysis.conflicts.length > 0 && !priorityPath) {
        priorityPath = selectedMods[0]?.path ?? null
      }
    } catch (err) {
      log('error', `Analysis failed: ${err instanceof Error ? err.message : String(err)}`)
      analysis = null
    } finally {
      analyzing = false
    }
  }

  async function handleMerge() {
    if (!canMerge || !analysis) return
    merging = true
    try {
      result = await window.electron.umb.executeMerge(
        selectedMods.map(m => m.path),
        outputName.trim(),
        priorityPath
      )
    } catch (err) {
      log('error', `Merge failed: ${err instanceof Error ? err.message : String(err)}`)
    } finally {
      merging = false
    }
  }

  function handleReset() {
    selected = new Set()
    priorityPath = null
    outputName = ''
    nameError = null
    analysis = null
    result = null
  }
</script>

<div class="flex-1 overflow-hidden">
  <div class="flex h-full min-h-0">
    <section class="flex h-full min-h-0 w-full flex-col border border-border bg-card overflow-hidden">
      <div class="gradient-strip h-[3px] shrink-0"></div>

      <div class="shrink-0 border-b border-border px-5 py-3 flex items-center gap-3">
        <div
          class="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl border border-border"
          style="background: linear-gradient(135deg, hsl(var(--gradient-from) / .13), hsl(var(--gradient-to) / .16)); color: hsl(var(--gradient-from));"
        >
          <GitMerge size={18} />
        </div>
        <div class="min-w-0">
          <h2 class="truncate text-sm font-semibold">{$_('merge.title')}</h2>
          <p class="truncate text-[12.5px] text-muted-foreground">{$_('merge.subtitle')}</p>
        </div>
      </div>

      <div class="min-h-0 flex-1 overflow-auto px-5 py-5">
        {#if mods.length === 0}
          <div class="rounded-xl border border-dashed border-border bg-background/70 px-3 py-8 text-center text-[13px] text-muted-foreground">
            {$_('merge.noMods')}
          </div>
        {:else}
          <div class="flex flex-col gap-5">
            <div class="flex flex-col gap-2">
              <label class="text-[12.5px] font-medium text-muted-foreground">{$_('merge.selectMods')}</label>
              <div class="max-h-[220px] overflow-auto rounded-lg border border-border bg-background">
                {#each mods as mod}
                  <button
                    onclick={() => toggleMod(mod.path)}
                    class="flex w-full items-center gap-3 border-b border-border px-3 py-2 text-left text-[13px] transition-colors hover:bg-muted last:border-b-0"
                  >
                    <div
                      class="flex h-4 w-4 shrink-0 items-center justify-center rounded border {selected.has(mod.path) ? 'border-primary bg-primary text-primary-foreground' : 'border-input bg-background'}"
                    >
                      {#if selected.has(mod.path)}
                        <Check size={12} />
                      {/if}
                    </div>
                    <span class="truncate">{mod.name}</span>
                  </button>
                {/each}
              </div>
              {#if selectedMods.length > 0 && selectedMods.length < 2}
                <p class="text-[12px] text-muted-foreground">{$_('merge.selectAtLeast')}</p>
              {/if}
            </div>

            {#if canAnalyze && !analysis && !result}
              <button
                onclick={handleAnalyze}
                disabled={analyzing}
                class="self-start inline-flex items-center gap-2 rounded-lg px-4 py-2.5 text-[13px] font-medium text-white disabled:opacity-60"
                style="background: linear-gradient(135deg, hsl(var(--gradient-from)), hsl(var(--gradient-to))); box-shadow: 0 4px 14px -2px hsl(var(--gradient-from) / .35);"
              >
                {#if analyzing}
                  <RefreshCw size={14} class="animate-spin" />
                  {$_('merge.analyzing')}
                {:else}
                  <GitMerge size={14} />
                  {$_('merge.analyze')}
                {/if}
              </button>
            {/if}

            {#if analysis && !result}
              <div class="rounded-xl border border-border bg-background p-4">
                <div class="grid grid-cols-2 gap-3 text-[13px]">
                  <div class="flex items-center gap-2">
                    <Check size={14} class="text-green-500" />
                    <span>{$_('merge.totalSeries', { values: { count: analysis.totalSeries } })}</span>
                  </div>
                  <div class="flex items-center gap-2">
                    {#if analysis.conflicts.length > 0}
                      <AlertTriangle size={14} class="text-yellow-500" />
                      <span>{$_('merge.conflictsDetected', { values: { count: analysis.conflicts.length } })}</span>
                    {:else}
                      <Check size={14} class="text-green-500" />
                      <span>{$_('merge.noConflicts')}</span>
                    {/if}
                  </div>
                </div>

                {#if analysis.totalSeries === 0}
                  <p class="mt-3 text-[12.5px] text-muted-foreground">{$_('merge.noSeries')}</p>
                {:else}
                  {#if analysis.conflicts.length > 0}
                    <p class="mt-3 mb-2 text-[12px] text-muted-foreground">{$_('merge.conflictsHint')}</p>
                    <div class="max-h-[160px] overflow-auto rounded-lg border border-border bg-card">
                      <table class="w-full border-collapse text-[12px]">
                        <thead>
                          <tr class="border-b border-border bg-muted/50">
                            <th class="px-3 py-1.5 text-left font-medium text-muted-foreground">{$_('merge.colSeries')}</th>
                            <th class="px-3 py-1.5 text-left font-medium text-muted-foreground">{$_('merge.colMods')}</th>
                          </tr>
                        </thead>
                        <tbody>
                          {#each analysis.conflicts as conflict}
                            <tr class="border-t border-border first:border-t-0">
                              <td class="px-3 py-1.5 font-medium">{conflict.seriesName}</td>
                              <td class="px-3 py-1.5 text-muted-foreground">{conflict.mods.join(', ')}</td>
                            </tr>
                          {/each}
                        </tbody>
                      </table>
                    </div>
                  {/if}

                  <!-- Priority mod dropdown (shown when conflicts exist) -->
                  {#if analysis.conflicts.length > 0}
                    <div class="mt-4 flex flex-col gap-2">
                      <label class="text-[12.5px] font-medium text-muted-foreground">{$_('merge.priorityMod')}</label>
                      <select
                        value={priorityPath ?? ''}
                        onchange={handlePriorityChange}
                        class="rounded-lg border border-input bg-background px-3 py-2 text-[13px] focus:outline-none focus:ring-2 focus:ring-ring"
                      >
                        {#each selectedMods as mod}
                          <option value={mod.path}>{mod.name}</option>
                        {/each}
                      </select>
                      <p class="text-[12px] text-muted-foreground">{$_('merge.priorityModHint')}</p>
                    </div>
                  {/if}

                  <div class="mt-4 flex flex-col gap-2">
                    <label class="text-[12.5px] font-medium text-muted-foreground">{$_('merge.outputName')}</label>
                    <input
                      type="text"
                      value={outputName}
                      oninput={handleOutputNameInput}
                      placeholder={$_('merge.outputNamePlaceholder')}
                      class="rounded-lg border border-input bg-background px-3 py-2 text-[13px] focus:outline-none focus:ring-2 focus:ring-ring {nameError ? 'border-red-500' : ''}"
                    />
                    {#if nameError}
                      <p class="text-[12px] text-red-500">{nameError}</p>
                    {/if}
                  </div>

                  <div class="mt-4">
                    <button
                      onclick={handleMerge}
                      disabled={!canMerge || merging}
                      class="inline-flex items-center gap-2 rounded-lg px-4 py-2.5 text-[13px] font-medium text-white disabled:opacity-60"
                      style="background: linear-gradient(135deg, hsl(var(--gradient-from)), hsl(var(--gradient-to))); box-shadow: 0 4px 14px -2px hsl(var(--gradient-from) / .35);"
                    >
                      {#if merging}
                        <RefreshCw size={14} class="animate-spin" />
                        {$_('merge.merging')}
                      {:else}
                        <GitMerge size={14} />
                        {$_('merge.merge')}
                      {/if}
                    </button>
                  </div>
                {/if}
              </div>
            {/if}

            {#if result}
              <div class="rounded-xl border border-green-500/30 bg-green-500/5 p-4">
                <div class="flex items-center gap-2 text-[13px] font-semibold text-green-500">
                  <Check size={16} />
                  {$_('merge.done')}
                </div>
                <p class="mt-1 text-[12.5px] text-muted-foreground">
                  {$_('merge.doneDetail', { values: { series: result.totalSeries, tracks: result.totalTracks, conflicts: result.conflictsResolved } })}
                </p>
                <p class="mt-1 text-[12px] text-muted-foreground">{result.outputPath}</p>
                <button
                  onclick={handleReset}
                  class="mt-3 inline-flex items-center gap-2 rounded-lg border border-input bg-background px-3 py-2 text-[12.5px] font-medium transition-colors hover:bg-muted"
                >
                  <GitMerge size={14} />
                  {$_('merge.mergeAnother')}
                </button>
              </div>
            {/if}
          </div>
        {/if}
      </div>
    </section>
  </div>
</div>
