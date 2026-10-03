import { describe, expect, it } from 'vitest'
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'fs'
import { join } from 'path'
import { tmpdir } from 'os'
import { lineSplitter, readJson, tempPath } from './utils'

describe('lineSplitter', () => {
  it('emits complete lines across chunk boundaries and flushes the tail', () => {
    const lines: string[] = []
    const splitter = lineSplitter((line) => lines.push(line))

    splitter.push(Buffer.from('first\nsec'))
    splitter.push(Buffer.from('ond\n'))
    splitter.push(Buffer.from('tail'))
    expect(lines).toEqual(['first', 'second'])

    splitter.flush()
    expect(lines).toEqual(['first', 'second', 'tail'])
  })

  it('does not flush whitespace-only remainders', () => {
    const lines: string[] = []
    const splitter = lineSplitter((line) => lines.push(line))
    splitter.push(Buffer.from('a\n  '))
    splitter.flush()
    expect(lines).toEqual(['a'])
  })
})

describe('readJson', () => {
  it('reads objects and returns null for missing/corrupt files', () => {
    const dir = mkdtempSync(join(tmpdir(), 'umb-json-'))
    try {
      const path = join(dir, 'value.json')
      writeFileSync(path, JSON.stringify({ a: 1 }), 'utf-8')
      expect(readJson<{ a: number }>(path)).toEqual({ a: 1 })

      expect(readJson(join(dir, 'missing.json'))).toBeNull()

      const corrupt = join(dir, 'corrupt.json')
      writeFileSync(corrupt, '{ not json', 'utf-8')
      expect(readJson(corrupt)).toBeNull()
      expect(readFileSync(corrupt, 'utf-8')).toContain('not json')
    } finally {
      rmSync(dir, { recursive: true, force: true })
    }
  })
})

describe('tempPath', () => {
  it('tempPath yields unique paths with the requested prefix/extension', () => {
    const a = tempPath('umb-test', 'json')
    const b = tempPath('umb-test', 'json')
    expect(a).toMatch(/umb-test-.*\.json$/)
    expect(a).not.toBe(b)
  })
})
