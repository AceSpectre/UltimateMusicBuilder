<script lang="ts">
  import { onMount } from 'svelte'
  import { _ } from 'svelte-i18n'
  import { Check, Minus, Square, X, FolderTree, ChevronDown, Folder, Sun, Moon, Settings } from '@lucide/svelte'
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

  let settingsOpen = $state(false)
  let pickerOpen = $state(false)
  let pickerEl: HTMLDivElement | undefined = $state()
  let stats = $state<{ seriesCount: number; trackCount: number } | null>(null)
  let version = $state('')

  $effect(() => {
    const path = activeMod?.path ?? null
    stats = null
    if (!path) return
    let cancelled = false
    void window.electron.umb.getModStats(path).then((s) => {
      if (!cancelled) stats = s
    })
    return () => { cancelled = true }
  })

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

<div class="drag-region flex flex-col shrink-0">
  <div class="h-[2px] gradient-strip"></div>

  <div class="h-14 flex items-center gap-3 px-4 bg-card border-b border-border {platform === 'darwin' ? 'pl-[84px]' : ''}">
    <div class="flex items-center gap-2.5 no-drag">
      <div
        class="w-[30px] h-[30px] rounded-lg gradient-bg flex items-center justify-center text-white font-bold text-sm tracking-wider"
        style="box-shadow: 0 4px 12px -2px hsl(var(--gradient-from) / .4);"
      >
        U
      </div>
      <div class="flex flex-col leading-tight">
        <span class="font-bold text-sm tracking-[.02em]">{$_('appBar.brand')}</span>
        <span class="font-mono text-[10.5px] text-muted-foreground">{$_('appBar.version', { values: { version } })}</span>
      </div>
    </div>

    <div bind:this={pickerEl} class="relative no-drag w-[320px] shrink-0">
      <button
        onclick={togglePicker}
        class="flex items-center gap-2 w-full h-9 px-3 rounded-lg border border-input bg-background transition-colors hover:bg-muted disabled:cursor-not-allowed disabled:opacity-70"
        disabled={loading || mods.length === 0}
      >
        <Folder size={14} class="text-muted-foreground shrink-0" />
        <div class="flex flex-col items-start leading-tight flex-1 min-w-0">
          <span class="font-mono text-[10.5px] text-muted-foreground">{$_('appBar.modsRoot')}</span>
          <span class="text-[13px] font-semibold truncate w-full text-left">
            {loading
              ? $_('appBar.loadingMods')
              : activeMod?.name ?? $_('appBar.noModSelected')}
          </span>
        </div>
        <ChevronDown size={14} class="text-muted-foreground shrink-0 transition-transform {pickerOpen ? 'rotate-180' : ''}" />
      </button>

      {#if pickerOpen}
        <div class="absolute left-0 right-0 top-[calc(100%+8px)] z-40 overflow-hidden rounded-xl border border-border bg-popover shadow-2xl">
          <div class="border-b border-border px-3 py-2.5">
            <div class="text-[10.5px] font-semibold uppercase tracking-[.12em] text-muted-foreground">{$_('appBar.selectMod')}</div>
          </div>

          <div class="max-h-[320px] overflow-y-auto p-2">
            {#each mods as mod}
              <button
                onclick={() => selectMod(mod)}
                class="flex w-full items-center gap-3 rounded-lg px-3 py-2 text-left text-sm transition-colors hover:bg-accent {activeMod?.path === mod.path ? 'bg-accent/70' : ''}"
              >
                <div
                  class="flex h-8 w-8 items-center justify-center rounded-lg border border-border"
                  style="background: linear-gradient(135deg, hsl(var(--gradient-from) / .12), hsl(var(--gradient-to) / .16)); color: hsl(var(--gradient-from));"
                >
                  <Folder size={14} />
                </div>
                <div class="min-w-0 flex-1">
                  <div class="truncate text-[13px] font-semibold">{mod.name}</div>
                  <div class="truncate text-[11px] text-muted-foreground">{$_('appBar.modPath', { values: { name: mod.name } })}</div>
                </div>
                {#if activeMod?.path === mod.path}
                  <Check size={14} class="shrink-0 text-[hsl(var(--gradient-from))]" />
                {/if}
              </button>
            {/each}
          </div>
        </div>
      {/if}
    </div>

    <div class="flex-1"></div>

    <div class="no-drag flex items-center gap-1.5">
      <span class="inline-flex items-center gap-1 px-2 h-[22px] rounded-full border border-border bg-muted text-[11px] font-medium text-muted-foreground tracking-[.02em]">
        <FolderTree size={11} />
        {$_('appBar.seriesCount', { values: { value: activeMod ? (stats?.seriesCount ?? '—') : '0' } })}
      </span>
      <span class="inline-flex items-center gap-1 px-2 h-[22px] rounded-full border border-border bg-muted text-[11px] font-medium text-muted-foreground tracking-[.02em]">
        {$_('appBar.tracksCount', { values: { value: activeMod ? (stats?.trackCount ?? '—') : '0' } })}
      </span>
    </div>

    <button
      onclick={() => themeStore.toggle()}
      class="no-drag w-8 h-8 flex items-center justify-center rounded-lg hover:bg-accent transition-colors"
      title={themeStore.current === 'dark' ? 'Switch to light mode' : 'Switch to dark mode'}
    >
      {#if themeStore.current === 'dark'}
        <Sun size={15} class="text-muted-foreground" />
      {:else}
        <Moon size={15} class="text-muted-foreground" />
      {/if}
    </button>

    <button
      onclick={() => { settingsOpen = true }}
      class="no-drag w-8 h-8 flex items-center justify-center rounded-lg hover:bg-accent transition-colors"
      title={$_('appBar.settings')}
    >
      <Settings size={15} class="text-muted-foreground" />
    </button>

    {#if platform !== 'darwin'}
      <div class="no-drag flex items-center ml-2">
        <button
          onclick={() => onWindowControlAttempt('minimize')}
          class="no-drag w-8 h-8 flex items-center justify-center rounded-lg hover:bg-accent transition-colors"
          aria-label={$_('appBar.minimize')}
        >
          <Minus size={14} class="text-muted-foreground" />
        </button>
        <button
          onclick={() => onWindowControlAttempt('fullscreen')}
          class="no-drag w-8 h-8 flex items-center justify-center rounded-lg hover:bg-accent transition-colors"
          aria-label={$_('appBar.fullscreen')}
        >
          <Square size={12} class="text-muted-foreground" />
        </button>
        <button
          onclick={() => onWindowControlAttempt('close')}
          class="no-drag w-8 h-8 flex items-center justify-center rounded-lg hover:bg-destructive/10 hover:text-destructive transition-colors"
          aria-label={$_('appBar.close')}
        >
          <X size={14} class="text-muted-foreground" />
        </button>
      </div>
    {/if}
  </div>
</div>

<SettingsModal open={settingsOpen} onClose={() => { settingsOpen = false }} />
