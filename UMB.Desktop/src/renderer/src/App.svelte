<script lang="ts">
  import { onMount } from 'svelte'
  import AppBar from '$lib/components/app-bar.svelte'
  import Sidebar from '$lib/components/sidebar.svelte'
  import BottomPanel from '$lib/components/bottom-panel.svelte'
  import CommandPalette from '$lib/components/command-palette.svelte'
  import BuildView from '$lib/components/actions/build-view.svelte'
  import OrderTracksView from '$lib/components/actions/order-tracks-view.svelte'
  import OrderSeriesView from '$lib/components/actions/order-series-view.svelte'
  import Nus3ConvertView from '$lib/components/actions/nus3-convert-view.svelte'
  import ConfigVolumeView from '$lib/components/actions/config-volume-view.svelte'
  import ExtractIconsView from '$lib/components/actions/extract-icons-view.svelte'
  import MergeView from '$lib/components/actions/merge-view.svelte'
  import PlaylistInfoView from '$lib/components/actions/playlist-info-view.svelte'
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

  function selectMod(mod: ModInfo) {
    activeMod = mod
    diagnostics.modSelections += 1
    diagnostics.lastMod = mod.name
  }

  function selectAction(id: string) {
    activeTab = id
    diagnostics.actionClicks += 1
    diagnostics.lastAction = id
  }

  async function handleWindowControl(action: 'minimize' | 'fullscreen' | 'close') {
    diagnostics.windowClicks += 1
    diagnostics.lastWindowRequest = action
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
        <OrderSeriesView activeMod={activeMod} />
      {:else if activeTab === 'nus3-convert'}
        <Nus3ConvertView activeMod={activeMod} />
      {:else if activeTab === 'config-volume'}
        <ConfigVolumeView activeMod={activeMod} />
      {:else if activeTab === 'order-tracks'}
        <OrderTracksView activeMod={activeMod} onNavigate={selectAction} />
      {:else if activeTab === 'manage-playlists'}
        <ManagePlaylistsView activeMod={activeMod} />
      {:else if activeTab === 'extract-icons'}
        <ExtractIconsView activeMod={activeMod} mods={mods} />
      {:else if activeTab === 'merge'}
        <MergeView mods={mods} />
      {:else if activeTab === 'playlist-info'}
        <PlaylistInfoView />
      {/if}
    </main>
  </div>

  <BottomPanel diagnostics={diagnostics} onPingBridge={runBridgePing} />
</div>

<CommandPalette
  bind:open={commandPaletteOpen}
  activeTab={activeTab}
  mods={mods}
  activeMod={activeMod}
  onSelectAction={selectAction}
  onSelectMod={selectMod}
/>
