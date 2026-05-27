<script lang="ts">
  import { PanelBottomClose, PanelBottomOpen, RefreshCw, X, ChevronLeft, ChevronRight } from '@lucide/svelte'
  import { tick } from 'svelte'
  import { logStore } from '$lib/stores/logs.svelte'

  interface DiagnosticsState {
    bridgeStatus: string
    workspace: string
    modsStatus: string
    actionClicks: number
    lastAction: string
    modSelections: number
    lastMod: string
    windowClicks: number
    lastWindowRequest: string
    lastWindowAck: string
    lastError: string
  }

  let {
    diagnostics,
    onPingBridge
  }: {
    diagnostics: DiagnosticsState
    onPingBridge: () => void | Promise<void>
  } = $props()

  let scrollContainer: HTMLDivElement | undefined = $state()
  let autoScroll = $state(true)
  let panelHeight = $state(Number(localStorage.getItem('umb-bottom-panel-height') ?? '240'))
  let splitRatio = $state(Number(localStorage.getItem('umb-bottom-panel-split') ?? '0.5'))
  let consoleCollapsed = $state(localStorage.getItem('umb-bottom-console-collapsed') === 'true')
  let debugCollapsed = $state(localStorage.getItem('umb-bottom-debug-collapsed') === 'true')
  const filters = ['all', 'info', 'warn', 'error'] as const

  function handleScroll() {
    if (!scrollContainer) return
    const { scrollTop, scrollHeight, clientHeight } = scrollContainer
    autoScroll = scrollHeight - scrollTop - clientHeight < 20
  }

  function startHeightResize(event: PointerEvent) {
    const startY = event.clientY
    const startHeight = panelHeight

    function handleMove(moveEvent: PointerEvent) {
      panelHeight = Math.min(420, Math.max(120, startHeight - (moveEvent.clientY - startY)))
      localStorage.setItem('umb-bottom-panel-height', String(panelHeight))
    }

    function handleUp() {
      window.removeEventListener('pointermove', handleMove)
      window.removeEventListener('pointerup', handleUp)
    }

    window.addEventListener('pointermove', handleMove)
    window.addEventListener('pointerup', handleUp)
  }

  function startSplitResize(event: PointerEvent) {
    const container = (event.currentTarget as HTMLElement).parentElement
    if (!container) return

    const rect = container.getBoundingClientRect()
    function handleMove(moveEvent: PointerEvent) {
      const nextRatio = (moveEvent.clientX - rect.left) / rect.width
      splitRatio = Math.min(0.8, Math.max(0.2, nextRatio))
      localStorage.setItem('umb-bottom-panel-split', String(splitRatio))
    }

    function handleUp() {
      window.removeEventListener('pointermove', handleMove)
      window.removeEventListener('pointerup', handleUp)
    }

    window.addEventListener('pointermove', handleMove)
    window.addEventListener('pointerup', handleUp)
  }

  function toggleConsoleCollapsed() {
    consoleCollapsed = !consoleCollapsed
    if (consoleCollapsed && debugCollapsed) {
      debugCollapsed = false
    }
    localStorage.setItem('umb-bottom-console-collapsed', String(consoleCollapsed))
    localStorage.setItem('umb-bottom-debug-collapsed', String(debugCollapsed))
  }

  function toggleDebugCollapsed() {
    debugCollapsed = !debugCollapsed
    if (debugCollapsed && consoleCollapsed) {
      consoleCollapsed = false
    }
    localStorage.setItem('umb-bottom-console-collapsed', String(consoleCollapsed))
    localStorage.setItem('umb-bottom-debug-collapsed', String(debugCollapsed))
  }

  $effect(() => {
    logStore.filtered
    if (autoScroll && scrollContainer) {
      tick().then(() => {
        if (scrollContainer) {
          scrollContainer.scrollTop = scrollContainer.scrollHeight
        }
      })
    }
  })
</script>

<div class="shrink-0 border-t border-border bg-card" style="height: {logStore.drawerOpen ? `${panelHeight}px` : '34px'}">
  <div
    class="h-2 cursor-row-resize border-b border-border bg-background/60 hover:bg-accent/50"
    role="separator"
    aria-label="Resize bottom panel"
    aria-orientation="horizontal"
    onpointerdown={startHeightResize}
  ></div>

  {#if !logStore.drawerOpen}
    <div class="flex h-[26px] items-center justify-between px-3.5">
      <span class="font-mono text-xs text-muted-foreground">bottom panel</span>
      <button
        onclick={() => logStore.toggleDrawer()}
        class="flex items-center gap-1.5 rounded-[7px] px-2.5 py-1 text-xs font-medium text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
      >
        <PanelBottomOpen size={13} />
        Show panel
      </button>
    </div>
  {:else}
    <div class="flex h-[calc(100%-8px)] min-h-0 flex-col">
      <div class="flex items-center justify-between border-b border-border px-3.5 py-2">
        <div class="flex items-center gap-2">
          <span class="text-[12.5px] font-semibold">Bottom Panel</span>
          <span class="font-mono text-xs text-muted-foreground">{logStore.lineCount} log lines</span>
        </div>
        <button
          onclick={() => logStore.toggleDrawer()}
          class="flex h-[26px] w-[26px] items-center justify-center rounded-[7px] text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
        >
          <PanelBottomClose size={13} />
        </button>
      </div>

      <div class="flex min-h-0 flex-1 overflow-hidden">
        <section class="min-h-0 overflow-hidden border-r border-border" style="width: {consoleCollapsed ? '44px' : debugCollapsed ? '100%' : `${splitRatio * 100}%`} ">
          <div class="flex h-full min-h-0 flex-col">
            <div class="flex items-center justify-between border-b border-border px-3 py-2">
              <div class="flex items-center gap-2">
                <button
                  onclick={toggleConsoleCollapsed}
                  class="flex h-[24px] w-[24px] items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
                >
                  {#if consoleCollapsed}
                    <ChevronRight size={14} />
                  {:else}
                    <ChevronLeft size={14} />
                  {/if}
                </button>
                {#if !consoleCollapsed}
                  <span class="text-[12px] font-semibold">Console</span>
                {/if}
              </div>

              {#if !consoleCollapsed}
                <div class="flex items-center gap-1">
                  {#each filters as filterName}
                    <button
                      onclick={() => logStore.setFilter(filterName)}
                      class="rounded-[7px] px-2 py-1 text-[11px] font-medium capitalize transition-colors {logStore.filter === filterName ? 'bg-accent text-foreground' : 'text-muted-foreground hover:bg-accent hover:text-foreground'}"
                    >
                      {filterName}
                    </button>
                  {/each}
                  <button
                    onclick={() => logStore.clear()}
                    class="flex h-[24px] w-[24px] items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
                  >
                    <X size={13} />
                  </button>
                </div>
              {/if}
            </div>

            {#if !consoleCollapsed}
              <div bind:this={scrollContainer} onscroll={handleScroll} class="min-h-0 flex-1 overflow-y-auto px-3.5 py-2">
                {#if logStore.filtered.length === 0}
                  <div class="py-2 font-mono text-xs text-muted-foreground">No log entries yet.</div>
                {:else}
                  {#each logStore.filtered as line}
                    <div class="flex items-baseline gap-3 font-mono text-[11.5px] leading-[1.65]">
                      <span class="shrink-0 text-muted-foreground">{line.timestamp}</span>
                      <span class="inline-flex h-[18px] w-[44px] shrink-0 items-center justify-center rounded-full border text-[10px] font-medium {line.level === 'warn' ? 'border-[hsl(38_92%_50%/.3)] bg-[hsl(38_92%_50%/.14)] text-[hsl(38,92%,32%)]' : line.level === 'error' ? 'border-[hsl(var(--destructive)/.3)] bg-[hsl(var(--destructive)/.12)] text-destructive' : 'border-border bg-muted text-muted-foreground'}">
                        {line.level}
                      </span>
                      <span class="text-foreground">{line.message}</span>
                    </div>
                  {/each}
                {/if}
              </div>
            {/if}
          </div>
        </section>

        {#if !consoleCollapsed && !debugCollapsed}
          <div
            class="w-2 shrink-0 cursor-col-resize bg-background/60 hover:bg-accent/50"
            role="separator"
            aria-label="Resize bottom panel sections"
            aria-orientation="vertical"
            onpointerdown={startSplitResize}
          ></div>
        {/if}

        <section class="min-h-0 overflow-hidden" style="width: {debugCollapsed ? '44px' : consoleCollapsed ? '100%' : `${(1 - splitRatio) * 100}%`} ">
          <div class="flex h-full min-h-0 flex-col">
            <div class="flex items-center justify-between border-b border-border px-3 py-2">
              <div class="flex items-center gap-2">
                <button
                  onclick={toggleDebugCollapsed}
                  class="flex h-[24px] w-[24px] items-center justify-center rounded-md text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
                >
                  {#if debugCollapsed}
                    <ChevronLeft size={14} />
                  {:else}
                    <ChevronRight size={14} />
                  {/if}
                </button>
                {#if !debugCollapsed}
                  <span class="text-[12px] font-semibold">Runtime Debug</span>
                {/if}
              </div>

              {#if !debugCollapsed}
                <button
                  onclick={() => void onPingBridge()}
                  class="flex items-center gap-1 rounded-md border border-input bg-background px-2 py-1 text-[11px] font-medium transition-colors hover:bg-muted"
                >
                  <RefreshCw size={12} />
                  Ping
                </button>
              {/if}
            </div>

            {#if !debugCollapsed}
              <div class="min-h-0 flex-1 overflow-y-auto px-3.5 py-2 font-mono text-[11px] leading-5 text-muted-foreground">
                <div>bridge: <span class="text-foreground">{diagnostics.bridgeStatus}</span></div>
                <div>workspace: <span class="break-all text-foreground">{diagnostics.workspace}</span></div>
                <div>mods: <span class="text-foreground">{diagnostics.modsStatus}</span></div>
                <div>active tab: <span class="text-foreground">{diagnostics.lastAction}</span></div>
                <div>mod selections: <span class="text-foreground">{diagnostics.modSelections}</span> / {diagnostics.lastMod}</div>
                <div>action clicks: <span class="text-foreground">{diagnostics.actionClicks}</span></div>
                <div>window clicks: <span class="text-foreground">{diagnostics.windowClicks}</span> / {diagnostics.lastWindowRequest}</div>
                <div>window ack: <span class="text-foreground">{diagnostics.lastWindowAck}</span></div>
                <div>last error: <span class="break-all text-foreground">{diagnostics.lastError}</span></div>
              </div>
            {/if}
          </div>
        </section>
      </div>
    </div>
  {/if}
</div>