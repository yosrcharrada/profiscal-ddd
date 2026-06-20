import { useCallback, useEffect, useState } from 'react';
import fiscalService from '../services/fiscalService';

/**
 * Polls the fiscal engine's aggregate health so the UI can show what's connected:
 * Neo4j knowledge base, the LLM, and the embedding server.
 */
export default function useSystemHealth({ poll = 0 } = {}) {
  const [state, setState] = useState({ data: null, loading: true });

  const refresh = useCallback(async () => {
    try {
      const { data } = await fiscalService.health();
      setState({ data: data.data, loading: false });
    } catch {
      setState({ data: { neo4j: false, chunks: 0, llmConfigured: false, embedServer: false, ready: false }, loading: false });
    }
  }, []);

  useEffect(() => {
    refresh();
    if (poll > 0) {
      const t = setInterval(refresh, poll);
      return () => clearInterval(t);
    }
  }, [refresh, poll]);

  return { ...state, refresh };
}
