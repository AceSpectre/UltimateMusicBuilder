import { posix, resolve } from 'path'

/**
 * Release root for a packaged app. resourcesPath is <root>/desktop/resources on Windows/Linux
 * and <root>/desktop/UltimateMusicBuilder.app/Contents/Resources on macOS.
 */
export function packagedWorkspacePath(resourcesPath: string, platform: NodeJS.Platform = process.platform): string {
  return resolve(resourcesPath, platform === 'darwin' ? '../../../..' : '../..')
}

export function macToolPath(currentPath: string, home: string, pipxBinDir?: string): string {
  return [...new Set([
    ...currentPath.split(':'),
    '/opt/homebrew/bin',
    '/usr/local/bin',
    pipxBinDir || posix.join(home, '.local', 'bin')
  ])].filter(Boolean).join(':')
}
