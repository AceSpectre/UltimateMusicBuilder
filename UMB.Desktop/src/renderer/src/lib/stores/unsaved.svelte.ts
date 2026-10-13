// The open view's unsaved changes, so leaving it (another view or mod) can ask first.
export const unsavedStore = $state({
  dirty: false,
  save: null as (() => Promise<void>) | null
})

/** Registers a view's dirty state and save action for the leave guard; call during component init. */
export function trackUnsaved(isDirty: () => boolean, save: () => Promise<void>): void {
  $effect(() => {
    unsavedStore.dirty = isDirty()
    unsavedStore.save = save
  })
  $effect(() => () => {
    unsavedStore.dirty = false
    unsavedStore.save = null
  })
}
