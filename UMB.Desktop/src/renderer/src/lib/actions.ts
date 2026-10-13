import Hammer from '@lucide/svelte/icons/hammer'
import AudioWaveform from '@lucide/svelte/icons/audio-waveform'
import Volume2 from '@lucide/svelte/icons/volume-2'
import ListOrdered from '@lucide/svelte/icons/list-ordered'
import ArrowUpDown from '@lucide/svelte/icons/arrow-up-down'
import GitMerge from '@lucide/svelte/icons/git-merge'
import Image from '@lucide/svelte/icons/image'
import ListMusic from '@lucide/svelte/icons/list-music'
import ListPlus from '@lucide/svelte/icons/list-plus'
import SlidersHorizontal from '@lucide/svelte/icons/sliders-horizontal'
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
      { id: 'manage-presets', icon: SlidersHorizontal },
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