/** Case-insensitive substring match of a search box value against any of the fields; blank matches all. */
export function matchesSearch(search: string, ...fields: string[]): boolean {
  const q = search.trim().toLowerCase()
  return !q || fields.some((field) => field.toLowerCase().includes(q))
}
