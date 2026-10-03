import { posix, resolve } from 'path'

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
