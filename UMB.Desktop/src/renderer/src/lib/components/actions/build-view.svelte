<script lang="ts">
  import Folder from '@lucide/svelte/icons/folder'
  import AlertTriangle from '@lucide/svelte/icons/alert-triangle'
  import LoaderCircle from '@lucide/svelte/icons/loader-circle'
  import CircleCheck from '@lucide/svelte/icons/circle-check'
  import CircleX from '@lucide/svelte/icons/circle-x'
  import RotateCw from '@lucide/svelte/icons/rotate-cw'
  import { _ } from 'svelte-i18n'
  import { logStore } from '$lib/stores/logs.svelte'
  import ActionHeader from '$lib/components/ui/action-header.svelte'
  import CommandRow from '$lib/components/ui/command-row.svelte'
  import Modal from '$lib/components/ui/modal.svelte'
  import { settingsStore } from '$lib/stores/settings.svelte'
  import { formatDb } from '$lib/volume'
  import type { CleanupItem, ModInfo, SetupStatus, SuspiciousVolumeTrack } from '$lib/types/electron'

  let { activeMod }: { activeMod: ModInfo | null } = $props()

  let buildRunning = $state(false)
  let scaffoldRunning = $state(false)
  let cleanupRunning = $state(false)
  let importRunning = $state(false)

  let importModalOpen = $state(false)
  let importSource = $state('')
  let importName = $state('')
  let overwriteModalOpen = $state(false)
  let volumeWarnings = $state<SuspiciousVolumeTrack[]>([])

  const canImport = $derived(importSource.trim() !== '' && importName.trim() !== '' && !importRunning)
  const anyRunning = $derived(buildRunning || scaffoldRunning || cleanupRunning || importRunning)

  // Facts for the strip under the commands; re-read when settings change or an action finishes.
  let hasOutput = $state(false)
  let globalVolumeDb = $state<number | null>(null)
  $effect(() => {
    void settingsStore.version
    if (anyRunning) return
    void window.electron.umb.checkArcOutput().then((v) => { hasOutput = v })
    void window.electron.umb.getAppSettings().then((s) => { globalVolumeDb = s.globalVolumeDb })
  })

  let setup = $state<SetupStatus | null>(null)
  let setupRunning = $state(false)

  async function checkSetup() {
    setupRunning = true
    try {
      setup = await window.electron.umb.checkSetup()
    } finally {
      setupRunning = false
    }
  }

  $effect(() => {
    void checkSetup()
  })

  // What cleanup would remove from the active mod; re-read on mod switch, after every action and on window
  // focus (files may have changed outside the app).
  let cleanupItems = $state<CleanupItem[]>([])
  let focusCount = $state(0)
  let cleanupModalOpen = $state(false)
  const cleanupGroups = $derived(
    [...new Set(cleanupItems.map((item) => item.file))].map((file) => [file, cleanupItems.filter((item) => item.file === file)] as const)
  )
  $effect(() => {
    const path = activeMod?.path
    void focusCount
    if (!path || anyRunning) {
      cleanupItems = []
      return
    }
    let cancelled = false
    void window.electron.umb.previewCleanup(path).then((items) => { if (!cancelled) cleanupItems = items })
    return () => { cancelled = true }
  })

  async function handleBuildClick() {
    if (buildRunning) {
      window.electron.umb.cancelAction()
      return
    }

    const warnings = await window.electron.umb.checkBuildVolumes(activeMod?.name ?? null).catch(() => [])
    if (warnings.length > 0) {
      volumeWarnings = warnings
      return
    }

    checkOutputAndBuild()
  }

  async function checkOutputAndBuild() {
    volumeWarnings = []
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

  async function openCleanupModal() {
    if (!activeMod) return
    cleanupItems = await window.electron.umb.previewCleanup(activeMod.path)
    cleanupModalOpen = true
  }

  async function handleCleanup() {
    if (cleanupRunning || !activeMod) return

    cleanupModalOpen = false
    cleanupRunning = true
    logStore.clear()
    if (!logStore.drawerOpen) {
      logStore.toggleDrawer()
    }

    try {
      await window.electron.umb.runCleanup(activeMod.path)
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

<svelte:window onfocus={() => focusCount++} />

{#snippet sectionLabel(text: string)}
  <div class="text-[10.5px] font-semibold uppercase tracking-[.1em] text-dim">{text}</div>
{/snippet}

{#snippet status(ok: boolean | undefined)}
  {#if setupRunning || ok === undefined}
    <span class="flex items-center gap-1.5 text-[12px] text-dim"><LoaderCircle size={13} class="animate-spin" />{$_('build.setup.checking')}</span>
  {:else if ok}
    <span class="flex items-center gap-1.5 text-[12px] text-primary"><CircleCheck size={13} />{$_('build.setup.ready')}</span>
  {:else}
    <span class="flex items-center gap-1.5 text-[12px] text-destructive"><CircleX size={13} />{$_('build.setup.missing')}</span>
  {/if}
{/snippet}

<ActionHeader title={$_('build.title')} subtitle={$_('build.subtitle')} />

<div class="action-pane grid grid-cols-2 items-start gap-6">
  <section class="min-w-0">
    <div class="mb-2 flex h-6 items-center">{@render sectionLabel($_('build.sections.build'))}</div>
    <CommandRow
      name={$_('build.buildButton')}
      description={$_('build.buildDescription')}
      note={hasOutput ? $_('build.outputNotEmpty') : ''}
      tone="warn"
    >
      <button onclick={handleBuildClick} disabled={anyRunning && !buildRunning} class="btn-primary">
        {buildRunning ? $_('build.stopBuild') : $_('build.buildButton')}
      </button>
    </CommandRow>

    <CommandRow name={$_('build.scaffoldButton')} description={$_('build.scaffoldDescription')}>
      <button onclick={handleScaffold} disabled={anyRunning} class="btn">
        {#if scaffoldRunning}<LoaderCircle size={12} class="animate-spin" />{:else}{$_('build.scaffoldButton')}{/if}
      </button>
    </CommandRow>

    <CommandRow
      name={$_('build.cleanupButton')}
      description={$_('build.cleanupDescription')}
      note={$_('build.destructive')}
      tone="err"
      warning={cleanupItems.length > 0 ? $_('build.invalidEntries', { values: { count: cleanupItems.length } }) : ''}
    >
      <button onclick={openCleanupModal} disabled={anyRunning || !activeMod} class="btn-danger">
        {#if cleanupRunning}<LoaderCircle size={12} class="animate-spin" />{:else}{$_('build.cleanupButton')}{/if}
      </button>
    </CommandRow>

    <CommandRow name={$_('build.importButton')} description={$_('build.importDescription')} note={$_('build.importNote')}>
      <button onclick={openImportModal} disabled={anyRunning} class="btn">
        {#if importRunning}<LoaderCircle size={12} class="animate-spin" />{:else}{$_('build.importButton')}…{/if}
      </button>
    </CommandRow>

    <div class="mt-3 grid grid-cols-[repeat(3,minmax(0,180px))] gap-[18px]">
      <div>
        <div class="text-[10.5px] font-semibold uppercase tracking-[.1em] text-dim">{$_('build.facts.target')}</div>
        <div class="mt-[5px] text-[13.5px] font-medium">ArcOutput/</div>
      </div>
      {#if globalVolumeDb !== null}
        <div>
          <div class="text-[10.5px] font-semibold uppercase tracking-[.1em] text-dim">{$_('build.facts.globalVolume')}</div>
          <div class="mt-[5px] text-[13.5px] font-medium">{formatDb(globalVolumeDb)}</div>
        </div>
      {/if}
    </div>
  </section>

  <section class="min-w-0">
    <div class="mb-2 flex h-6 items-center justify-between">
      {@render sectionLabel($_('build.sections.setup'))}
      <button onclick={checkSetup} disabled={setupRunning} class="btn h-6">
        <RotateCw size={12} />
        {$_('build.setup.recheck')}
      </button>
    </div>

    {#each ['uv', 'pymusiclooper', 'ffmpeg'] as const as tool (tool)}
      <CommandRow
        name={tool}
        description={$_(`build.setup.${tool}Description`)}
        note={setup?.[tool].version ?? (setup ? $_('build.setup.toolNotFound') : '')}
        tone={setup && !setup[tool].ok ? 'err' : 'dim'}
      >
        {@render status(setup?.[tool].ok)}
      </CommandRow>
    {/each}

    <CommandRow
      name={$_('build.setup.resourcesName')}
      description={setup ? $_('build.setup.resourcesDescription', { values: { path: setup.resources.path } }) : ''}
      note={setup && !setup.resources.ok ? $_('build.setup.resourcesMissing', { values: { count: setup.resources.missing.length } }) : ''}
      tone="err"
    >
      {@render status(setup?.resources.ok)}
    </CommandRow>
  </section>
</div>

{#if importModalOpen}
  <Modal maxWidth="480px">
    <div class="border-b border-border px-5 py-4">
      <h2 class="text-[13.5px] font-semibold">{$_('build.importModal.title')}</h2>
    </div>

    <div class="flex flex-col gap-4 px-5 py-5">
      <div class="flex flex-col gap-1.5">
        <label for="import-source" class="text-[12.5px] font-medium">{$_('build.importModal.sourceLabel')}</label>
        <div class="flex items-center gap-2">
          <input
            id="import-source"
            type="text"
            readonly
            value={importSource}
            placeholder={$_('build.importModal.sourcePlaceholder')}
            class="field min-w-0 flex-1 text-muted-foreground"
          />
          <button onclick={pickImportSource} disabled={importRunning} class="btn h-7 shrink-0">
            <Folder size={13} />
            {$_('build.importModal.browse')}
          </button>
        </div>
      </div>

      <div class="flex flex-col gap-1.5">
        <label for="import-name" class="text-[12.5px] font-medium">{$_('build.importModal.nameLabel')}</label>
        <input
          id="import-name"
          type="text"
          bind:value={importName}
          placeholder={$_('build.importModal.namePlaceholder')}
          disabled={importRunning}
          class="field"
        />
      </div>
    </div>

    <div class="flex items-center justify-end gap-2 border-t border-border px-5 py-4">
      <button onclick={closeImportModal} disabled={importRunning} class="btn">
        {$_('build.importModal.close')}
      </button>
      <button onclick={handleImport} disabled={!canImport} class="btn-primary">
        {importRunning ? $_('build.running') : $_('build.importModal.import')}
      </button>
    </div>
  </Modal>
{/if}

{#if cleanupModalOpen}
  <Modal maxWidth="560px" maxHeight="80vh">
    <div class="border-b border-border px-5 py-4">
      <h2 class="flex items-center gap-2 text-[13.5px] font-semibold">
        <AlertTriangle size={15} class="text-warn" />
        {$_('build.cleanupModal.title', { values: { mod: activeMod?.name ?? '' } })}
      </h2>
      <p class="mt-[3px] text-[12.5px] text-muted-foreground">
        {cleanupItems.length > 0
          ? $_('build.cleanupModal.description', { values: { count: cleanupItems.length } })
          : $_('build.cleanupModal.empty')}
      </p>
    </div>

    {#if cleanupItems.length > 0}
      <div class="min-h-0 flex-1 overflow-auto px-5 py-4">
        {#each cleanupGroups as [file, items] (file)}
          <div class="mb-3 last:mb-0">
            <div class="mb-1 font-mono text-[11.5px] text-dim">{file}</div>
            <ul class="rounded border border-border text-[12px]">
              {#each items as item, i (i)}
                <li class="break-all border-b border-line2 px-3 py-1.5 last:border-b-0">{item.entry}</li>
              {/each}
            </ul>
          </div>
        {/each}
      </div>
    {/if}

    <div class="flex items-center justify-end gap-2 border-t border-border px-5 py-4">
      <button onclick={() => { cleanupModalOpen = false }} class="btn">
        {$_('build.cleanupModal.cancel')}
      </button>
      {#if cleanupItems.length > 0}
        <button onclick={handleCleanup} class="btn-danger">
          {$_('build.cleanupModal.confirm', { values: { count: cleanupItems.length } })}
        </button>
      {/if}
    </div>
  </Modal>
{/if}

{#if overwriteModalOpen}
  <Modal maxWidth="420px">
    <div class="border-b border-border px-5 py-4">
      <h2 class="flex items-center gap-2 text-[13.5px] font-semibold">
        <AlertTriangle size={15} class="text-warn" />
        {$_('build.overwriteModal.title')}
      </h2>
      <p class="mt-[3px] text-[12.5px] text-muted-foreground">{$_('build.overwriteModal.subtitle')}</p>
    </div>

    <p class="px-5 py-5 text-[12.5px] text-muted-foreground">{$_('build.overwriteModal.description')}</p>

    <div class="flex items-center justify-end gap-2 border-t border-border px-5 py-4">
      <button onclick={() => { overwriteModalOpen = false }} class="btn">
        {$_('build.overwriteModal.cancel')}
      </button>
      <button onclick={startBuild} class="btn-danger">
        {$_('build.overwriteModal.overwrite')}
      </button>
    </div>
  </Modal>
{/if}

{#if volumeWarnings.length > 0}
  <Modal maxWidth="520px">
    <div class="flex flex-col gap-3 px-5 py-4">
      <h3 class="flex items-center gap-2 text-[13.5px] font-semibold">
        <AlertTriangle size={15} class="text-warn" />
        {$_('build.volumeModal.title')}
      </h3>
      <p class="text-[12.5px] text-muted-foreground">
        {$_('build.volumeModal.subtitle', { values: { count: volumeWarnings.length } })}
        {$_('build.volumeModal.description')}
      </p>
      <ul class="max-h-[240px] overflow-auto rounded border border-border text-[12px]">
        {#each volumeWarnings as track (`${track.modName}/${track.seriesName}/${track.filename}`)}
          <li class="flex items-center gap-3 border-b border-line2 px-3 py-1.5 last:border-b-0">
            <span class="min-w-0 flex-1 truncate" title={track.filename}>
              <span class="text-muted-foreground">{track.seriesName} /</span> {track.title || track.filename}
            </span>
            <span class="shrink-0 tabular-nums">{formatDb(track.effectiveDb)}</span>
          </li>
        {/each}
      </ul>
    </div>
    <div class="flex justify-end gap-2 border-t border-border px-5 py-4">
      <button onclick={() => { volumeWarnings = [] }} class="btn">
        {$_('build.volumeModal.cancel')}
      </button>
      <button onclick={checkOutputAndBuild} class="btn-danger">
        {$_('build.volumeModal.build')}
      </button>
    </div>
  </Modal>
{/if}
