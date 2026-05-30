export const IPC = {
  LIST_MODS: 'umb:list-mods',
  LIST_MOD_SERIES: 'umb:list-mod-series',
  LOAD_TRACK_ORDER: 'umb:load-track-order',
  SAVE_TRACK_ORDER: 'umb:save-track-order',
  LOAD_SERIES_ORDER: 'umb:load-series-order',
  SAVE_SERIES_ORDER: 'umb:save-series-order',
  RUN_ACTION: 'umb:run-action',
  LOG_STREAM: 'umb:log-stream',
  GET_WORKSPACE: 'umb:get-workspace',
  CANCEL_ACTION: 'umb:cancel-action',
  DEBUG_PING: 'umb:debug-ping',
  WINDOW_MINIMIZE: 'umb:window-minimize',
  WINDOW_FULLSCREEN: 'umb:window-fullscreen',
  WINDOW_CLOSE: 'umb:window-close'
} as const
