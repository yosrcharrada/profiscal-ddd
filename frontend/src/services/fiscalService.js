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

  /**
   * Streaming legal chatbot (Server-Sent Events). Invokes the callbacks as events
   * arrive: onStatus({phase,text}), onSources([...]), onToken(text), onDone({elapsedMs}),
   * onError(err). Returns once the stream completes.
   */
  chatStream: async ({ question, history = [] }, cbs = {}) => {
    const { onStatus, onToken, onSources, onDone, onError, signal } = cbs;
    const token = localStorage.getItem('accessToken');
    let resp;
    try {
      resp = await fetch(`${api.defaults.baseURL}/fiscal/chat/stream`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...(token ? { Authorization: `Bearer ${token}` } : {}),
        },
        body: JSON.stringify({ question, history }),
        signal,
      });
    } catch (e) { onError?.(e); return; }

    if (!resp.ok || !resp.body) {
      onError?.(new Error(`stream HTTP ${resp?.status}`));
      return;
    }

    const reader = resp.body.getReader();
    const decoder = new TextDecoder();
    let buf = '';
    let finished = false;

    const emit = (evName, dataStr) => {
      let payload = dataStr;
      try { payload = JSON.parse(dataStr); } catch { /* keep raw */ }
      if (evName === 'status') onStatus?.(payload);
      else if (evName === 'token') onToken?.(payload?.text ?? '');
      else if (evName === 'sources') onSources?.(Array.isArray(payload) ? payload : []);
      else if (evName === 'done') { finished = true; onDone?.(payload || {}); }
      else if (evName === 'error') onError?.(new Error(payload?.message || 'stream error'));
    };

    try {
      for (;;) {
        const { value, done } = await reader.read();
        if (done) break;
        buf += decoder.decode(value, { stream: true });
        let idx;
        while ((idx = buf.indexOf('\n\n')) >= 0) {
          const raw = buf.slice(0, idx);
          buf = buf.slice(idx + 2);
          let evName = null;
          let dataStr = '';
          for (const line of raw.split('\n')) {
            if (line.startsWith('event:')) evName = line.slice(6).trim();
            else if (line.startsWith('data:')) dataStr += line.slice(5).replace(/^ /, '');
          }
          if (evName) emit(evName, dataStr);
        }
      }
    } catch (e) { if (!finished) onError?.(e); }
    if (!finished) onDone?.({});
  },

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
