import api from './api';

const authService = {
  register: (data) => api.post('/Auth/register', data),
  login:    (data) => api.post('/Auth/login', data),
  refresh:  (data) => api.post('/Auth/refresh', data),
  logout:   (refreshToken, everywhere = false) =>
    api.post('/Auth/logout', { refreshToken, everywhere }),
  changePassword: (data) => api.post('/Auth/change-password', data),
  me: () => api.get('/Auth/me'),
  sessions: () => api.get('/Auth/sessions', {
    headers: { 'X-Refresh-Token': localStorage.getItem('refreshToken') || '' },
  }),
  revokeSession: (id) => api.delete(`/Auth/sessions/${id}`),
};

export const adminService = {
  getUsers: ({ page = 1, pageSize = 20, search = '' } = {}) =>
    api.get('/Users', { params: { page, pageSize, search: search || undefined } }),
  updateRole: (id, role) => api.put(`/Users/${id}/role`, { role }),
  lock:   (id) => api.post(`/Users/${id}/lock`),
  unlock: (id) => api.post(`/Users/${id}/unlock`),
  activity: (id, take = 20) => api.get(`/Users/${id}/activity`, { params: { take } }),
};

export default authService;
