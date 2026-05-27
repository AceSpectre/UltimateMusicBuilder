import {
  Hammer, FolderTree, AudioWaveform, CheckCircle, Volume2,
  ListOrdered, ArrowUpDown, Wand2, ArrowLeftRight, GitMerge,
  Image, Database
} from '@lucide/svelte'
import type { Component } from 'svelte'

export interface NavItem {
  id: string
  label: string
  icon: Component
  mode?: 'cli' | 'view'
}

export interface NavGroup {
  label: string
  items: NavItem[]
}

export const navGroups: NavGroup[] = [
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
      { id: 'order-tracks', label: 'Order Tracks', icon: ArrowUpDown, mode: 'view' },
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

export const actions = navGroups.flatMap((group) =>
  group.items.map((item) => ({
    id: item.id,
    label: item.label,
    group: 'Actions',
    mode: item.mode ?? 'cli'
  }))
)

export function shouldRunCliAction(actionId: string): boolean {
  return actions.find((action) => action.id === actionId)?.mode !== 'view'
}