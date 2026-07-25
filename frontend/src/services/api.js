import axios from 'axios';

const BASE_URL = process.env.REACT_APP_API_URL || 'http://localhost:5131/api';

/* No default Content-Type on purpose.
   Axios already sets `application/json` for plain-object payloads, so pinning it here changes
   nothing for JSON calls — but it BREAKS file uploads: axios 1.x's transformRequest reads
   `hasJSONContentType` and, when it is set, serialises a FormData body to a JSON string
   (`JSON.stringify(formDataToJSON(data))`) instead of sending multipart. The file is lost and
   the server answers 415. Letting axios infer the type keeps both cases correct. */
const api = axios.create({ baseURL: BASE_URL });

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
  const { data } = await axios.post(`${BASE_URL}/Auth/refresh`, { accessToken, refreshToken });
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
