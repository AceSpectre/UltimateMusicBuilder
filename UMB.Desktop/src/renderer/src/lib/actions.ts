export interface NavGroup {
  id: string
  items: string[]
}

export const navGroups: NavGroup[] = [
  { id: 'build', items: ['build'] },
  { id: 'audio', items: ['nus3-convert', 'config-volume'] },
  { id: 'organize', items: ['order-series', 'order-tracks', 'manage-presets', 'manage-playlists'] },
  { id: 'transfer', items: ['merge', 'extract-icons'] },
  { id: 'inspect', items: ['playlist-info'] }
]

export const actions = navGroups.flatMap((group) => group.items)
