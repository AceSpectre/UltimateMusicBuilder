// Click-to-sort table headers.
export interface SortState<C extends string> {
  column: C
  desc: boolean
}

export type SortKey = string | number | boolean

export const nextSort = <C extends string>(sort: SortState<C>, column: C): SortState<C> => ({
  column,
  desc: sort.column === column && !sort.desc
})

const compareKeys = (a: SortKey, b: SortKey): number =>
  typeof a === 'string' && typeof b === 'string'
    ? a.localeCompare(b, undefined, { numeric: true, sensitivity: 'base' })
    : Number(a) - Number(b)

// Returns a sorted copy; ties keep their original relative order.
export function sortBy<T>(items: T[], desc: boolean, keyOf: (item: T, index: number) => SortKey): T[] {
  return items
    .map((item, index) => ({ item, index, key: keyOf(item, index) }))
    .sort((a, b) => (desc ? -1 : 1) * compareKeys(a.key, b.key) || a.index - b.index)
    .map((entry) => entry.item)
}
