import { resolve } from 'path'
import { defineConfig } from 'vitest/config'

export default defineConfig({
  test: {
    environment: 'node',
    include: ['src/main/**/*.test.ts'],
    coverage: {
      provider: 'v8',
      include: ['src/main/**/*.ts'],
      exclude: ['src/main/**/*.test.ts', 'src/main/index.ts', 'src/main/preload.ts', 'src/main/cli.ts']
    }
  },
  resolve: {
    alias: {
      $shared: resolve(__dirname, 'src/shared')
    }
  }
})
