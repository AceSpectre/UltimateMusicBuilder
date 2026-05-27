<script lang="ts">
  import { Minus, Square, X, Search, Settings, FolderTree, ChevronDown, Folder } from '@lucide/svelte'
  import { modsStore } from '$lib/stores/mods.svelte'
  import { themeStore } from '$lib/stores/theme.svelte'

  let { onOpenSearch = () => {}, onOpenModPicker = () => {} }: {
    onOpenSearch?: () => void
    onOpenModPicker?: () => void
  } = $props()
</script>

<div class="drag-region flex flex-col shrink-0">
  <!-- Gradient strip -->
  <div class="h-[2px] gradient-strip"></div>

  <!-- App bar -->
  <div class="h-14 flex items-center gap-3 px-4 bg-card border-b border-border">
    <!-- Brand -->
    <div class="flex items-center gap-2.5 no-drag">
      <div
        class="w-[30px] h-[30px] rounded-lg gradient-bg flex items-center justify-center text-white font-bold text-sm tracking-wider"
        style="box-shadow: 0 4px 12px -2px hsl(var(--gradient-from) / .4);"
      >
        U
      </div>
      <div class="flex flex-col leading-tight">
        <span class="font-bold text-sm tracking-[.02em]">Ultimate Music Builder</span>
        <span class="font-mono text-[10.5px] text-muted-foreground">v0.8.2 · electron</span>
      </div>
    </div>

    <!-- Mod picker -->
    <button
      onclick={onOpenModPicker}
      class="no-drag flex items-center gap-2 w-[280px] h-9 px-3 rounded-lg border border-input bg-background hover:bg-muted transition-colors"
    >
      <Folder size={14} class="text-muted-foreground shrink-0" />
      <div class="flex flex-col items-start leading-tight flex-1 min-w-0">
        <span class="font-mono text-[10.5px] text-muted-foreground">Mods/MusicMods/</span>
        <span class="text-[13px] font-semibold truncate w-full text-left">
          {modsStore.activeMod?.name ?? 'No mod selected'}
        </span>
      </div>
      <ChevronDown size={14} class="text-muted-foreground shrink-0" />
    </button>

    <!-- Global search -->
    <button
      onclick={onOpenSearch}
      class="no-drag flex items-center gap-2 flex-1 max-w-[360px] h-9 px-3 rounded-lg border border-input bg-background hover:bg-muted transition-colors"
    >
      <Search size={14} class="text-muted-foreground" />
      <span class="text-[13.5px] text-muted-foreground flex-1 text-left">Search actions, series, tracks…</span>
      <kbd class="inline-flex items-center px-1.5 py-0.5 rounded border border-border bg-muted font-mono text-[11px] text-muted-foreground">
        Ctrl+K
      </kbd>
    </button>

    <div class="flex-1"></div>

    <!-- Status badges -->
    <div class="no-drag flex items-center gap-1.5">
      <span class="inline-flex items-center gap-1 px-2 h-[22px] rounded-full border border-border bg-muted text-[11px] font-medium text-muted-foreground tracking-[.02em]">
        <FolderTree size={11} />
        {modsStore.activeMod ? '—' : '0'} series
      </span>
      <span class="inline-flex items-center gap-1 px-2 h-[22px] rounded-full border border-border bg-muted text-[11px] font-medium text-muted-foreground tracking-[.02em]">
        — tracks
      </span>
    </div>

    <!-- Settings -->
    <button
      onclick={() => themeStore.toggle()}
      class="no-drag w-8 h-8 flex items-center justify-center rounded-lg hover:bg-accent transition-colors"
    >
      <Settings size={15} class="text-muted-foreground" />
    </button>

    <!-- Window controls -->
    <div class="no-drag flex items-center ml-2">
      <button
        onclick={() => window.electron.umb.windowMinimize()}
        class="w-8 h-8 flex items-center justify-center rounded-lg hover:bg-accent transition-colors"
      >
        <Minus size={14} class="text-muted-foreground" />
      </button>
      <button
        onclick={() => window.electron.umb.windowMaximize()}
        class="w-8 h-8 flex items-center justify-center rounded-lg hover:bg-accent transition-colors"
      >
        <Square size={12} class="text-muted-foreground" />
      </button>
      <button
        onclick={() => window.electron.umb.windowClose()}
        class="w-8 h-8 flex items-center justify-center rounded-lg hover:bg-destructive/10 hover:text-destructive transition-colors"
      >
        <X size={14} class="text-muted-foreground" />
      </button>
    </div>
  </div>
</div>
