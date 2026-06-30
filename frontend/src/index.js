import React from 'react';
import ReactDOM from 'react-dom/client';
import './index.css';
import App from './App';

// Apply the saved/system theme BEFORE first paint to avoid a flash of the wrong theme.
(() => {
  try {
    const saved = localStorage.getItem('theme');
    const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
    document.documentElement.classList.toggle('dark', saved ? saved === 'dark' : prefersDark);
  } catch { /* ignore */ }
})();

const root = ReactDOM.createRoot(document.getElementById('root'));
root.render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
);
