import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'

// Catch script loading errors (e.g. 404 on outdated chunk hashes after new deployment)
window.addEventListener('error', (event) => {
  const target = event.target as HTMLElement | null;
  if (target && target.tagName === 'SCRIPT') {
    console.warn('Script loading error detected (likely 404 on stale build chunk). Clearing cache and reloading...');
    if ('caches' in window) {
      caches.keys().then((names) => {
        names.forEach((name) => caches.delete(name));
      });
    }
    window.location.reload();
  }
}, true);

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)

if ('serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('/sw.js')
      .then((reg) => {
        console.log('Service Worker registered successfully:', reg.scope);
        // Automatically prompt SW update check on load
        reg.update().catch(() => {});
      })
      .catch((err) => console.error('Service Worker registration failed:', err));
  });
}


