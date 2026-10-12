import { describe, expect, it } from 'vitest'
import { nextSort, sortBy } from './table-sort'

describe('nextSort', () => {
  it('sorts a newly clicked column ascending', () => {
    expect(nextSort({ column: 'a', desc: true }, 'b')).toEqual({ column: 'b', desc: false })
  })

  it('flips direction when the same column is clicked again', () => {
    expect(nextSort({ column: 'a', desc: false }, 'a')).toEqual({ column: 'a', desc: true })
    expect(nextSort({ column: 'a', desc: true }, 'a')).toEqual({ column: 'a', desc: false })
  })
})

describe('sortBy', () => {
  const words = ['b', 'A', 'track 10', 'track 2', 'a']

  it('sorts strings case-insensitively with natural number order', () => {
    expect(sortBy(words, false, (w) => w)).toEqual(['A', 'a', 'b', 'track 2', 'track 10'])
  })

  it('reverses for descending but keeps ties in original order', () => {
    expect(sortBy(words, true, (w) => w)).toEqual(['track 10', 'track 2', 'b', 'A', 'a'])
  })

  it('sorts numbers, booleans and the original index', () => {
    expect(sortBy([3, 1, 2], false, (n) => n)).toEqual([1, 2, 3])
    expect(sortBy([true, false, true], false, (b) => b)).toEqual([false, true, true])
    expect(sortBy(['x', 'y', 'z'], true, (_, i) => i)).toEqual(['z', 'y', 'x'])
  })

  it('does not mutate the input', () => {
    const input = [2, 1]
    sortBy(input, false, (n) => n)
    expect(input).toEqual([2, 1])
  })
})
