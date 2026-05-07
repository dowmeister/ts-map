import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { resolve, join, extname } from 'path'
import { createReadStream, statSync } from 'fs'

const MIME = {
  '.json':  'application/json',
  '.png':   'image/png',
  '.jpg':   'image/jpeg',
  '.jpeg':  'image/jpeg',
  '.svg':   'image/svg+xml',
  '.webp':  'image/webp',
};

export default defineConfig({
  plugins: [
    react(),
    {
      name: 'serve-map-data',
      configureServer(server) {
        const mapDataDir = resolve(__dirname, '../../map_data');
        server.middlewares.use('/map_data', (req, res, next) => {
          const filePath = join(mapDataDir, req.url.split('?')[0]);
          try {
            if (!statSync(filePath).isFile()) return next();
            res.setHeader('Content-Type', MIME[extname(filePath)] ?? 'application/octet-stream');
            createReadStream(filePath).pipe(res);
          } catch {
            next();
          }
        });
      },
    },
  ],
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
