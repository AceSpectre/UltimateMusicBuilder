<script lang="ts">
  import { onMount } from 'svelte'
  import AppBar from '$lib/components/app-bar.svelte'
  import Sidebar from '$lib/components/sidebar.svelte'
  import LogDrawer from '$lib/components/log-drawer.svelte'
  import CommandPalette from '$lib/components/command-palette.svelte'
  import { logStore } from '$lib/stores/logs.svelte'
  import { modsStore } from '$lib/stores/mods.svelte'
  import { sidebarStore } from '$lib/stores/sidebar.svelte'

  let commandPaletteOpen = $state(false)

  onMount(() => {
    modsStore.load()

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
        sidebarStore.toggle()
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
    onOpenSearch={() => commandPaletteOpen = true}
    onOpenModPicker={() => {}}
  />

  <div class="flex flex-1 overflow-hidden">
    <Sidebar />

    <!-- Main content area (placeholder) -->
    <main class="flex-1 flex flex-col overflow-hidden bg-background">
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
          <h2 class="text-base font-semibold">Select an action</h2>
          <p class="text-[13.5px] text-muted-foreground">
            Pick a tab in the sidebar to run that action, or press
            <kbd class="inline-flex items-center px-1.5 py-0.5 mx-0.5 rounded border border-border bg-muted font-mono text-[11px]">Ctrl+K</kbd>
            to search.
          </p>
        </div>
      </div>
    </main>
  </div>

  <LogDrawer />
</div>

<CommandPalette bind:open={commandPaletteOpen} />
