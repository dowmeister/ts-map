import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  base: './',
  server: {
    port: 3000,
    historyApiFallback: true,
    proxy: {
      '/api': { target: 'http://localhost:3001', changeOrigin: true },
      '/font': { target: 'https://demotiles.maplibre.org', changeOrigin: true },
    },
  },
  publicDir: 'public'
})
