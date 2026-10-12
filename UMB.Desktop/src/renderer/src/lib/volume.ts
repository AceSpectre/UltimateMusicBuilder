// Volume boosts are stored and edited in dB (0 = unchanged).

const MIN_DB = -30
const MAX_DB = 20

/** Signed label, e.g. "+3.5 dB". */
export const formatDb = (db: number): string => `${db > 0 ? '+' : ''}${db.toFixed(1)} dB`

/** Absolute nus3bank volume, e.g. "2.7 dB". */
export const formatBankDb = (db: number): string => `${db.toFixed(1)} dB`

/** Linear gain for Web Audio playback. */
export const dbToGain = (db: number): number => 10 ** (db / 20)

/** Parses a typed boost, clamped to the editable range (blank or invalid is 0). */
export function parseDb(raw: string): number {
  const value = parseFloat(raw)
  return Number.isNaN(value) ? 0 : Math.max(MIN_DB, Math.min(MAX_DB, value))
}

export const DB_INPUT = { min: MIN_DB, max: MAX_DB, step: 0.5 }
