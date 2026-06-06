<script lang="ts">
  import { Hammer, FolderTree, Wand2, ArrowLeftRight, Folder, AlertTriangle } from '@lucide/svelte'
  import { _ } from 'svelte-i18n'
  import { logStore } from '$lib/stores/logs.svelte'
  import type { ModInfo } from '$lib/types/electron'

  let { activeMod }: { activeMod: ModInfo | null } = $props()

  let buildRunning = $state(false)
  let scaffoldRunning = $state(false)
  let cleanupRunning = $state(false)
  let importRunning = $state(false)

  let importModalOpen = $state(false)
  let importSource = $state('')
  let importName = $state('')
  let overwriteModalOpen = $state(false)

  const canImport = $derived(importSource.trim() !== '' && importName.trim() !== '' && !importRunning)

  async function handleBuildClick() {
    if (buildRunning) {
      window.electron.umb.cancelAction()
      return
    }

    const hasOutput = await window.electron.umb.checkArcOutput()
    if (hasOutput) {
      overwriteModalOpen = true
      return
    }

    startBuild()
  }

  function startBuild() {
    overwriteModalOpen = false
    buildRunning = true
    logStore.clear()
    if (!logStore.drawerOpen) {
      logStore.toggleDrawer()
    }

    const mod = activeMod?.name
    window.electron.umb.runAction('build', mod ? [mod] : []).finally(() => {
      buildRunning = false
    })
  }

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

  async function handleCleanup() {
    if (cleanupRunning) return

    cleanupRunning = true
    logStore.clear()
    if (!logStore.drawerOpen) {
      logStore.toggleDrawer()
    }

    try {
      await window.electron.umb.runAction('cleanup')
    } finally {
      cleanupRunning = false
    }
  }

  function openImportModal() {
    importSource = ''
    importName = ''
    importModalOpen = true
  }

  function closeImportModal() {
    if (importRunning) return
    importModalOpen = false
  }

  async function pickImportSource() {
    const folder = await window.electron.umb.selectFolder()
    if (folder) importSource = folder
  }

  async function handleImport() {
    if (!canImport) return

    importRunning = true
    logStore.clear()
    if (!logStore.drawerOpen) {
      logStore.toggleDrawer()
    }

    try {
      await window.electron.umb.runAction('convert', [importSource.trim(), importName.trim()])
      importModalOpen = false
    } finally {
      importRunning = false
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
          <!-- Build button (active) -->
          <div class="rounded-xl border border-border bg-background/75 p-4">
            <div class="flex items-center gap-3">
              <div
                class="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-border"
                style="background: linear-gradient(135deg, hsl(var(--gradient-from) / .10), hsl(var(--gradient-to) / .14)); color: hsl(var(--gradient-from));"
              >
                <Hammer size={18} />
              </div>
              <div class="flex-1 min-w-0">
                <h3 class="text-[13.5px] font-semibold">{$_('build.buildButton')}</h3>
                <p class="text-[12px] text-muted-foreground">{$_('build.buildDescription')}</p>
              </div>
              <button
                onclick={handleBuildClick}
                class="shrink-0 inline-flex items-center gap-2 rounded-lg px-4 py-2 text-[12.5px] font-medium transition-colors {buildRunning
                  ? 'border-0 bg-destructive text-destructive-foreground hover:bg-destructive/90'
                  : 'border border-input bg-background hover:bg-muted'}"
              >
                <Hammer size={14} />
                {buildRunning ? $_('build.stopBuild') : $_('build.buildButton')}
              </button>
            </div>
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

          <!-- Cleanup button (active) -->
          <div class="rounded-xl border border-border bg-background/75 p-4">
            <div class="flex items-center gap-3">
              <div
                class="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-border"
                style="background: linear-gradient(135deg, hsl(var(--gradient-from) / .10), hsl(var(--gradient-to) / .14)); color: hsl(var(--gradient-from));"
              >
                <Wand2 size={18} />
              </div>
              <div class="flex-1 min-w-0">
                <h3 class="text-[13.5px] font-semibold">{$_('build.cleanupButton')}</h3>
                <p class="text-[12px] text-muted-foreground">{$_('build.cleanupDescription')}</p>
              </div>
              <button
                onclick={handleCleanup}
                disabled={cleanupRunning}
                class="shrink-0 inline-flex items-center gap-2 rounded-lg border border-input bg-background px-4 py-2 text-[12.5px] font-medium transition-colors hover:bg-muted disabled:cursor-not-allowed disabled:opacity-60"
              >
                <Wand2 size={14} />
                {cleanupRunning ? $_('build.running') : $_('build.cleanupButton')}
              </button>
            </div>
          </div>

          <!-- Import button (active) -->
          <div class="rounded-xl border border-border bg-background/75 p-4">
            <div class="flex items-center gap-3">
              <div
                class="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-border"
                style="background: linear-gradient(135deg, hsl(var(--gradient-from) / .10), hsl(var(--gradient-to) / .14)); color: hsl(var(--gradient-from));"
              >
                <ArrowLeftRight size={18} />
              </div>
              <div class="flex-1 min-w-0">
                <h3 class="text-[13.5px] font-semibold">{$_('build.importButton')}</h3>
                <p class="text-[12px] text-muted-foreground">{$_('build.importDescription')}</p>
              </div>
              <button
                onclick={openImportModal}
                disabled={importRunning}
                class="shrink-0 inline-flex items-center gap-2 rounded-lg border border-input bg-background px-4 py-2 text-[12.5px] font-medium transition-colors hover:bg-muted disabled:cursor-not-allowed disabled:opacity-60"
              >
                <ArrowLeftRight size={14} />
                {importRunning ? $_('build.running') : $_('build.importButton')}
              </button>
            </div>
          </div>
        </div>
      </div>
    </section>
  </div>
</div>

{#if importModalOpen}
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div
    class="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4"
    onclick={closeImportModal}
    onkeydown={(e) => { if (e.key === 'Escape') closeImportModal() }}
  >
    <!-- svelte-ignore a11y_no_static_element_interactions -->
    <div
      class="w-full max-w-[480px] rounded-xl border border-border bg-popover shadow-2xl overflow-hidden"
      onclick={(e) => e.stopPropagation()}
      onkeydown={(e) => e.stopPropagation()}
    >
      <div class="gradient-strip h-[3px] shrink-0"></div>
      <div class="border-b border-border px-5 py-4 flex items-center gap-3">
        <div
          class="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl border border-border"
          style="background: linear-gradient(135deg, hsl(var(--gradient-from) / .13), hsl(var(--gradient-to) / .16)); color: hsl(var(--gradient-from));"
        >
          <ArrowLeftRight size={18} />
        </div>
        <div class="min-w-0">
          <h2 class="truncate text-sm font-semibold">{$_('build.importModal.title')}</h2>
          <p class="truncate text-[12.5px] text-muted-foreground">{$_('build.importModal.subtitle')}</p>
        </div>
      </div>

      <div class="px-5 py-5 flex flex-col gap-4">
        <!-- Sma5h mod location -->
        <div class="flex flex-col gap-1.5">
          <label for="import-source" class="text-[12.5px] font-medium">{$_('build.importModal.sourceLabel')}</label>
          <div class="flex items-center gap-2">
            <input
              id="import-source"
              type="text"
              readonly
              value={importSource}
              placeholder={$_('build.importModal.sourcePlaceholder')}
              class="flex-1 min-w-0 rounded-lg border border-input bg-background px-3 py-2 text-[12.5px] text-muted-foreground outline-none"
            />
            <button
              onclick={pickImportSource}
              disabled={importRunning}
              class="shrink-0 inline-flex items-center gap-2 rounded-lg border border-input bg-background px-3 py-2 text-[12.5px] font-medium transition-colors hover:bg-muted disabled:cursor-not-allowed disabled:opacity-60"
            >
              <Folder size={14} />
              {$_('build.importModal.browse')}
            </button>
          </div>
        </div>

        <!-- New UMB mod name -->
        <div class="flex flex-col gap-1.5">
          <label for="import-name" class="text-[12.5px] font-medium">{$_('build.importModal.nameLabel')}</label>
          <input
            id="import-name"
            type="text"
            bind:value={importName}
            placeholder={$_('build.importModal.namePlaceholder')}
            disabled={importRunning}
            class="rounded-lg border border-input bg-background px-3 py-2 text-[12.5px] outline-none focus:border-ring disabled:cursor-not-allowed disabled:opacity-60"
          />
        </div>
      </div>

      <div class="border-t border-border px-5 py-4 flex items-center justify-end gap-2">
        <button
          onclick={closeImportModal}
          disabled={importRunning}
          class="inline-flex items-center gap-2 rounded-lg border border-input bg-background px-4 py-2 text-[12.5px] font-medium transition-colors hover:bg-muted disabled:cursor-not-allowed disabled:opacity-60"
        >
          {$_('build.importModal.close')}
        </button>
        <button
          onclick={handleImport}
          disabled={!canImport}
          class="inline-flex items-center gap-2 rounded-lg border border-input bg-background px-4 py-2 text-[12.5px] font-medium transition-colors hover:bg-muted disabled:cursor-not-allowed disabled:opacity-60"
        >
          <ArrowLeftRight size={14} />
          {importRunning ? $_('build.running') : $_('build.importModal.import')}
        </button>
      </div>
    </div>
  </div>
{/if}

{#if overwriteModalOpen}
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div
    class="fixed inset-0 z-50 flex items-center justify-center bg-black/50 p-4"
    onclick={() => { overwriteModalOpen = false }}
    onkeydown={(e) => { if (e.key === 'Escape') overwriteModalOpen = false }}
  >
    <!-- svelte-ignore a11y_no_static_element_interactions -->
    <div
      class="w-full max-w-[420px] rounded-xl border border-border bg-popover shadow-2xl overflow-hidden"
      onclick={(e) => e.stopPropagation()}
      onkeydown={(e) => e.stopPropagation()}
    >
      <div class="h-[3px] shrink-0" style="background: hsl(var(--destructive));"></div>
      <div class="border-b border-border px-5 py-4 flex items-center gap-3">
        <div
          class="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl border border-border"
          style="background: hsl(var(--destructive) / .12); color: hsl(var(--destructive));"
        >
          <AlertTriangle size={18} />
        </div>
        <div class="min-w-0">
          <h2 class="truncate text-sm font-semibold">{$_('build.overwriteModal.title')}</h2>
          <p class="truncate text-[12.5px] text-muted-foreground">{$_('build.overwriteModal.subtitle')}</p>
        </div>
      </div>

      <div class="px-5 py-5">
        <p class="text-[12.5px] text-muted-foreground">{$_('build.overwriteModal.description')}</p>
      </div>

      <div class="border-t border-border px-5 py-4 flex items-center justify-end gap-2">
        <button
          onclick={() => { overwriteModalOpen = false }}
          class="inline-flex items-center gap-2 rounded-lg border border-input bg-background px-4 py-2 text-[12.5px] font-medium transition-colors hover:bg-muted"
        >
          {$_('build.overwriteModal.cancel')}
        </button>
        <button
          onclick={startBuild}
          class="inline-flex items-center gap-2 rounded-lg border-0 px-4 py-2 text-[12.5px] font-medium text-white transition-colors bg-destructive hover:bg-destructive/90"
        >
          <Hammer size={14} />
          {$_('build.overwriteModal.overwrite')}
        </button>
      </div>
    </div>
  </div>
{/if}
