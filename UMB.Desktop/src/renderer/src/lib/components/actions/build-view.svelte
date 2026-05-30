<script lang="ts">
  import { Hammer, FolderTree } from '@lucide/svelte'
  import { _ } from 'svelte-i18n'
  import { logStore } from '$lib/stores/logs.svelte'

  let scaffoldRunning = $state(false)

  async function handleScaffold() {
    if (scaffoldRunning) return

    scaffoldRunning = true
    logStore.clear()
    if (!logStore.drawerOpen) {
      logStore.toggleDrawer()
    }

    try {
      await window.electron.umb.runAction('scaffold')
    } finally {
      scaffoldRunning = false
    }
  }
</script>

<div class="flex-1 overflow-hidden">
  <div class="flex h-full min-h-0 flex-col">
    <section class="flex h-full min-h-0 flex-1 flex-col border border-border bg-card overflow-hidden">
      <div class="gradient-strip h-[3px] shrink-0"></div>
      <div class="shrink-0 border-b border-border px-4 py-3 flex items-center gap-3">
        <div
          class="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl border border-border"
          style="background: linear-gradient(135deg, hsl(var(--gradient-from) / .13), hsl(var(--gradient-to) / .16)); color: hsl(var(--gradient-from));"
        >
          <Hammer size={18} />
        </div>
        <div class="min-w-0">
          <h2 class="truncate text-sm font-semibold">{$_('build.title')}</h2>
          <p class="truncate text-[12.5px] text-muted-foreground">{$_('build.subtitle')}</p>
        </div>
      </div>

      <div class="min-h-0 flex-1 overflow-auto p-6">
        <div class="flex flex-col gap-4 max-w-[520px]">
          <!-- Build button (disabled) -->
          <div class="rounded-xl border border-border bg-background/75 p-4 opacity-50">
            <div class="flex items-center gap-3">
              <div class="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-border bg-muted/50">
                <Hammer size={18} class="text-muted-foreground" />
              </div>
              <div class="flex-1 min-w-0">
                <h3 class="text-[13.5px] font-semibold">{$_('build.buildButton')}</h3>
                <p class="text-[12px] text-muted-foreground">{$_('build.buildDescription')}</p>
              </div>
              <button
                disabled
                class="shrink-0 inline-flex items-center gap-2 rounded-lg border border-input bg-background px-4 py-2 text-[12.5px] font-medium cursor-not-allowed opacity-60"
              >
                <Hammer size={14} />
                {$_('build.buildButton')}
              </button>
            </div>
            <p class="mt-2 text-[11.5px] text-muted-foreground italic">{$_('build.buildDisabled')}</p>
          </div>

          <!-- Scaffold button (active) -->
          <div class="rounded-xl border border-border bg-background/75 p-4">
            <div class="flex items-center gap-3">
              <div
                class="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-border"
                style="background: linear-gradient(135deg, hsl(var(--gradient-from) / .10), hsl(var(--gradient-to) / .14)); color: hsl(var(--gradient-from));"
              >
                <FolderTree size={18} />
              </div>
              <div class="flex-1 min-w-0">
                <h3 class="text-[13.5px] font-semibold">{$_('build.scaffoldButton')}</h3>
                <p class="text-[12px] text-muted-foreground">{$_('build.scaffoldDescription')}</p>
              </div>
              <button
                onclick={handleScaffold}
                disabled={scaffoldRunning}
                class="shrink-0 inline-flex items-center gap-2 rounded-lg border border-input bg-background px-4 py-2 text-[12.5px] font-medium transition-colors hover:bg-muted disabled:cursor-not-allowed disabled:opacity-60"
              >
                <FolderTree size={14} />
                {scaffoldRunning ? $_('build.running') : $_('build.scaffoldButton')}
              </button>
            </div>
          </div>
        </div>
      </div>
    </section>
  </div>
</div>
