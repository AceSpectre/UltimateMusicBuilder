export const sidebarStore = $state({
  collapsed: localStorage.getItem('umb-sidebar-collapsed') === 'true',
  activeTab: 'build',
  toggle() {
    sidebarStore.collapsed = !sidebarStore.collapsed
    localStorage.setItem('umb-sidebar-collapsed', String(sidebarStore.collapsed))
  },

  setActive(tab: string) {
    sidebarStore.activeTab = tab
  }
})
