import {
  Hammer, AudioWaveform, Volume2,
  ListOrdered, ArrowUpDown, GitMerge,
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
  id: string
  label: string
  items: NavItem[]
}

export const navGroups: NavGroup[] = [
  {
    id: 'build',
    label: 'BUILD',
    items: [
      { id: 'build', label: 'Build', icon: Hammer, mode: 'view' }
    ]
  },
  {
    id: 'audio',
    label: 'AUDIO',
    items: [
      { id: 'nus3-convert', label: 'Nus3 Convert', icon: AudioWaveform, mode: 'view' },
      { id: 'config-volume', label: 'Config Volume', icon: Volume2, mode: 'view' }
    ]
  },
  {
    id: 'organize',
    label: 'ORGANIZE',
    items: [
      { id: 'order-series', label: 'Order Series', icon: ListOrdered, mode: 'view' },
      { id: 'order-tracks', label: 'Order Tracks', icon: ArrowUpDown, mode: 'view' }
    ]
  },
  {
    id: 'transfer',
    label: 'TRANSFER',
    items: [
      { id: 'merge', label: 'Merge', icon: GitMerge },
      { id: 'extract-icons', label: 'Extract Icons', icon: Image }
    ]
  },
  {
    id: 'diagnostic',
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