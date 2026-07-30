import {
  Hammer, AudioWaveform, Volume2,
  ListOrdered, ArrowUpDown, GitMerge,
  Image, ListMusic, ListPlus
} from '@lucide/svelte'
import type { Component } from 'svelte'

export interface NavItem {
  id: string
  icon: Component
}

export interface NavGroup {
  id: string
  items: NavItem[]
}

export const navGroups: NavGroup[] = [
  {
    id: 'build',
    items: [
      { id: 'build', icon: Hammer }
    ]
  },
  {
    id: 'audio',
    items: [
      { id: 'nus3-convert', icon: AudioWaveform },
      { id: 'config-volume', icon: Volume2 }
    ]
  },
  {
    id: 'organize',
    items: [
      { id: 'order-series', icon: ListOrdered },
      { id: 'order-tracks', icon: ArrowUpDown },
      { id: 'manage-playlists', icon: ListPlus }
    ]
  },
  {
    id: 'transfer',
    items: [
      { id: 'merge', icon: GitMerge },
      { id: 'extract-icons', icon: Image }
    ]
  },
  {
    id: 'inspect',
    items: [
      { id: 'playlist-info', icon: ListMusic }
    ]
  }
]

export const actions = navGroups.flatMap((group) =>
  group.items.map((item) => ({
    id: item.id
  }))
)