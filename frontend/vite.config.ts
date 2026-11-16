import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// In development the API runs on :5008 and is proxied, so the browser sees one origin.
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: { '/api': 'http://localhost:5008', '/sprites': 'http://localhost:5008' },
  },
})
