import type { SeriesGame, TrackFields, TrackOrderItem } from '$lib/types/electron'

// Manage Songs overlay: colours songs by game, dimmed when hidden from the sound test.
export interface LegendEntry {
  label: string
  style: string
}

export const inSoundtest = (fields: TrackFields): boolean => fields.in_soundtest.toLowerCase() === 'true'

// Golden-angle steps keep neighbouring games' hues far apart.
const gameHue = (index: number): number => Math.round((200 + index * 137.508) % 360)

// background-image so the tint stacks on the row bg and doesn't clash with the selection ring.
const tintStyle = (hue: number, dim = false): string => {
  const alpha = dim ? 0.35 : 1
  return `background-image: linear-gradient(to right, hsl(${hue} 70% 50% / ${alpha}) 3px, hsl(${hue} 70% 50% / ${0.14 * alpha}) 3px);`
}

// Declared series games first, then any other game ids the songs use.
export function seriesGameIds(games: SeriesGame[], items: TrackOrderItem[]): string[] {
  return [...new Set([...games.map((g) => g.id), ...items.flatMap((i) => (i.fields?.game ? [i.fields.game] : []))])]
}

export function overlayStyle(item: TrackOrderItem, gameIds: string[]): string {
  const index = gameIds.indexOf(item.fields?.game ?? '')
  return item.fields && index >= 0 ? tintStyle(gameHue(index), !inSoundtest(item.fields)) : ''
}

export const overlayLegend = (gameIds: string[], gameName: (id: string) => string): LegendEntry[] =>
  gameIds.map((id, index) => ({ label: gameName(id), style: tintStyle(gameHue(index)) }))
