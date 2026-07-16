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
  getUsers: ({ page = 1, pageSize = 20, search = '', role = '' } = {}) =>
    api.get('/Users', { params: { page, pageSize, search: search || undefined, role: role || undefined } }),
  createUser: (data) => api.post('/Users', data), // { firstName, lastName, email, role, managerId }
  updateRole: (id, role) => api.put(`/Users/${id}/role`, { role }),
  assignManager: (id, managerId) => api.put(`/Users/${id}/manager`, { managerId }),
  managers: () => api.get('/Users/managers'),
  overview: () => api.get('/Users/overview'),
  globalActivity: (take = 60) => api.get('/Users/activity', { params: { take } }),
  lock:   (id) => api.post(`/Users/${id}/lock`),
  unlock: (id) => api.post(`/Users/${id}/unlock`),
  removeUser: (id) => api.delete(`/Users/${id}`),
  activity: (id, take = 20) => api.get(`/Users/${id}/activity`, { params: { take } }),
};

/** Manager ↔ consultant task workflow. */
export const taskService = {
  create: (data) => api.post('/tasks', data),
  get: (id) => api.get(`/tasks/${id}`),
  assigned: ({ consultantId, status } = {}) =>
    api.get('/tasks/assigned', { params: { consultantId, status } }),
  consultants: () => api.get('/tasks/consultants'),
  consultantDetail: (id) => api.get(`/tasks/consultants/${id}`),
  mine: () => api.get('/tasks/mine'),
  start: (id) => api.post(`/tasks/${id}/start`),
  submit: (id, { consultationId, note } = {}) => api.post(`/tasks/${id}/submit`, { consultationId, note }),
  approve: (id) => api.post(`/tasks/${id}/approve`),
  reopen: (id) => api.post(`/tasks/${id}/reopen`),
  remove: (id) => api.delete(`/tasks/${id}`),
  addCollaborator: (id, email) => api.post(`/tasks/${id}/collaborators`, { email }),
};

/** Bug reports / reclamations. */
export const reclamationService = {
  create: (data) => api.post('/reclamations', data), // { subject, description, category }
  mine: () => api.get('/reclamations/mine'),
  all: (status) => api.get('/reclamations', { params: { status: status || undefined } }),
  resolve: (id, adminNote) => api.post(`/reclamations/${id}/resolve`, { adminNote }),
  reopen: (id) => api.post(`/reclamations/${id}/reopen`),
};

/** In-app notifications (navbar bell). */
export const notificationService = {
  mine: (take = 30) => api.get('/notifications', { params: { take } }),
  read: (id) => api.post(`/notifications/${id}/read`),
  readAll: () => api.post('/notifications/read-all'),
};

/** Live JORT feed scraped from iort.gov.tn. */
export const jortService = {
  activities: (take = 20) => api.get('/jort/activities', { params: { take } }),
  refresh: () => api.post('/jort/refresh'),
  /** Mint a fresh iort.gov.tn session link (French). target: 'latest' | 'home'. */
  open: (target = 'latest') => api.get('/jort/open', { params: { target } }),
  /** Official PDF of one JORT text (streamed through the backend session replay). */
  pdf: (id) => api.get(`/jort/activities/${id}/pdf`, { responseType: 'blob' }),
};

export default authService;
