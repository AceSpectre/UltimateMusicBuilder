<script lang="ts">
  import Image from '@lucide/svelte/icons/image'
  import FolderOpen from '@lucide/svelte/icons/folder-open'
  import RefreshCw from '@lucide/svelte/icons/refresh-cw'
  import Download from '@lucide/svelte/icons/download'
  import Check from '@lucide/svelte/icons/check'
  import AlertTriangle from '@lucide/svelte/icons/alert-triangle'
  import { _ } from 'svelte-i18n'
  import { logStore } from '$lib/stores/logs.svelte'
  import ActionHeader from '$lib/components/ui/action-header.svelte'
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
      logStore.log('error', `Analysis failed: ${err instanceof Error ? err.message : String(err)}`)
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
      logStore.log('error', `Extraction failed: ${err instanceof Error ? err.message : String(err)}`)
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
    <section class="surface-grain flex h-full min-h-0 w-full flex-col overflow-hidden bg-card">

      <ActionHeader title={$_('extractIcons.title')} subtitle={$_('extractIcons.subtitle')} />

      <div class="min-h-0 flex-1 overflow-auto px-[22px] py-3.5">
        {#if mods.length === 0}
          <div class="rounded-xl border border-dashed border-border px-3 py-8 text-center text-[13px] text-muted-foreground">
            {$_('common.chooseMod')}
          </div>
        {:else}
          <div class="flex flex-col gap-5">
            <div class="flex flex-col gap-2">
              <label class="text-[12.5px] font-medium text-muted-foreground">{$_('extractIcons.compiledModLabel')}</label>
              <div class="flex items-center gap-2">
                <div class="field flex flex-1 items-center truncate {compiledModPath ? '' : 'text-muted-foreground'}">
                  {compiledModPath ?? $_('extractIcons.compiledModPlaceholder')}
                </div>
                <button
                  onclick={handleBrowse}
                  class="btn"
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
                class="field"
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
                class="btn-primary self-start"
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
              <div class="rounded-xl border border-border p-4">
                <div class="grid grid-cols-2 gap-3 text-[13px]">
                  <div class="flex items-center gap-2">
                    <Check size={14} class="text-primary" />
                    <span>{$_('extractIcons.resultMatched', { values: { count: analysis.matched.length } })}</span>
                  </div>
                  <div class="flex items-center gap-2">
                    <AlertTriangle size={14} class="text-warn" />
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
                          <tr class="border-t border-line2 first:border-t-0 hover:bg-accent">
                            <td class="px-3 py-1.5 font-medium">{match.seriesId}</td>
                            <td class="px-3 py-1.5 text-right text-muted-foreground">
                              {#if match.hasExistingIcon}
                                <span class="inline-flex items-center gap-1 text-warn">
                                  <Image size={12} /> has icon
                                </span>
                              {:else}
                                <span class="text-primary">no icon</span>
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
                        class="btn-primary"
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
                          class="btn"
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
              <div class="rounded-xl border border-line2 p-4">
                <div class="flex items-center gap-2 text-[13px] font-semibold text-primary">
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
