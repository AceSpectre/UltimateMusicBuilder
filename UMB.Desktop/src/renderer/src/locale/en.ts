export default {
  build: {
    title: 'Build',
    subtitle: 'Build and scaffold mod output for your Nintendo Switch.',
    buildButton: 'Build',
    buildDescription: 'Compile all mods into ArcOutput for the Switch.',
    buildDisabled: 'Build is not yet available in the desktop app.',
    scaffoldButton: 'Scaffold',
    scaffoldDescription: 'Generate folder structure and series order for existing mods.',
    running: 'Running...'
  },
  app: {
    selectAction: 'Select an action',
    viewModeNotice:
      'Action selection now changes the desktop view without auto-running the CLI. This avoids non-interactive prompt failures and overlapping `dotnet run` locks while the desktop UI is being built out.'
  },
  appBar: {
    brand: 'Ultimate Music Builder',
    version: 'v0.8.2 · electron',
    modsRoot: 'Mods/MusicMods/',
    loadingMods: 'Loading mods...',
    noModSelected: 'No mod selected',
    selectMod: 'Select Mod',
    modPath: 'Mods/MusicMods/{name}',
    seriesCount: '{value} series',
    tracksCount: '{value} tracks',
    minimize: 'Minimize window',
    fullscreen: 'Toggle fullscreen',
    close: 'Close window'
  },
  nav: {
    groups: {
      build: 'BUILD',
      audio: 'AUDIO',
      organize: 'ORGANIZE',
      transfer: 'TRANSFER',
      diagnostic: 'DIAGNOSTIC'
    },
    items: {
      build: 'Build',
      'nus3-convert': 'Nus3 Convert',
      'accept-nus3': 'Accept Nus3',
      'config-volume': 'Config Volume',
      'order-series': 'Order Series',
      'order-tracks': 'Order Tracks',
      cleanup: 'Cleanup',
      convert: 'Import',
      merge: 'Merge',
      'extract-icons': 'Extract Icons',
      'dump-stages': 'Dump Stages'
    }
  },
  commandPalette: {
    searchPlaceholder: 'Search actions...',
    noResults: 'No results found.',
    actions: 'Actions',
    mods: 'Mods'
  },
  bottomPanel: {
    collapsedLabel: 'bottom panel',
    show: 'Show panel',
    title: 'Bottom Panel',
    logLines: '{count} log lines',
    console: 'Console',
    runtimeDebug: 'Runtime Debug',
    ping: 'Ping',
    noLogs: 'No log entries yet.',
    resizePanel: 'Resize bottom panel',
    resizeSections: 'Resize bottom panel sections',
    filters: {
      all: 'All',
      info: 'Info',
      warn: 'Warn',
      error: 'Error'
    },
    debug: {
      bridge: 'bridge:',
      workspace: 'workspace:',
      mods: 'mods:',
      activeTab: 'active tab:',
      modSelections: 'mod selections:',
      actionClicks: 'action clicks:',
      windowClicks: 'window clicks:',
      windowAck: 'window ack:',
      lastError: 'last error:'
    }
  },
  orderSeries: {
    title: 'Order Series',
    dragHint: 'Drag series to reorder. Custom series appear after official series, before Other.',
    chooseMod: 'Choose a mod',
    chooseModBody: 'Select a mod from the app bar to view and reorder its custom series.',
    noCustomSeries: 'No custom series found in this mod.',
    loading: 'Loading series...',
    reload: 'Reload series',
    save: 'Save Order',
    saving: 'Saving...',
    saved: 'Saved',
    dragAria: 'Drag to reorder',
    emptyTitle: 'No custom series',
    emptyBody: 'This mod has no custom series to reorder. Only non-existing series (not built into the game) can be reordered here.'
  },
  orderTracks: {
    title: 'Order Tracks',
    seriesHeading: 'Series',
    selectModSubtitle: 'Select a mod to browse series.',
    chooseMod: 'Choose a mod from the app bar to start.',
    loadingSeries: 'Loading series...',
    noSeries: 'No series with tracks.csv were found in this mod.',
    reload: 'Reload series',
    dragHintLocked:
      'Drag tracks to reorder them. Locked vanilla entries are preserved from song_order.toml.',
    dragHint: 'Drag tracks to reorder the tracks.csv entries for this series.',
    pickSeriesHint: 'Pick a series from the left to start ordering tracks.',
    save: 'Save Order',
    saving: 'Saving...',
    saved: 'Saved',
    loadingOrder: 'Loading order data...',
    dragAria: 'Drag to reorder',
    locked: 'Locked',
    emptyTitle: 'Pick a series',
    emptyBody:
      'This panel will show the selected series order and drag handles once a series is selected.',
    unsavedTitle: 'Unsaved order changes',
    unsavedBodyBefore: 'You have unsaved changes to the track order for ',
    unsavedBodyAfter: '. Save them before switching series?',
    saveAndSwitch: 'Yes, save changes',
    discard: 'No, discard changes',
    continue: 'Continue making changes'
  }
}
