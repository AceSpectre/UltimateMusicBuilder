<script lang="ts">
  import AlertTriangle from '@lucide/svelte/icons/alert-triangle'
  import FlaskConical from '@lucide/svelte/icons/flask-conical'
  import RotateCcw from '@lucide/svelte/icons/rotate-ccw'
  import SlidersHorizontal from '@lucide/svelte/icons/sliders-horizontal'
  import Volume2 from '@lucide/svelte/icons/volume-2'
  import { _ } from 'svelte-i18n'
  import Modal from '$lib/components/ui/modal.svelte'
  import { DB_INPUT, clampDb } from '$lib/volume'
  import { settingsStore } from '$lib/stores/settings.svelte'
  import { clampIncidence } from '$lib/playlists'
  import type { AppSettings } from '$lib/types/electron'

  let { open, onClose }: { open: boolean; onClose: () => void } = $props()

  let globalVolumeDb = $state(0)
  let applyVolumeToVanilla = $state(false)
  let addModSongsToSmashBattle = $state(true)
  let smashBattleIncidence = $state(0)
  let albumSelectionOnAllStages = $state(true)
  let tab = $state<'general' | 'experimental'>('general')
  let saveState = $state<'idle' | 'saving' | 'saved'>('idle')
  let loaded = $state(false)
  let resetConfirmOpen = $state(false)
  let resetting = $state(false)
  let resetFailed = $state(false)

  $effect(() => {
    if (open && !loaded) {
      void window.electron.umb.getAppSettings().then((s) => {
        globalVolumeDb = s.globalVolumeDb
        applyVolumeToVanilla = s.applyVolumeToVanilla
        addModSongsToSmashBattle = s.addModSongsToSmashBattle
        smashBattleIncidence = s.smashBattleIncidence
        albumSelectionOnAllStages = s.albumSelectionOnAllStages
        loaded = true
      })
    }
    if (!open) {
      loaded = false
      saveState = 'idle'
      tab = 'general'
    }
  })

  function markEdited() {
    if (saveState === 'saved') saveState = 'idle'
  }

  function current(): AppSettings {
    return {
      globalVolumeDb: clampDb(globalVolumeDb),
      applyVolumeToVanilla,
      addModSongsToSmashBattle,
      smashBattleIncidence: clampIncidence(smashBattleIncidence),
      albumSelectionOnAllStages
    }
  }

  async function handleSave() {
    saveState = 'saving'
    try {
      await window.electron.umb.saveAppSettings(current())
      settingsStore.version++
      saveState = 'saved'
      setTimeout(() => onClose(), 600)
    } catch {
      saveState = 'idle'
    }
  }

  async function handleReset() {
    resetting = true
    resetFailed = false
    try {
      await window.electron.umb.saveAppSettings({ ...current(), globalVolumeDb: 0 })
      globalVolumeDb = 0
      await window.electron.umb.resetAllVolumes()
      settingsStore.version++
      resetConfirmOpen = false
    } catch {
      resetFailed = true
    } finally {
      resetting = false
    }
  }

  function handleBackdrop() {
    if (saveState !== 'saving') onClose()
  }
</script>

{#snippet tabButton(id: 'general' | 'experimental', Icon: typeof Volume2, label: string)}
  <button
    onclick={() => (tab = id)}
    class="inline-flex items-center gap-1.5 rounded px-3 py-1.5 text-[12.5px] font-medium transition-colors duration-100 {tab === id ? 'bg-accbg text-accink' : 'text-muted-foreground hover:bg-accent hover:text-foreground'}"
  >
    <Icon size={13} />
    {label}
  </button>
{/snippet}

{#snippet switchRow(id: string, label: string, value: boolean, set: (value: boolean) => void, onClass: string)}
  <div class="flex items-center justify-between gap-3">
    <span id="{id}-label" class="text-[12.5px] font-medium">{$_(label)}</span>
    <button
      role="switch"
      aria-checked={value}
      aria-labelledby="{id}-label"
      onclick={() => { set(!value); markEdited() }}
      class="relative h-5 w-9 shrink-0 rounded-full transition-colors {value ? onClass : 'bg-dim/50'}"
    >
      <span class="absolute top-0.5 left-0.5 h-4 w-4 rounded-full bg-card transition-transform {value ? 'translate-x-4' : ''}"></span>
    </button>
  </div>
{/snippet}

{#snippet toggle(id: string, label: string, hint: string, value: boolean, set: (value: boolean) => void)}
  <div class="flex flex-col gap-1">
    {@render switchRow(id, label, value, set, 'bg-primary')}
    <p class="text-[11.5px] text-muted-foreground">{$_(hint)}</p>
  </div>
{/snippet}

{#if open}
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div
    class="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4"
    onclick={handleBackdrop}
    onkeydown={(e) => { if (e.key === 'Escape' && saveState !== 'saving') onClose() }}
  >
    <!-- svelte-ignore a11y_no_static_element_interactions -->
    <div
      class="w-full max-w-[420px] rounded-xl border border-border bg-popover shadow-overlay overflow-hidden"
      onclick={(e) => e.stopPropagation()}
      onkeydown={(e) => e.stopPropagation()}
    >
      <div class="border-b border-border px-5 py-4">
        <h2 class="text-[13.5px] font-semibold">{$_('settings.title')}</h2>
        <p class="mt-[3px] text-[12.5px] text-muted-foreground">{$_('settings.subtitle')}</p>
      </div>

      <div class="flex items-center gap-1 border-b border-line2 px-2 py-1.5">
        {@render tabButton('general', SlidersHorizontal, $_('settings.tabGeneral'))}
        {@render tabButton('experimental', FlaskConical, $_('settings.tabExperimental'))}
      </div>

      {#if tab === 'general'}
        <div class="px-5 py-5 flex flex-col gap-4">
          <div class="flex flex-col gap-1.5">
            <label for="global-volume" class="flex items-center gap-1.5 text-[12.5px] font-medium">
              <Volume2 size={13} class="text-dim" />
              {$_('settings.globalVolume')}
            </label>
            <input
              id="global-volume"
              type="number"
              {...DB_INPUT}
              bind:value={globalVolumeDb}
              oninput={markEdited}
              class="field"
            />
            <p class="text-[11.5px] text-muted-foreground">{$_('settings.globalVolumeHint')}</p>
          </div>
          <button
            onclick={() => { resetFailed = false; resetConfirmOpen = true }}
            disabled={saveState === 'saving'}
            class="btn-danger self-start"
          >
            <RotateCcw size={14} />
            {$_('settings.resetVolumes')}
          </button>

          <div class="flex flex-col gap-3 border-t border-line2 pt-4">
            {@render toggle('add-smash-battle', 'settings.addModSongsToSmashBattle', 'settings.addModSongsToSmashBattleHint', addModSongsToSmashBattle, (v) => (addModSongsToSmashBattle = v))}
            <div class="flex flex-col gap-1.5">
              <label for="smash-battle-incidence" class="text-[12.5px] font-medium">{$_('settings.smashBattleIncidence')}</label>
              <input
                id="smash-battle-incidence"
                type="number"
                min="0"
                max="65535"
                bind:value={smashBattleIncidence}
                oninput={markEdited}
                disabled={!addModSongsToSmashBattle}
                class="field font-mono"
              />
              <p class="text-[11.5px] text-muted-foreground">{$_('settings.smashBattleIncidenceHint')}</p>
            </div>
            {@render toggle('album-selection', 'settings.albumSelectionOnAllStages', 'settings.albumSelectionOnAllStagesHint', albumSelectionOnAllStages, (v) => (albumSelectionOnAllStages = v))}
          </div>
        </div>
      {:else}
        <div class="px-5 py-5 flex flex-col gap-1.5">
          {@render switchRow('apply-vanilla', 'settings.applyVolumeToVanilla', applyVolumeToVanilla, (v) => (applyVolumeToVanilla = v), 'bg-destructive')}
          <p class="flex items-start gap-1.5 text-[11.5px] text-destructive">
            <AlertTriangle size={13} class="mt-0.5 shrink-0" />
            {$_('settings.applyVolumeToVanillaWarning')}
          </p>
        </div>
      {/if}

      <div class="border-t border-border px-5 py-4 flex items-center justify-end gap-2">
        <button
          onclick={onClose}
          disabled={saveState === 'saving'}
          class="btn"
        >
          {$_('common.cancel')}
        </button>
        <button
          onclick={handleSave}
          disabled={saveState === 'saving'}
          class="btn-primary"
        >
          {saveState === 'saving' ? $_('common.saving') : saveState === 'saved' ? $_('common.saved') : $_('settings.save')}
        </button>
      </div>
    </div>
  </div>
{/if}

{#if resetConfirmOpen}
  <Modal>
    <div class="flex flex-col gap-3 px-5 py-4">
      <h3 class="flex items-center gap-2 text-[13.5px] font-semibold">
        <AlertTriangle size={15} class="text-destructive" />
        {$_('settings.resetModal.title')}
      </h3>
      <p class="text-[12.5px] text-muted-foreground">{$_('settings.resetModal.description')}</p>
      {#if resetFailed}
        <p class="text-[12px] text-destructive">{$_('settings.resetModal.failed')}</p>
      {/if}
    </div>
    {#snippet footer()}
      <button
        onclick={() => { resetConfirmOpen = false }}
        disabled={resetting}
        class="btn"
      >
        {$_('common.cancel')}
      </button>
      <button
        onclick={handleReset}
        disabled={resetting}
        class="btn-danger"
      >
        <RotateCcw size={14} />
        {resetting ? $_('settings.resetModal.resetting') : $_('settings.resetModal.confirm')}
      </button>
    {/snippet}
  </Modal>
{/if}
