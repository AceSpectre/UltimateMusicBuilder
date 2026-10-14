<script lang="ts">
  import { onMount, tick } from 'svelte'
  import { _ } from 'svelte-i18n'
  import Modal from '$lib/components/ui/modal.svelte'
  import { guardLeave, unsavedStore } from '$lib/stores/unsaved.svelte'
  import AppBar from '$lib/components/app-bar.svelte'
  import Sidebar from '$lib/components/sidebar.svelte'
  import BottomPanel from '$lib/components/bottom-panel.svelte'
  import CommandPalette from '$lib/components/command-palette.svelte'
  import BuildView from '$lib/components/actions/build-view.svelte'
  import OrderTracksView from '$lib/components/actions/order-tracks-view.svelte'
  import ManagePresetsView from '$lib/components/actions/manage-presets-view.svelte'
  import OrderSeriesView from '$lib/components/actions/order-series-view.svelte'
  import Nus3ConvertView from '$lib/components/actions/nus3-convert-view.svelte'
  import ConfigVolumeView from '$lib/components/actions/config-volume-view.svelte'
  import ExtractIconsView from '$lib/components/actions/extract-icons-view.svelte'
  import MergeView from '$lib/components/actions/merge-view.svelte'
  import ManagePlaylistsView from '$lib/components/actions/manage-playlists-view.svelte'
  import { logStore } from '$lib/stores/logs.svelte'
  import type { ModInfo, WindowActionResult } from '$lib/types/electron'

  let commandPaletteOpen = $state(false)
  let mods = $state<ModInfo[]>([])
  let activeMod = $state<ModInfo | null>(null)
  let modsLoading = $state(false)
  let activeTab = $state('build')
  let diagnostics = $state({
    bridgeStatus: 'checking',
    workspace: 'pending',
    modsStatus: 'idle',
    actionClicks: 0,
    lastAction: 'none',
    modSelections: 0,
    lastMod: 'none',
    windowClicks: 0,
    lastWindowRequest: 'none',
    lastWindowAck: 'none',
    lastError: 'none'
  })

  async function runBridgePing() {
    diagnostics.bridgeStatus = 'pinging'
    try {
      const result = await window.electron.umb.debugPing()
      diagnostics.bridgeStatus = result.ok ? 'ok' : 'not-ok'
      diagnostics.workspace = result.workspace
    } catch (error) {
      diagnostics.bridgeStatus = 'error'
      diagnostics.lastError = error instanceof Error ? error.message : String(error)
    }
  }

  async function loadMods() {
    diagnostics.modsStatus = 'loading'
    modsLoading = true
    try {
      mods = await window.electron.umb.listMods()
      if (!mods.some((mod) => mod.path === activeMod?.path)) {
        activeMod = mods[0] ?? null
      }
      diagnostics.modsStatus = `loaded:${mods.length}`
    } finally {
      modsLoading = false
    }
  }

  // Leaving the open view, mod or series with unsaved changes asks first.
  let leaveSaving = $state(false)

  function leaveNow() {
    const leave = unsavedStore.pendingLeave
    unsavedStore.pendingLeave = null
    leave?.()
  }

  async function saveAndLeave() {
    leaveSaving = true
    await unsavedStore.save?.()
    await tick()
    leaveSaving = false
    // A failed save stays dirty: keep the user on the view.
    if (unsavedStore.dirty) unsavedStore.pendingLeave = null
    else leaveNow()
  }

  function selectMod(mod: ModInfo) {
    const select = () => {
      activeMod = mod
      diagnostics.modSelections += 1
      diagnostics.lastMod = mod.name
    }
    if (mod.path === activeMod?.path) select()
    else guardLeave(select)
  }

  function selectAction(id: string) {
    const select = () => {
      activeTab = id
      diagnostics.actionClicks += 1
      diagnostics.lastAction = id
    }
    if (id === activeTab) select()
    else guardLeave(select)
  }

  async function handleWindowControl(action: 'minimize' | 'fullscreen' | 'close') {
    diagnostics.windowClicks += 1
    diagnostics.lastWindowRequest = action
    if (action === 'close' && unsavedStore.dirty) {
      unsavedStore.pendingLeave = () => void window.electron.umb.windowClose()
      return
    }
    try {
      let result: WindowActionResult
      if (action === 'minimize') {
        result = await window.electron.umb.windowMinimize()
      } else if (action === 'fullscreen') {
        result = await window.electron.umb.windowFullscreen()
      } else {
        result = await window.electron.umb.windowClose()
      }

      diagnostics.lastWindowAck = result.ok
        ? `${result.action}${typeof result.fullScreen === 'boolean' ? `:${result.fullScreen}` : ''}`
        : `failed:${result.action}`
    } catch (error) {
      diagnostics.lastWindowAck = 'error'
      diagnostics.lastError = error instanceof Error ? error.message : String(error)
    }
  }

  onMount(() => {
    void runBridgePing()
    void loadMods()

    const unsubLogs = window.electron.umb.subscribeLogs((line) => {
      logStore.push(line)
    })

    function handleKeydown(e: KeyboardEvent) {
      if ((e.ctrlKey || e.metaKey) && e.key === 'k') {
        e.preventDefault()
        commandPaletteOpen = !commandPaletteOpen
      }
      if ((e.ctrlKey || e.metaKey) && e.key === 'j') {
        e.preventDefault()
        logStore.toggleDrawer()
      }
    }

    window.addEventListener('keydown', handleKeydown)

    return () => {
      unsubLogs()
      window.removeEventListener('keydown', handleKeydown)
    }
  })
</script>

<div class="h-screen w-screen flex flex-col overflow-hidden bg-background text-foreground">
  <AppBar
    platform={window.electron.umb.platform}
    mods={mods}
    activeMod={activeMod}
    loading={modsLoading}
    onSelectMod={selectMod}
    onWindowControlAttempt={handleWindowControl}
  />

  <div class="flex flex-1 overflow-hidden">
    <Sidebar activeTab={activeTab} onSelectAction={selectAction} />

    <main class="flex-1 flex flex-col overflow-hidden bg-background">
      {#if activeTab === 'build'}
        <BuildView activeMod={activeMod} />
      {:else if activeTab === 'order-series'}
        <OrderSeriesView activeMod={activeMod} onNavigate={selectAction} />
      {:else if activeTab === 'nus3-convert'}
        <Nus3ConvertView activeMod={activeMod} />
      {:else if activeTab === 'config-volume'}
        <ConfigVolumeView activeMod={activeMod} />
      {:else if activeTab === 'order-tracks'}
        <OrderTracksView activeMod={activeMod} />
      {:else if activeTab === 'manage-presets'}
        <ManagePresetsView activeMod={activeMod} />
      {:else if activeTab === 'manage-playlists'}
        <ManagePlaylistsView activeMod={activeMod} />
      {:else if activeTab === 'extract-icons'}
        <ExtractIconsView activeMod={activeMod} mods={mods} />
      {:else if activeTab === 'merge'}
        <MergeView mods={mods} />
      {/if}
    </main>
  </div>

  <BottomPanel diagnostics={diagnostics} onPingBridge={runBridgePing} />
</div>

{#if unsavedStore.pendingLeave}
  <Modal>
    <div class="px-5 py-4">
      <h3 class="text-sm font-semibold">{$_('unsaved.title')}</h3>
      <p class="pt-1.5 text-[13px] text-muted-foreground">{$_('unsaved.body')}</p>
    </div>
    <div class="flex flex-col gap-2 border-t border-border px-5 py-4">
      <button
        onclick={saveAndLeave}
        disabled={leaveSaving}
        class="inline-flex items-center justify-center rounded-lg border border-input bg-background px-3 py-2 text-[12.5px] font-medium transition-colors hover:bg-muted disabled:opacity-60"
      >
        {$_('unsaved.save')}
      </button>
      <button
        onclick={leaveNow}
        disabled={leaveSaving}
        class="inline-flex items-center justify-center rounded-lg border border-input bg-background px-3 py-2 text-[12.5px] font-medium transition-colors hover:bg-muted disabled:opacity-60"
      >
        {$_('unsaved.discard')}
      </button>
      <button
        onclick={() => (unsavedStore.pendingLeave = null)}
        disabled={leaveSaving}
        class="inline-flex items-center justify-center rounded-lg px-3 py-2 text-[12.5px] font-medium text-muted-foreground transition-colors hover:bg-muted"
      >
        {$_('unsaved.stay')}
      </button>
    </div>
  </Modal>
{/if}

<CommandPalette
  bind:open={commandPaletteOpen}
  activeTab={activeTab}
  mods={mods}
  activeMod={activeMod}
  onSelectAction={selectAction}
  onSelectMod={selectMod}
/>
