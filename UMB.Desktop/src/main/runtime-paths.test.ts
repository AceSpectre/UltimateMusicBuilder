import { describe, expect, it } from 'vitest'
import { resolve } from 'path'
import { macToolPath, packagedWorkspacePath } from './runtime-paths'

describe('packagedWorkspacePath', () => {
  it.each([
    ['darwin', '/release/desktop/UltimateMusicBuilder.app/Contents/Resources'],
    ['win32', '/release/desktop/resources'],
    ['linux', '/release/desktop/resources']
  ] as const)('resolves the release root on %s', (platform, resourcesPath) => {
    expect(packagedWorkspacePath(resourcesPath, platform)).toBe(resolve('/release'))
  })
})

describe('macToolPath', () => {
  it('adds Homebrew and pipx locations to a Finder PATH', () => {
    expect(macToolPath('/usr/bin:/bin', '/Users/example')).toBe(
      '/usr/bin:/bin:/opt/homebrew/bin:/usr/local/bin:/Users/example/.local/bin'
    )
  })

  it('preserves existing precedence and a custom pipx directory without duplicates', () => {
    expect(macToolPath('/custom/bin:/opt/homebrew/bin', '/Users/example', '/custom/bin')).toBe(
      '/custom/bin:/opt/homebrew/bin:/usr/local/bin'
    )
  })
})
