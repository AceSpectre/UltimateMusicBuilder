import { describe, expect, it } from 'vitest'
import { matchesSearch } from './search'

describe('matchesSearch', () => {
  it('matches everything when the search is blank', () => {
    expect(matchesSearch('  ', 'Mario')).toBe(true)
  })

  it('matches a case-insensitive substring of any field', () => {
    expect(matchesSearch('MAR', 'Super Mario')).toBe(true)
    expect(matchesSearch('bgm', 'Battlefield', 'bgm_battle')).toBe(true)
    expect(matchesSearch('zelda', 'Mario', 'bgm_mario')).toBe(false)
  })
})
