let collapsed = $state(localStorage.getItem('umb-sidebar-collapsed') === 'true')
let activeTab = $state<string>('build')

export const sidebarStore = {
  get collapsed() { return collapsed },
  get activeTab() { return activeTab },

  toggle() {
    collapsed = !collapsed
    localStorage.setItem('umb-sidebar-collapsed', String(collapsed))
  },

  setActive(tab: string) {
    activeTab = tab
  }
}
