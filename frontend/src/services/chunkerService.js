import api from './api';

/**
 * Client for the qEntropy chunking pipeline, used by the admin
 * "Base de connaissances" page.
 *
 * Every call goes to OUR API (`/fiscal`-style routes under `api/chunker`), which proxies to
 * the FastAPI chunker. The browser never talks to the chunker directly: the proxy is what
 * enforces the Admin role and keeps the chunking service off the public surface. See
 * ChunkerController.cs for the rationale.
 *
 * The pipeline is asynchronous by design — a long run must not hold an HTTP request open:
 *
 *     upload(file) ──► { document_id }
 *          └─ run(document_id) ──► { job_id }
 *                  └─ status(job_id) ──► { status, stage, progress }   ← poll
 *                          └─ results(job_id) ──► { chunks, summary, … }
 */
const chunkerService = {
  /** Is the chunking service running? → `{ alive, baseUrl }`. */
  health: () => api.get('/chunker/health'),

  /** Embedding backends the chunker can use (OpenAI only when it has a key). */
  backends: () => api.get('/chunker/backends'),

  /**
   * Upload a document. Sent as multipart under the field name `file`, which is what the
   * FastAPI handler binds.
   *
   * Content-Type is left unset so the browser writes `multipart/form-data` with its own
   * boundary. Setting it — even as an axios instance default — makes axios 1.x serialise the
   * FormData to JSON instead of sending multipart, which the API rejects with 415. See the
   * note in api.js.
   */
  upload: (file) => {
    const form = new FormData();
    form.append('file', file);
    return api.post('/chunker/upload', form);
  },

  /**
   * Start the pipeline for an uploaded document.
   * `config` is the chunker's own JSON config object, forwarded verbatim — `{}` uses its
   * defaults, which is what the admin flow wants (the service owns its tuning contract).
   */
  run: (documentId, config = {}) =>
    api.post(`/chunker/run/${encodeURIComponent(documentId)}`, {
      config: JSON.stringify(config),
    }),

  /** Current stage + progress of a running job. */
  status: (jobId) => api.get(`/chunker/status/${encodeURIComponent(jobId)}`),

  /** Final chunks + document profile + evaluation summary. */
  results: (jobId) => api.get(`/chunker/results/${encodeURIComponent(jobId)}`),
};

/**
 * Run the whole flow and resolve with the results, reporting progress along the way.
 *
 * Polling (rather than a socket) matches the chunker's REST contract and survives the proxy
 * hop. `onProgress({stage, progress})` is called on every tick so the UI can show which of the
 * 8 stages is running.
 *
 * @param {File} file
 * @param {(p: {stage?: string, progress?: number}) => void} onProgress
 * @param {{config?: object, intervalMs?: number, timeoutMs?: number}} [opts]
 */
export async function runChunking(file, onProgress = () => {}, opts = {}) {
  const { config = {}, intervalMs = 1500, timeoutMs = 15 * 60 * 1000 } = opts;

  onProgress({ stage: 'upload', progress: 0 });
  const up = await chunkerService.upload(file);
  const documentId = up?.data?.document_id;
  if (!documentId) throw new Error('upload-failed');

  const started = await chunkerService.run(documentId, config);
  const jobId = started?.data?.job_id;
  if (!jobId) throw new Error('run-failed');

  const deadline = Date.now() + timeoutMs;
  // Poll until the job reports completion. The chunker answers 500 with its own message when
  // the pipeline throws, and axios turns that into a rejection — which is what we want: the
  // real reason surfaces to the caller instead of an endless "running" spinner.
  for (;;) {
    if (Date.now() > deadline) throw new Error('timeout');
    await new Promise((r) => setTimeout(r, intervalMs));

    const st = (await chunkerService.status(jobId))?.data || {};
    onProgress({ stage: st.stage, progress: st.progress });

    if (st.status === 'complete') break;
    if (st.status === 'error') throw new Error(st.message || 'pipeline-error');
  }

  const res = await chunkerService.results(jobId);
  return res?.data;
}

export default chunkerService;
