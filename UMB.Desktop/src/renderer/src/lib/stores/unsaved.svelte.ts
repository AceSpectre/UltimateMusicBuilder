// The open view's unsaved changes, so leaving it (another view, mod or series) can ask first.
export const unsavedStore = $state({
  dirty: false,
  save: null as (() => Promise<void>) | null,
  // The leave waiting on the user's answer; App shows the prompt while it is set.
  pendingLeave: null as (() => void) | null
})

/** Runs leave now, or once the user saves or discards the open view's unsaved changes. */
export function guardLeave(leave: () => void): void {
  if (unsavedStore.dirty) unsavedStore.pendingLeave = leave
  else leave()
}

/** Registers a view's dirty state and save action for the leave guard; call during component init. */
export function trackUnsaved(isDirty: () => boolean, save: () => Promise<void>): void {
  $effect(() => {
    unsavedStore.dirty = isDirty()
    unsavedStore.save = save
  })
  // Only clear our own registration: the next view may already have registered.
  $effect(() => () => {
    if (unsavedStore.save !== save) return
    unsavedStore.dirty = false
    unsavedStore.save = null
  })
}
