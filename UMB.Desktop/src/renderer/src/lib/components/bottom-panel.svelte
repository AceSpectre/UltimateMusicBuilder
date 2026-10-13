<script lang="ts">
  import PanelBottomClose from '@lucide/svelte/icons/panel-bottom-close'
  import PanelBottomOpen from '@lucide/svelte/icons/panel-bottom-open'
  import RefreshCw from '@lucide/svelte/icons/refresh-cw'
  import X from '@lucide/svelte/icons/x'
  import ChevronLeft from '@lucide/svelte/icons/chevron-left'
  import ChevronRight from '@lucide/svelte/icons/chevron-right'
  import { tick } from 'svelte'
  import { _ } from 'svelte-i18n'
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
  let panelHeight = $state(Number(localStorage.getItem('umb-bottom-panel-height') ?? '180'))
  let splitRatio = $state(Number(localStorage.getItem('umb-bottom-panel-split') ?? '0.5'))
  let debugCollapsed = $state(localStorage.getItem('umb-bottom-debug-collapsed') === 'true')
  const iconButton =
    'flex h-[22px] w-[22px] shrink-0 items-center justify-center rounded text-dim transition-colors duration-100 hover:bg-accent hover:text-foreground'
  const filters = ['all', 'info', 'warn', 'error'] as const
  const levelClass = { info: 'text-sec', warn: 'text-warn', error: 'text-destructive' }

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

  function toggleDebugCollapsed() {
    debugCollapsed = !debugCollapsed
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

<div class="surface-grain relative flex shrink-0 flex-col border-t border-border bg-rail" style="height: {logStore.drawerOpen ? `${panelHeight}px` : '30px'}">
  {#if logStore.drawerOpen}
    <div
      class="absolute inset-x-0 -top-[3px] z-10 h-[5px] cursor-row-resize hover:bg-primary/30"
      role="separator"
      aria-label={$_('bottomPanel.resizePanel')}
      aria-orientation="horizontal"
      onpointerdown={startHeightResize}
    ></div>
  {/if}

  <div class="flex h-[30px] shrink-0 items-center gap-2.5 border-b border-line2 px-3.5 text-[12px] text-dim">
    <span class="font-semibold text-foreground">{$_('bottomPanel.console')}</span>
    <span>{$_('bottomPanel.logLines', { values: { count: logStore.lineCount } })}</span>
    <span class="flex-1"></span>
    {#if logStore.drawerOpen}
      {#each filters as filterName}
        <button
          onclick={() => logStore.setFilter(filterName)}
          class="rounded-sm px-[7px] py-0.5 transition-colors duration-100 {logStore.filter === filterName ? 'bg-secondary text-secondary-foreground' : 'hover:text-foreground'}"
        >
          {$_('bottomPanel.filters.' + filterName)}
        </button>
      {/each}
      <button onclick={() => logStore.clear()} class={iconButton} title={$_('bottomPanel.clear')}>
        <X size={13} />
      </button>
      <button onclick={() => logStore.toggleDrawer()} class={iconButton} title={$_('bottomPanel.hide')}>
        <PanelBottomClose size={13} />
      </button>
    {:else}
      <button onclick={() => logStore.toggleDrawer()} class="flex items-center gap-1.5 rounded px-1.5 py-0.5 transition-colors duration-100 hover:bg-accent hover:text-foreground">
        <PanelBottomOpen size={13} />
        {$_('bottomPanel.show')}
      </button>
    {/if}
  </div>

  {#if logStore.drawerOpen}
    <div class="flex min-h-0 flex-1 overflow-hidden">
      <section class="min-h-0 overflow-hidden" style="width: {debugCollapsed ? '100%' : `${splitRatio * 100}%`}">
        <div bind:this={scrollContainer} onscroll={handleScroll} class="h-full overflow-y-auto px-3.5 py-1.5 font-mono text-[11.5px] leading-[1.75]">
          {#if logStore.filtered.length === 0}
            <div class="text-dim">{$_('bottomPanel.noLogs')}</div>
          {:else}
            {#each logStore.filtered as line}
              <div class="flex gap-3">
                <span class="shrink-0 text-dim">{line.timestamp}</span>
                <span class="w-10 shrink-0 {levelClass[line.level] ?? 'text-dim'}">{line.level}</span>
                <span class="min-w-0 break-words">{line.message}</span>
              </div>
            {/each}
          {/if}
        </div>
      </section>

      {#if !debugCollapsed}
        <div
          class="w-[5px] shrink-0 cursor-col-resize border-l border-line2 hover:bg-primary/30"
          role="separator"
          aria-label={$_('bottomPanel.resizeSections')}
          aria-orientation="vertical"
          onpointerdown={startSplitResize}
        ></div>
      {/if}

      <section class="flex min-h-0 shrink-0 flex-col overflow-hidden {debugCollapsed ? 'border-l border-line2' : ''}" style="width: {debugCollapsed ? '34px' : `${(1 - splitRatio) * 100}%`}">
        <div class="flex h-[30px] shrink-0 items-center gap-2 px-2 text-[12px]">
          <button onclick={toggleDebugCollapsed} class={iconButton} title={$_('bottomPanel.runtimeDebug')}>
            {#if debugCollapsed}
              <ChevronLeft size={14} />
            {:else}
              <ChevronRight size={14} />
            {/if}
          </button>
          {#if !debugCollapsed}
            <span class="font-semibold">{$_('bottomPanel.runtimeDebug')}</span>
            <span class="flex-1"></span>
            <button onclick={() => void onPingBridge()} class="btn h-[22px] px-2 text-[11px]">
              <RefreshCw size={12} />
              {$_('bottomPanel.ping')}
            </button>
          {/if}
        </div>

        {#if !debugCollapsed}
          <div class="min-h-0 flex-1 overflow-y-auto px-3.5 pb-2 text-[11.5px] leading-5 text-dim">
            <div>{$_('bottomPanel.debug.bridge')} <span class="text-foreground">{diagnostics.bridgeStatus}</span></div>
            <div>{$_('bottomPanel.debug.workspace')} <span class="break-all text-foreground">{diagnostics.workspace}</span></div>
            <div>{$_('bottomPanel.debug.mods')} <span class="text-foreground">{diagnostics.modsStatus}</span></div>
            <div>{$_('bottomPanel.debug.activeTab')} <span class="text-foreground">{diagnostics.lastAction}</span></div>
            <div>{$_('bottomPanel.debug.modSelections')} <span class="text-foreground">{diagnostics.modSelections}</span> / {diagnostics.lastMod}</div>
            <div>{$_('bottomPanel.debug.actionClicks')} <span class="text-foreground">{diagnostics.actionClicks}</span></div>
            <div>{$_('bottomPanel.debug.windowClicks')} <span class="text-foreground">{diagnostics.windowClicks}</span> / {diagnostics.lastWindowRequest}</div>
            <div>{$_('bottomPanel.debug.windowAck')} <span class="text-foreground">{diagnostics.lastWindowAck}</span></div>
            <div>{$_('bottomPanel.debug.lastError')} <span class="break-all text-foreground">{diagnostics.lastError}</span></div>
          </div>
        {/if}
      </section>
    </div>
  {/if}
</div>
