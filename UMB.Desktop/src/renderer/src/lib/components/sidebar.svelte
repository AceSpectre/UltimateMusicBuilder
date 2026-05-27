<script lang="ts">
  import {
    Hammer, FolderTree, AudioWaveform, CheckCircle, Volume2,
    ListOrdered, ArrowUpDown, Wand2, ArrowLeftRight, GitMerge,
    Image, Database
  } from '@lucide/svelte'
  import { sidebarStore } from '$lib/stores/sidebar.svelte'
  import type { Component } from 'svelte'

  interface NavItem {
    id: string
    label: string
    icon: Component
  }

  interface NavGroup {
    label: string
    items: NavItem[]
  }

  const groups: NavGroup[] = [
    {
      label: 'BUILD',
      items: [
        { id: 'build', label: 'Build', icon: Hammer },
        { id: 'scaffold', label: 'Scaffold', icon: FolderTree }
      ]
    },
    {
      label: 'AUDIO',
      items: [
        { id: 'nus3-convert', label: 'Nus3 Convert', icon: AudioWaveform },
        { id: 'accept-nus3', label: 'Accept Nus3', icon: CheckCircle },
        { id: 'config-volume', label: 'Config Volume', icon: Volume2 }
      ]
    },
    {
      label: 'ORGANIZE',
      items: [
        { id: 'order-series', label: 'Order Series', icon: ListOrdered },
        { id: 'order-tracks', label: 'Order Tracks', icon: ArrowUpDown },
        { id: 'cleanup', label: 'Cleanup', icon: Wand2 }
      ]
    },
    {
      label: 'TRANSFER',
      items: [
        { id: 'convert', label: 'Import', icon: ArrowLeftRight },
        { id: 'merge', label: 'Merge', icon: GitMerge },
        { id: 'extract-icons', label: 'Extract Icons', icon: Image }
      ]
    },
    {
      label: 'DIAGNOSTIC',
      items: [
        { id: 'dump-stages', label: 'Dump Stages', icon: Database }
      ]
    }
  ]

  function handleClick(id: string) {
    sidebarStore.setActive(id)
    window.electron.umb.runAction(id)
  }
</script>

<aside
  class="shrink-0 flex flex-col bg-card border-r border-border transition-[width] duration-200 overflow-hidden"
  style="width: {sidebarStore.collapsed ? '60px' : '232px'}"
>
  <nav class="flex-1 overflow-y-auto py-2">
    {#each groups as group}
      <div class="py-1">
        {#if !sidebarStore.collapsed}
          <div class="px-3.5 pt-2.5 pb-1 text-[10.5px] font-semibold tracking-[.12em] uppercase text-muted-foreground">
            {group.label}
          </div>
        {/if}

        {#each group.items as item}
          {@const isActive = sidebarStore.activeTab === item.id}
          <button
            onclick={() => handleClick(item.id)}
            class="relative flex items-center gap-2.5 mx-2 px-2.5 py-[7px] rounded-[7px] w-[calc(100%-16px)] text-left transition-colors duration-100
              {isActive
                ? 'font-semibold'
                : 'font-medium text-foreground/80 hover:bg-accent hover:text-foreground'}"
            style={isActive
              ? 'background: linear-gradient(135deg, hsl(var(--gradient-from) / .14), hsl(var(--gradient-to) / .14));'
              : ''}
            title={sidebarStore.collapsed ? item.label : undefined}
          >
            {#if isActive}
              <div
                class="absolute left-[-1px] top-1.5 bottom-1.5 w-[3px] rounded-r-[3px] gradient-bg"
              ></div>
            {/if}

            <svelte:component this={item.icon} size={16} class="shrink-0 {isActive ? 'opacity-100' : 'opacity-85'}" />

            {#if !sidebarStore.collapsed}
              <span class="text-[13.5px] truncate">{item.label}</span>
            {/if}
          </button>
        {/each}
      </div>
    {/each}
  </nav>
</aside>
