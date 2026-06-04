<script lang="ts">
  import { PanelBottomOpen, PanelBottomClose, Filter, X } from '@lucide/svelte'
  import { logStore } from '$lib/stores/logs.svelte'
  import { tick } from 'svelte'

  let scrollContainer: HTMLDivElement | undefined = $state()
  let autoScroll = $state(true)

  function handleScroll() {
    if (!scrollContainer) return
    const { scrollTop, scrollHeight, clientHeight } = scrollContainer
    autoScroll = scrollHeight - scrollTop - clientHeight < 20
  }

  $effect(() => {
    logStore.displayed
    if (autoScroll && scrollContainer) {
      tick().then(() => {
        scrollContainer!.scrollTop = scrollContainer!.scrollHeight
      })
    }
  })

  const filters = ['all', 'info', 'warn', 'error'] as const
</script>

<div class="shrink-0 border-t border-border bg-card">
  {#if !logStore.drawerOpen}
    <!-- Collapsed -->
    <div class="h-[30px] flex items-center justify-between px-3.5">
      <span class="font-mono text-xs text-muted-foreground">
        log · {logStore.lineCount} lines
      </span>
      <button
        onclick={() => logStore.toggleDrawer()}
        class="flex items-center gap-1.5 h-[26px] px-2.5 rounded-[7px] text-xs font-medium text-muted-foreground hover:bg-accent hover:text-foreground transition-colors"
      >
        <PanelBottomOpen size={13} />
        Show log
      </button>
    </div>
  {:else}
    <!-- Expanded header -->
    <div class="h-9 flex items-center justify-between px-3.5 border-b border-border">
      <div class="flex items-center gap-2.5">
        <span class="text-[12.5px] font-semibold">Console</span>
        <span class="font-mono text-xs text-muted-foreground">{logStore.lineCount} lines</span>
      </div>
      <div class="flex items-center gap-1">
        {#each filters as f}
          <button
            onclick={() => logStore.setFilter(f)}
            class="h-[26px] px-2.5 rounded-[7px] text-[12px] font-medium capitalize transition-colors
              {logStore.filter === f
                ? 'bg-accent text-foreground'
                : 'text-muted-foreground hover:bg-accent hover:text-foreground'}"
          >
            {f}
          </button>
        {/each}

        <div class="w-2"></div>

        <button
          onclick={() => logStore.clear()}
          class="w-[26px] h-[26px] flex items-center justify-center rounded-[7px] text-muted-foreground hover:bg-accent hover:text-foreground transition-colors"
        >
          <X size={13} />
        </button>
        <button
          onclick={() => logStore.toggleDrawer()}
          class="w-[26px] h-[26px] flex items-center justify-center rounded-[7px] text-muted-foreground hover:bg-accent hover:text-foreground transition-colors"
        >
          <PanelBottomClose size={13} />
        </button>
      </div>
    </div>

    <!-- Log body -->
    <div
      bind:this={scrollContainer}
      onscroll={handleScroll}
      class="px-3.5 py-2 max-h-[200px] overflow-y-auto"
    >
      {#if logStore.displayed.length === 0}
        <div class="text-xs text-muted-foreground font-mono py-2">No log entries yet. Run an action to see output here.</div>
      {:else}
        {#each logStore.displayed as line}
          <div class="flex items-baseline gap-3 font-mono text-[11.5px] leading-[1.65]">
            <span class="text-muted-foreground shrink-0">{line.timestamp}</span>
            <span
              class="inline-flex items-center justify-center w-[44px] h-[18px] rounded-full text-[10px] font-medium shrink-0
                {line.level === 'warn'
                  ? 'bg-[hsl(38_92%_50%/.14)] text-[hsl(38,92%,32%)] border border-[hsl(38_92%_50%/.3)]'
                  : line.level === 'error'
                    ? 'bg-[hsl(var(--destructive)/.12)] text-destructive border border-[hsl(var(--destructive)/.3)]'
                    : 'bg-muted text-muted-foreground border border-border'}"
            >
              {line.level}
            </span>
            <span class="text-foreground">{line.message}</span>
          </div>
        {/each}
      {/if}
    </div>
  {/if}
</div>
