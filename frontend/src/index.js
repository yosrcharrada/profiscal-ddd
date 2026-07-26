import React from 'react';
import ReactDOM from 'react-dom/client';
import './index.css';
import App from './App';
import { loadRuntimeConfig } from './services/api';

// Apply the saved/system theme BEFORE first paint to avoid a flash of the wrong theme.
(() => {
  try {
    const saved = localStorage.getItem('theme');
    const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
    document.documentElement.classList.toggle('dark', saved ? saved === 'dark' : prefersDark);
  } catch { /* ignore */ }
})();

const root = ReactDOM.createRoot(document.getElementById('root'));

// Resolve the API URL from /config.json BEFORE the app (and its first API calls) can render.
// This is the one await in the whole boot sequence — it's a same-origin static file fetch,
// so on a deployed build it resolves in single-digit milliseconds, and loadRuntimeConfig()
// never throws (see its own try/catch), so this can't hang the app on a missing file.
loadRuntimeConfig().finally(() => {
  root.render(
    <React.StrictMode>
      <App />
    </React.StrictMode>
  );
});
