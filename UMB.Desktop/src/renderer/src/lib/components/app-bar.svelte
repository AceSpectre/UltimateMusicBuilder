<script lang="ts">
  import { onMount } from 'svelte'
  import { _ } from 'svelte-i18n'
  import Check from '@lucide/svelte/icons/check'
  import Minus from '@lucide/svelte/icons/minus'
  import Square from '@lucide/svelte/icons/square'
  import X from '@lucide/svelte/icons/x'
  import ChevronDown from '@lucide/svelte/icons/chevron-down'
  import Sun from '@lucide/svelte/icons/sun'
  import Moon from '@lucide/svelte/icons/moon'
  import Settings from '@lucide/svelte/icons/settings'
  import { themeStore } from '$lib/stores/theme.svelte'
  import SettingsModal from './settings-modal.svelte'
  import type { ModInfo } from '$lib/types/electron'

  let {
    platform,
    mods,
    activeMod,
    loading,
    onSelectMod,
    onWindowControlAttempt
  }: {
    platform: NodeJS.Platform
    mods: ModInfo[]
    activeMod: ModInfo | null
    loading: boolean
    onSelectMod: (mod: ModInfo) => void
    onWindowControlAttempt: (action: 'minimize' | 'fullscreen' | 'close') => void | Promise<void>
  } = $props()

  const iconButton =
    'flex h-[26px] w-[26px] items-center justify-center rounded text-dim transition-colors duration-100 hover:bg-accent hover:text-foreground'

  let settingsOpen = $state(false)
  let pickerOpen = $state(false)
  let pickerEl: HTMLDivElement | undefined = $state()
  let version = $state('')

  function togglePicker() {
    if (mods.length === 0) {
      return
    }

    pickerOpen = !pickerOpen
  }

  function selectMod(mod: ModInfo) {
    onSelectMod(mod)
    pickerOpen = false
  }

  onMount(() => {
    void window.electron.umb.getAppVersion().then((v) => { version = v })

    function handlePointerDown(event: PointerEvent) {
      if (!pickerOpen || !pickerEl) {
        return
      }

      const target = event.target
      if (target instanceof Node && !pickerEl.contains(target)) {
        pickerOpen = false
      }
    }

    function handleKeydown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        pickerOpen = false
      }
    }

    window.addEventListener('pointerdown', handlePointerDown)
    window.addEventListener('keydown', handleKeydown)

    return () => {
      window.removeEventListener('pointerdown', handlePointerDown)
      window.removeEventListener('keydown', handleKeydown)
    }
  })
</script>

<div class="drag-region surface-grain flex h-[46px] shrink-0 items-center gap-3 border-b border-border bg-card px-3 {platform === 'darwin' ? 'pl-[84px]' : ''}">
  <div class="no-drag flex items-baseline gap-[9px]">
    <span class="text-[13px] font-semibold tracking-[-.01em]">{$_('appBar.brand')}</span>
    <span class="text-[11.5px] text-dim">{$_('appBar.version', { values: { version } })}</span>
  </div>

  <div bind:this={pickerEl} class="relative no-drag min-w-[260px] shrink-0">
    <button
      onclick={togglePicker}
      class="flex h-7 w-full items-center gap-0.5 rounded border border-border bg-rail px-[9px] text-[12px] transition-colors duration-100 hover:border-dim disabled:cursor-not-allowed disabled:opacity-45"
      disabled={loading || mods.length === 0}
    >
      <span class="text-dim">{$_('appBar.modsRoot')}</span>
      <span class="min-w-0 truncate font-medium">
        {loading
          ? $_('appBar.loadingMods')
          : activeMod?.name ?? $_('appBar.noModSelected')}
      </span>
      <span class="flex-1"></span>
      <ChevronDown size={13} class="shrink-0 text-dim" />
    </button>

    {#if pickerOpen}
      <div class="shadow-overlay absolute left-0 right-0 top-[calc(100%+6px)] z-40 overflow-hidden rounded border border-border bg-popover">
        <div class="border-b border-line2 px-3 py-2 text-[10.5px] font-semibold uppercase tracking-[.1em] text-dim">{$_('appBar.selectMod')}</div>

        <div class="max-h-[320px] overflow-y-auto p-1">
          {#each mods as mod}
            {@const isActive = activeMod?.path === mod.path}
            <button
              onclick={() => selectMod(mod)}
              class="flex w-full items-center gap-2 rounded px-2.5 py-1.5 text-left transition-colors duration-100 {isActive ? 'bg-accbg text-accink' : 'hover:bg-accent'}"
            >
              <div class="min-w-0 flex-1">
                <div class="truncate text-[13px] font-medium">{mod.name}</div>
                <div class="truncate text-[11px] {isActive ? 'text-accink/70' : 'text-dim'}">{$_('appBar.modPath', { values: { name: mod.name } })}</div>
              </div>
              {#if isActive}
                <Check size={14} class="shrink-0 text-primary" />
              {/if}
            </button>
          {/each}
        </div>
      </div>
    {/if}
  </div>

  <div class="flex-1"></div>

  <div class="no-drag flex items-center">
    <button
      onclick={() => themeStore.toggle()}
      class={iconButton}
      title={themeStore.current === 'dark' ? 'Switch to light mode' : 'Switch to dark mode'}
    >
      {#if themeStore.current === 'dark'}
        <Sun size={14} strokeWidth={1.8} />
      {:else}
        <Moon size={14} strokeWidth={1.8} />
      {/if}
    </button>

    <button onclick={() => { settingsOpen = true }} class={iconButton} title={$_('appBar.settings')}>
      <Settings size={14} strokeWidth={1.8} />
    </button>
  </div>

  {#if platform !== 'darwin'}
    <div class="no-drag ml-1 flex items-center">
      <button onclick={() => onWindowControlAttempt('minimize')} class={iconButton} aria-label={$_('appBar.minimize')}>
        <Minus size={14} strokeWidth={1.8} />
      </button>
      <button onclick={() => onWindowControlAttempt('fullscreen')} class={iconButton} aria-label={$_('appBar.fullscreen')}>
        <Square size={12} strokeWidth={1.8} />
      </button>
      <button onclick={() => onWindowControlAttempt('close')} class={iconButton} aria-label={$_('appBar.close')}>
        <X size={14} strokeWidth={1.8} />
      </button>
    </div>
  {/if}
</div>

<SettingsModal open={settingsOpen} onClose={() => { settingsOpen = false }} />
