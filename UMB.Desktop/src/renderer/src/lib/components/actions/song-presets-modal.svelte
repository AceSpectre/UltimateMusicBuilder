<script lang="ts">
  import { _ } from 'svelte-i18n'
  import Modal from '$lib/components/ui/modal.svelte'
  import type { DefaultTrackData, SeriesGame } from '$lib/types/electron'

  let { games, defaults, presets, onSave, onClose }: {
    games: SeriesGame[]
    defaults: DefaultTrackData | null
    presets: DefaultTrackData[]
    onSave: (defaults: DefaultTrackData, presets: DefaultTrackData[]) => Promise<void>
    onClose: () => void
  } = $props()

  function initialDefaults(): DefaultTrackData {
    return { ...defaults ?? {
      game: games[0]?.id ?? '', author: '', copyright: '', record_type: 'original', volume: 1
    } }
  }
  function initialPresets(): DefaultTrackData[] { return presets.map((p) => ({ ...p })) }
  let seriesDefaults = $state<DefaultTrackData>(initialDefaults())
  let gamePresets = $state<DefaultTrackData[]>(initialPresets())
  let scope = $state('')
  let saving = $state(false)
  let error = $state('')
  const gamePreset = $derived(gamePresets.find((p) => p.game === scope))
  const editable = $derived(scope ? gamePreset : seriesDefaults)
  const inputClass = 'w-full rounded border border-input bg-background px-2 py-1.5 text-[13px] outline-none focus:border-ring'

  function setFallback(enabled: boolean) {
    gamePresets = gamePresets.filter((p) => p.game !== scope)
    if (!enabled) gamePresets.push({ ...seriesDefaults, game: scope })
  }

  async function save() {
    saving = true
    error = ''
    try {
      await onSave({ ...seriesDefaults }, gamePresets.map((p) => ({ ...p })))
      onClose()
    } catch (reason) {
      error = reason instanceof Error ? reason.message : String(reason)
    } finally {
      saving = false
    }
  }
</script>

<Modal>
  <div class="max-h-[75vh] overflow-auto px-5 py-4">
    <h3 class="text-sm font-semibold">{$_('songPresets.title')}</h3>
    <p class="pt-1.5 text-[13px] text-muted-foreground">{$_('songPresets.hint')}</p>
    <label class="mt-4 block space-y-1">
      <span class="text-[12px] font-medium">{$_('songPresets.editScope')}</span>
      <select class={inputClass} bind:value={scope} disabled={saving}>
        <option value="">{$_('songPresets.seriesDefaults')}</option>
        {#each games as game}
          <option value={game.id}>{game.name}</option>
        {/each}
        {#each gamePresets.filter((p) => !games.some((g) => g.id === p.game)) as preset}
          <option value={preset.game}>{preset.game}</option>
        {/each}
      </select>
    </label>
    {#if scope}
      <label class="mt-3 flex items-center gap-2 text-[13px]">
        <input type="checkbox" checked={!gamePreset} disabled={saving}
          onchange={(event) => setFallback(event.currentTarget.checked)} />
        {$_('songPresets.useSeriesDefaults')}
      </label>
    {/if}
    <p class="pt-2 text-[12px] text-muted-foreground">{$_('songPresets.fallbackHint')}</p>
    {#if editable}
      <fieldset disabled={saving} class="mt-4 space-y-3">
        {#if !scope}
          <label class="block space-y-1">
            <span class="text-[12px] font-medium">{$_('orderSeries.fldDefaultGame')}</span>
            <select class={inputClass} bind:value={editable.game}>
              <option value="">{$_('orderSeries.defaultGameNone')}</option>
              {#each games as game}<option value={game.id}>{game.name}</option>{/each}
              {#if editable.game && !games.some((g) => g.id === editable.game)}
                <option value={editable.game}>{editable.game}</option>
              {/if}
            </select>
          </label>
        {/if}
        <label class="block space-y-1">
          <span class="text-[12px] font-medium">{$_('orderSeries.fldDefaultAuthor')}</span>
          <input class={inputClass} type="text" bind:value={editable.author} />
        </label>
        <label class="block space-y-1">
          <span class="text-[12px] font-medium">{$_('orderSeries.fldDefaultCopyright')}</span>
          <input class={inputClass} type="text" bind:value={editable.copyright} />
        </label>
        <label class="block space-y-1">
          <span class="text-[12px] font-medium">{$_('orderSeries.fldDefaultRecord')}</span>
          <select class={inputClass} bind:value={editable.record_type}>
            <option value="original">{$_('orderTracks.recordOriginal')}</option>
            <option value="arrange">{$_('orderTracks.recordArrange')}</option>
            <option value="new_arrange">{$_('orderTracks.recordNewArrange')}</option>
          </select>
        </label>
        <label class="block space-y-1">
          <span class="text-[12px] font-medium">{$_('songPresets.volume')}</span>
          <input class={inputClass} type="number" min="0" step="0.1" bind:value={editable.volume} />
        </label>
      </fieldset>
    {/if}
    {#if error}<p class="mt-3 text-[13px] text-destructive" role="alert">{error}</p>{/if}
  </div>
  <div class="flex justify-end gap-2 border-t border-border px-5 py-4">
    <button class="rounded-lg border border-input px-3 py-2 text-[13px]" onclick={onClose} disabled={saving}>{$_('songPresets.cancel')}</button>
    <button class="rounded-lg border border-input px-3 py-2 text-[13px]" onclick={save} disabled={saving}>
      {saving ? $_('songPresets.saving') : $_('songPresets.save')}
    </button>
  </div>
</Modal>
