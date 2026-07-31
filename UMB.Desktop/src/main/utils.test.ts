import { describe, expect, it } from 'vitest'
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'fs'
import { join } from 'path'
import { tmpdir } from 'os'
import { isChildOf, lineSplitter, log, readJson, resolveUnderMods, tempPath, writeJson } from './utils'

describe('isChildOf', () => {
  it('accepts a nested child and rejects siblings, parents and self', () => {
    const parent = join(tmpdir(), 'umb-parent')
    expect(isChildOf(parent, join(parent, 'child'))).toBe(true)
    expect(isChildOf(parent, join(parent, 'a', 'b'))).toBe(true)
    expect(isChildOf(parent, parent)).toBe(false)
    expect(isChildOf(parent, join(parent, '..', 'other'))).toBe(false)
  })
})

describe('resolveUnderMods', () => {
  const workspace = join(tmpdir(), 'umb-ws')

  it('returns the resolved path for a mod inside Mods/MusicMods', () => {
    const inside = join(workspace, 'Mods', 'MusicMods', 'MyMod')
    expect(resolveUnderMods(workspace, inside)).toBe(inside)
  })

  it('throws the given message for paths outside Mods/MusicMods', () => {
    expect(() => resolveUnderMods(workspace, workspace, 'Invalid series path.')).toThrow('Invalid series path.')
    expect(() => resolveUnderMods(workspace, join(workspace, 'Mods'))).toThrow('Invalid mod path.')
  })
})

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

describe('json helpers', () => {
  it('round-trips objects and returns null for missing/corrupt files', () => {
    const dir = mkdtempSync(join(tmpdir(), 'umb-json-'))
    try {
      const path = join(dir, 'nested', 'value.json')
      writeJson(path, { a: 1 })
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

describe('log / tempPath', () => {
  it('log stamps the level and message', () => {
    const line = log('warn', 'hello')
    expect(line.level).toBe('warn')
    expect(line.message).toBe('hello')
    expect(line.timestamp).toMatch(/\d{2}:\d{2}:\d{2}/)
  })

  it('tempPath yields unique paths with the requested prefix/extension', () => {
    const a = tempPath('umb-test', 'json')
    const b = tempPath('umb-test', 'json')
    expect(a).toMatch(/umb-test-.*\.json$/)
    expect(a).not.toBe(b)
  })
})
