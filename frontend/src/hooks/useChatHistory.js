import { useCallback, useEffect, useState } from 'react';
import { useAuth } from '../context/AuthContext';

/* Local conversation store for the assistant — Claude-style history.
   Persisted per user in localStorage until the API exposes chat sessions.
   Conversation: { id, title, createdAt, updatedAt, messages: [{ role, content, sources?, error? }] } */

const MAX_CONVERSATIONS = 50;

const keyFor = (user) => `taxmind.chats.${user?.id || user?.email || 'anon'}`;

const load = (key) => {
  try {
    const list = JSON.parse(localStorage.getItem(key));
    return Array.isArray(list) ? list : [];
  } catch {
    return [];
  }
};

const newId = () =>
  (typeof crypto !== 'undefined' && crypto.randomUUID)
    ? crypto.randomUUID()
    : `${Date.now()}-${Math.random().toString(36).slice(2)}`;

const titleFrom = (q = '') => {
  const t = q.replace(/\s+/g, ' ').trim();
  return t.length > 64 ? `${t.slice(0, 61)}…` : t || 'Nouvelle conversation';
};

export default function useChatHistory() {
  const { user } = useAuth();
  const storageKey = keyFor(user);
  const [conversations, setConversations] = useState(() => load(storageKey));

  // switch store when the signed-in user changes
  useEffect(() => {
    setConversations(load(storageKey));
  }, [storageKey]);

  useEffect(() => {
    try {
      localStorage.setItem(storageKey, JSON.stringify(conversations));
    } catch {
      /* quota exceeded — keep the session in memory only */
    }
  }, [conversations, storageKey]);

  /** Create a conversation from its first messages; returns the new id. */
  const create = useCallback((messages) => {
    const id = newId();
    const now = Date.now();
    const firstUser = messages.find((m) => m.role === 'user');
    const conv = {
      id,
      title: titleFrom(firstUser?.content),
      createdAt: now,
      updatedAt: now,
      messages,
    };
    setConversations((prev) => [conv, ...prev].slice(0, MAX_CONVERSATIONS));
    return id;
  }, []);

  const update = useCallback((id, messages) => {
    setConversations((prev) =>
      prev.map((c) =>
        c.id === id ? { ...c, messages, updatedAt: Date.now() } : c,
      ),
    );
  }, []);

  const remove = useCallback((id) => {
    setConversations((prev) => prev.filter((c) => c.id !== id));
  }, []);

  const rename = useCallback((id, title) => {
    setConversations((prev) =>
      prev.map((c) => (c.id === id ? { ...c, title: titleFrom(title) } : c)),
    );
  }, []);

  return { conversations, create, update, remove, rename };
}
