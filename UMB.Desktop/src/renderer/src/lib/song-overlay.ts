import type { SeriesGame, TrackFields, TrackOrderItem } from '$lib/types/electron'

// Colour-coding contexts for the Manage Songs table, cycled by one toolbar button.
export const OVERLAY_MODES = ['off', 'game', 'source', 'soundtest'] as const
export type OverlayMode = (typeof OVERLAY_MODES)[number]

export interface LegendEntry {
  label: string
  style: string
}

const MODDED_HUE = 210
const VANILLA_HUE = 35
const HIDDEN_STYLE = 'opacity: 0.45;'

export const nextOverlayMode = (mode: OverlayMode): OverlayMode =>
  OVERLAY_MODES[(OVERLAY_MODES.indexOf(mode) + 1) % OVERLAY_MODES.length]

export const inSoundtest = (fields: TrackFields): boolean => fields.in_soundtest.toLowerCase() === 'true'

// Golden-angle steps keep neighbouring games' hues far apart.
const gameHue = (index: number): number => Math.round((200 + index * 137.508) % 360)

// background-image so the tint stacks on the row bg and doesn't clash with the selection ring.
const tintStyle = (hue: number): string =>
  `background-image: linear-gradient(to right, hsl(${hue} 70% 50%) 3px, hsl(${hue} 70% 50% / 0.14) 3px);`

// Declared series games first, then any other game ids the songs use.
export function seriesGameIds(games: SeriesGame[], items: TrackOrderItem[]): string[] {
  return [...new Set([...games.map((g) => g.id), ...items.flatMap((i) => (i.fields?.game ? [i.fields.game] : []))])]
}

export function overlayStyle(mode: OverlayMode, item: TrackOrderItem, gameIds: string[]): string {
  switch (mode) {
    case 'game': {
      const index = gameIds.indexOf(item.fields?.game ?? '')
      return index < 0 ? '' : tintStyle(gameHue(index))
    }
    case 'source':
      return tintStyle(item.fields ? MODDED_HUE : VANILLA_HUE)
    case 'soundtest':
      return item.fields && !inSoundtest(item.fields) ? HIDDEN_STYLE : ''
    default:
      return ''
  }
}

export function overlayLegend(
  mode: OverlayMode,
  gameIds: string[],
  gameName: (id: string) => string,
  t: (key: string) => string
): LegendEntry[] {
  switch (mode) {
    case 'game':
      return gameIds.map((id, index) => ({ label: gameName(id), style: tintStyle(gameHue(index)) }))
    case 'source':
      return [
        { label: t('orderTracks.overlayModded'), style: tintStyle(MODDED_HUE) },
        { label: t('orderTracks.vanillaTag'), style: tintStyle(VANILLA_HUE) }
      ]
    case 'soundtest':
      return [{ label: t('orderTracks.overlayHidden'), style: HIDDEN_STYLE }]
    default:
      return []
  }
}
