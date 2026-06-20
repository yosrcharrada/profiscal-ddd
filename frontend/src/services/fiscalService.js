import api from './api';

/** Fiscal engine API — semantic search, legal chatbot, consultations, refinement. */
const fiscalService = {
  // Knowledge base
  health:       () => api.get('/fiscal/health'),
  stats:        () => api.get('/fiscal/stats'),
  statsHealth:  () => api.get('/fiscal/stats/health'),
  searchHealth: () => api.get('/fiscal/search/health'),

  // Semantic search engine
  search: (body) => api.post('/fiscal/search', {
    query: body.query,
    docType: body.docType || 'all',
    chunkType: body.chunkType || 'all',
    yearMin: body.yearMin || 2000,
    yearMax: body.yearMax || 2030,
    size: body.size || 30,
  }),

  // Legal chatbot
  chat: (question, history = []) => api.post('/fiscal/chat', { question, history }),

  // Consultations
  generate: (body) => api.post('/fiscal/consultations/generate', body),
  list:     (search = '', all = false) => api.get('/fiscal/consultations', { params: { search: search || undefined, all } }),
  get:      (id) => api.get(`/fiscal/consultations/${id}`),
  saveOutput: (id, output) => api.put(`/fiscal/consultations/${id}/output`, output),
  rate:     (consultationId, reference, stars, comment) =>
              api.post('/fiscal/consultations/rate', { consultationId, reference, stars, comment }),
  exportDocx: (body) => api.post('/fiscal/consultations/export', body, { responseType: 'blob' }),

  // Refinement (editable sections)
  startSession: (consultationId, clientName, reference) =>
                  api.post('/fiscal/refine/session/start', { consultationId, clientName, reference }),
  refine: (body) => api.post('/fiscal/refine/message', body),
  endSession: (sessionId) => api.post('/fiscal/refine/session/end', { sessionId }),
};

export default fiscalService;
