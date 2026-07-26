import axios from 'axios';

/*
 * The API URL is resolved at RUNTIME from /config.json (see loadRuntimeConfig below and its
 * call in index.js), not baked in at build time. A build-time REACT_APP_API_URL bakes the URL
 * into the compiled JS — fine on a dev machine, but wrong for a shipped IIS deployment: if the
 * client's API ends up on a different port than whatever was set when `npm run build` ran, the
 * frontend can never reach it without a full rebuild + re-ship. /config.json is a static file
 * CRA copies to the build output untouched, so ops can edit it on the client machine (change
 * apiUrl, no rebuild) exactly like appsettings.json on the backend side.
 *
 * REACT_APP_API_URL is kept as the dev-time default so `npm start` still works with zero setup.
 */
const DEV_DEFAULT = process.env.REACT_APP_API_URL || 'http://localhost:5131/api';

/* No default Content-Type on purpose.
   Axios already sets `application/json` for plain-object payloads, so pinning it here changes
   nothing for JSON calls — but it BREAKS file uploads: axios 1.x's transformRequest reads
   `hasJSONContentType` and, when it is set, serialises a FormData body to a JSON string
   (`JSON.stringify(formDataToJSON(data))`) instead of sending multipart. The file is lost and
   the server answers 415. Letting axios infer the type keeps both cases correct. */
const api = axios.create({ baseURL: DEV_DEFAULT });

/**
 * Fetch /config.json and point the api instance at its `apiUrl`. Called once from index.js
 * before the app renders. Failure (file missing, bad JSON, network error) is silent and keeps
 * the DEV_DEFAULT — this must never block the app from loading, since a missing config.json is
 * a legitimate state on a dev machine that never got one.
 */
export async function loadRuntimeConfig() {
  try {
    const res = await fetch('/config.json', { cache: 'no-store' });
    if (!res.ok) return;
    const cfg = await res.json();
    if (cfg?.apiUrl) api.defaults.baseURL = cfg.apiUrl;
  } catch {
    // Keep DEV_DEFAULT — see comment above.
  }
}

api.interceptors.request.use((config) => {
  const token = localStorage.getItem('accessToken');
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

/* Refresh tokens rotate on every use — a second concurrent refresh with the same
   token would trip the server's reuse detection and kill all sessions. So all
   401s share a single in-flight refresh promise. */
let refreshPromise = null;

async function refreshTokens() {
  const refreshToken = localStorage.getItem('refreshToken');
  const accessToken = localStorage.getItem('accessToken');
  if (!refreshToken || !accessToken) throw new Error('No tokens');
  // Reads api.defaults.baseURL (live, set by loadRuntimeConfig) rather than a module-load
  // constant, so this keeps working after the runtime config overrides the dev default.
  const { data } = await axios.post(`${api.defaults.baseURL}/Auth/refresh`, { accessToken, refreshToken });
  localStorage.setItem('accessToken', data.data.accessToken);
  localStorage.setItem('refreshToken', data.data.refreshToken);
  localStorage.setItem('user', JSON.stringify(data.data.user));
  return data.data.accessToken;
}

api.interceptors.response.use(
  (res) => res,
  async (err) => {
    const original = err.config;
    if (err.response?.status === 401 && !original._retry) {
      original._retry = true;
      try {
        refreshPromise = refreshPromise || refreshTokens().finally(() => { refreshPromise = null; });
        const newToken = await refreshPromise;
        original.headers.Authorization = `Bearer ${newToken}`;
        return api(original);
      } catch {
        localStorage.clear();
        window.location.href = '/login';
      }
    }
    return Promise.reject(err);
  }
);

export default api;
