import { existsSync, readFileSync, writeFileSync } from 'fs'
import { parse as parseCsv } from 'csv-parse/sync'
import { stringify as stringifyCsv } from 'csv-stringify/sync'

export interface CsvRow {
  [key: string]: string
}

/**
 * Reads a tracks.csv preserving cell whitespace (trim: false) for lossless round-trips,
 * returning both the keyed rows and the raw header order.
 */
export function readCsvWithHeaders(csvPath: string): { rows: CsvRow[]; headers: string[] } {
  const source = readFileSync(csvPath, 'utf8')
  const rows = parseCsv(source, {
    columns: true,
    skip_empty_lines: true,
    relax_column_count: true,
    bom: true,
    trim: false
  }) as CsvRow[]

  const headers = parseCsv(source, {
    to_line: 1,
    relax_column_count: true,
    bom: true
  })[0] as string[]

  return { rows, headers }
}

export function writeCsvRows(csvPath: string, rows: CsvRow[], headers: string[]): void {
  const output = stringifyCsv(rows, {
    header: true,
    columns: headers,
    quoted_match: /[\n\r,]/
  })

  writeFileSync(csvPath, output, 'utf8')
}

/** Lenient trimmed read for display/merge purposes: [] when missing or unparsable. */
export function readCsvLenient(csvPath: string): CsvRow[] {
  if (!existsSync(csvPath)) return []
  try {
    return parseCsv(readFileSync(csvPath, 'utf-8'), {
      columns: true,
      skip_empty_lines: true,
      relax_column_count: true,
      bom: true,
      trim: true
    }) as CsvRow[]
  } catch {
    return []
  }
}

/** Counts data rows (non-empty, excluding the header); 0 when missing/unreadable. */
export function countCsvDataRows(csvPath: string): number {
  try {
    const lines = readFileSync(csvPath, 'utf-8')
      .split(/\r?\n/)
      .filter((l) => l.trim().length > 0)
    return Math.max(0, lines.length - 1)
  } catch {
    return 0
  }
}
