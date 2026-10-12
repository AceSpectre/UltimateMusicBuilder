import { describe, expect, it } from 'vitest'
import { dbToGain, formatBankDb, formatDb, parseDb } from './volume'

describe('formatDb', () => {
  it('signs positive boosts and keeps one decimal', () => {
    expect(formatDb(3.456)).toBe('+3.5 dB')
    expect(formatDb(0)).toBe('0.0 dB')
    expect(formatDb(-6)).toBe('-6.0 dB')
  })
})

describe('formatBankDb', () => {
  it('shows the absolute volume without a sign', () => {
    expect(formatBankDb(2.7)).toBe('2.7 dB')
    expect(formatBankDb(-1.5)).toBe('-1.5 dB')
  })
})

describe('dbToGain', () => {
  it('converts dB to linear gain', () => {
    expect(dbToGain(0)).toBe(1)
    expect(dbToGain(20)).toBeCloseTo(10)
    expect(dbToGain(-6)).toBeCloseTo(0.501, 3)
  })
})

describe('parseDb', () => {
  it('parses and clamps to the editable range', () => {
    expect(parseDb('-4.5')).toBe(-4.5)
    expect(parseDb('99')).toBe(20)
    expect(parseDb('-99')).toBe(-30)
  })

  it('treats blank or invalid input as no boost', () => {
    expect(parseDb('')).toBe(0)
    expect(parseDb('abc')).toBe(0)
  })
})
