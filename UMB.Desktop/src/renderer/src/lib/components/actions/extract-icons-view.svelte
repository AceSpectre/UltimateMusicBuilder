<script lang="ts">
  import { Image, FolderOpen, RefreshCw, Download, Check, AlertTriangle } from '@lucide/svelte'
  import { _ } from 'svelte-i18n'
  import { logStore } from '$lib/stores/logs.svelte'
  import type { ModInfo, ExtractIconsAnalysis, ExtractIconsResult } from '$lib/types/electron'

  let { activeMod, mods }: { activeMod: ModInfo | null; mods: ModInfo[] } = $props()

  let compiledModPath = $state<string | null>(null)
  let selectedMod = $state<ModInfo | null>(activeMod)
  let analysis = $state<ExtractIconsAnalysis | null>(null)
  let result = $state<ExtractIconsResult | null>(null)
  let analyzing = $state(false)
  let extracting = $state(false)

  $effect(() => {
    if (activeMod && !selectedMod) {
      selectedMod = activeMod
    }
  })

  function log(level: 'info' | 'warn' | 'error', message: string) {
    logStore.push({ timestamp: new Date().toLocaleTimeString('en-GB', { hour12: false }), level, message })
  }

  async function handleBrowse() {
    const folder = await window.electron.umb.selectFolder()
    if (folder) {
      compiledModPath = folder
      analysis = null
      result = null
    }
  }

  async function handleAnalyze() {
    if (!compiledModPath || !selectedMod) return
    analyzing = true
    result = null
    try {
      analysis = await window.electron.umb.analyzeExtractIcons(compiledModPath, selectedMod.path)
    } catch (err) {
      log('error', `Analysis failed: ${err instanceof Error ? err.message : String(err)}`)
      analysis = null
    } finally {
      analyzing = false
    }
  }

  async function handleExtract(mode: 'all' | 'missing-only') {
    if (!compiledModPath || !selectedMod) return
    extracting = true
    try {
      result = await window.electron.umb.extractIcons(compiledModPath, selectedMod.path, mode)
    } catch (err) {
      log('error', `Extraction failed: ${err instanceof Error ? err.message : String(err)}`)
    } finally {
      extracting = false
    }
  }

  function handleModChange(e: Event) {
    const target = e.currentTarget as HTMLSelectElement
    const mod = mods.find(m => m.path === target.value) ?? null
    selectedMod = mod
    analysis = null
    result = null
  }

  const withIcon = $derived(analysis?.matched.filter(m => m.hasExistingIcon).length ?? 0)
  const withoutIcon = $derived(analysis?.matched.filter(m => !m.hasExistingIcon).length ?? 0)
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
          <Image size={18} />
        </div>
        <div class="min-w-0">
          <h2 class="truncate text-sm font-semibold">{$_('extractIcons.title')}</h2>
          <p class="truncate text-[12.5px] text-muted-foreground">{$_('extractIcons.subtitle')}</p>
        </div>
      </div>

      <div class="min-h-0 flex-1 overflow-auto px-5 py-5">
        {#if mods.length === 0}
          <div class="rounded-xl border border-dashed border-border bg-background/70 px-3 py-8 text-center text-[13px] text-muted-foreground">
            {$_('extractIcons.chooseMod')}
          </div>
        {:else}
          <div class="flex flex-col gap-5">
            <div class="flex flex-col gap-2">
              <label class="text-[12.5px] font-medium text-muted-foreground">{$_('extractIcons.compiledModLabel')}</label>
              <div class="flex items-center gap-2">
                <div class="flex-1 truncate rounded-lg border border-input bg-background px-3 py-2 text-[13px] {compiledModPath ? '' : 'text-muted-foreground'}">
                  {compiledModPath ?? $_('extractIcons.compiledModPlaceholder')}
                </div>
                <button
                  onclick={handleBrowse}
                  class="inline-flex items-center gap-2 rounded-lg border border-input bg-background px-3 py-2 text-[12.5px] font-medium transition-colors hover:bg-muted"
                >
                  <FolderOpen size={14} />
                  {$_('extractIcons.browse')}
                </button>
              </div>
            </div>

            <div class="flex flex-col gap-2">
              <label class="text-[12.5px] font-medium text-muted-foreground">{$_('extractIcons.targetModLabel')}</label>
              <select
                value={selectedMod?.path ?? ''}
                onchange={handleModChange}
                class="rounded-lg border border-input bg-background px-3 py-2 text-[13px] focus:outline-none focus:ring-2 focus:ring-ring"
              >
                {#each mods as mod}
                  <option value={mod.path}>{mod.name}</option>
                {/each}
              </select>
            </div>

            {#if compiledModPath && selectedMod && !analysis}
              <button
                onclick={handleAnalyze}
                disabled={analyzing}
                class="self-start inline-flex items-center gap-2 rounded-lg px-4 py-2.5 text-[13px] font-medium text-white disabled:opacity-60"
                style="background: linear-gradient(135deg, hsl(var(--gradient-from)), hsl(var(--gradient-to))); box-shadow: 0 4px 14px -2px hsl(var(--gradient-from) / .35);"
              >
                {#if analyzing}
                  <RefreshCw size={14} class="animate-spin" />
                  {$_('extractIcons.analyzing')}
                {:else}
                  <Image size={14} />
                  {$_('extractIcons.analyze')}
                {/if}
              </button>
            {/if}

            {#if analysis}
              <div class="rounded-xl border border-border bg-background p-4">
                <div class="grid grid-cols-2 gap-3 text-[13px]">
                  <div class="flex items-center gap-2">
                    <Check size={14} class="text-green-500" />
                    <span>{$_('extractIcons.resultMatched', { values: { count: analysis.matched.length } })}</span>
                  </div>
                  <div class="flex items-center gap-2">
                    <AlertTriangle size={14} class="text-yellow-500" />
                    <span>{$_('extractIcons.resultUnmatched', { values: { count: analysis.unmatched.length } })}</span>
                  </div>
                  <div class="flex items-center gap-2 text-muted-foreground">
                    <Image size={14} />
                    <span>{$_('extractIcons.resultWithIcon', { values: { count: withIcon } })}</span>
                  </div>
                  <div class="flex items-center gap-2 text-muted-foreground">
                    <Image size={14} />
                    <span>{$_('extractIcons.resultWithoutIcon', { values: { count: withoutIcon } })}</span>
                  </div>
                </div>

                {#if analysis.matched.length === 0}
                  <p class="mt-3 text-[12.5px] text-muted-foreground">{$_('extractIcons.noMatches')}</p>
                {:else}
                  <div class="mt-4 max-h-[200px] overflow-auto rounded-lg border border-border bg-card">
                    <table class="w-full border-collapse text-[12px]">
                      <tbody>
                        {#each analysis.matched as match}
                          <tr class="border-t border-border first:border-t-0">
                            <td class="px-3 py-1.5 font-medium">{match.seriesId}</td>
                            <td class="px-3 py-1.5 text-right text-muted-foreground">
                              {#if match.hasExistingIcon}
                                <span class="inline-flex items-center gap-1 text-yellow-500">
                                  <Image size={12} /> has icon
                                </span>
                              {:else}
                                <span class="text-green-500">no icon</span>
                              {/if}
                            </td>
                          </tr>
                        {/each}
                      </tbody>
                    </table>
                  </div>

                  {#if !result}
                    <div class="mt-4 flex items-center gap-3">
                      <button
                        onclick={() => handleExtract('all')}
                        disabled={extracting}
                        class="inline-flex items-center gap-2 rounded-lg px-4 py-2.5 text-[13px] font-medium text-white disabled:opacity-60"
                        style="background: linear-gradient(135deg, hsl(var(--gradient-from)), hsl(var(--gradient-to))); box-shadow: 0 4px 14px -2px hsl(var(--gradient-from) / .35);"
                      >
                        {#if extracting}
                          <RefreshCw size={14} class="animate-spin" />
                          {$_('extractIcons.extracting')}
                        {:else}
                          <Download size={14} />
                          {$_('extractIcons.extractAll')}
                        {/if}
                      </button>
                      {#if withoutIcon > 0}
                        <button
                          onclick={() => handleExtract('missing-only')}
                          disabled={extracting}
                          class="inline-flex items-center gap-2 rounded-lg border border-input bg-background px-4 py-2.5 text-[13px] font-medium transition-colors hover:bg-muted disabled:opacity-60"
                        >
                          {#if extracting}
                            <RefreshCw size={14} class="animate-spin" />
                            {$_('extractIcons.extracting')}
                          {:else}
                            <Download size={14} />
                            {$_('extractIcons.extractMissing')}
                          {/if}
                        </button>
                      {/if}
                    </div>
                  {/if}
                {/if}
              </div>
            {/if}

            {#if result}
              <div class="rounded-xl border border-green-500/30 bg-green-500/5 p-4">
                <div class="flex items-center gap-2 text-[13px] font-semibold text-green-500">
                  <Check size={16} />
                  {$_('extractIcons.done')}
                </div>
                <p class="mt-1 text-[12.5px] text-muted-foreground">
                  {$_('extractIcons.doneDetail', { values: { extracted: result.extracted, skipped: result.skipped, failed: result.failed } })}
                </p>
              </div>
            {/if}
          </div>
        {/if}
      </div>
    </section>
  </div>
</div>
