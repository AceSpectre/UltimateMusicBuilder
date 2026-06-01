<script lang="ts">
  import { onMount } from 'svelte'
  import { _ } from 'svelte-i18n'
  import AppBar from '$lib/components/app-bar.svelte'
  import Sidebar from '$lib/components/sidebar.svelte'
  import BottomPanel from '$lib/components/bottom-panel.svelte'
  import CommandPalette from '$lib/components/command-palette.svelte'
  import BuildView from '$lib/components/actions/build-view.svelte'
  import OrderTracksView from '$lib/components/actions/order-tracks-view.svelte'
  import OrderSeriesView from '$lib/components/actions/order-series-view.svelte'
  import Nus3ConvertView from '$lib/components/actions/nus3-convert-view.svelte'
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
      if ((e.ctrlKey || e.metaKey) && e.key === 'b') {
        e.preventDefault()
        // Sidebar collapse is handled inside the sidebar component for now.
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
        <BuildView />
      {:else if activeTab === 'order-series'}
        <OrderSeriesView activeMod={activeMod} />
      {:else if activeTab === 'nus3-convert'}
        <Nus3ConvertView activeMod={activeMod} />
      {:else if activeTab === 'order-tracks'}
        <OrderTracksView activeMod={activeMod} />
      {:else}
        <div class="flex-1 grid place-items-center p-8">
          <div class="flex flex-col items-center gap-3 max-w-[420px] text-center">
            <div
              class="w-14 h-14 rounded-[14px] flex items-center justify-center border border-border"
              style="background: linear-gradient(135deg, hsl(var(--gradient-from) / .15), hsl(var(--gradient-to) / .15)); color: hsl(var(--gradient-from));"
            >
              <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <rect x="3" y="3" width="18" height="18" rx="2"/>
                <path d="M3 14h18"/>
              </svg>
            </div>
            <h2 class="text-base font-semibold">{diagnostics.lastAction === 'none' ? $_('app.selectAction') : diagnostics.lastAction}</h2>
            <p class="text-[13.5px] text-muted-foreground">
              {$_('app.viewModeNotice')}
            </p>
          </div>
        </div>
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
