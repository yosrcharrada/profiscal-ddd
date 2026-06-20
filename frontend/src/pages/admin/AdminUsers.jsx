import { useCallback, useEffect, useState } from 'react';
import { adminService } from '../../services/authService';
import { useAuth } from '../../context/AuthContext';

const fmtDate = (d) => (d ? new Date(d).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' }) : '—');

const EVENT_STYLES = {
  LoginSucceeded:     'bg-green-50 text-green-700 border-green-200',
  LoginFailed:        'bg-red-50 text-red-600 border-red-200',
  AccountLockedOut:   'bg-red-50 text-red-600 border-red-200',
  TokenReuseDetected: 'bg-red-50 text-red-600 border-red-200',
  RoleChanged:        'bg-brand/20 text-dark border-brand/40',
  AccountLocked:      'bg-red-50 text-red-600 border-red-200',
  AccountUnlocked:    'bg-green-50 text-green-700 border-green-200',
};

export default function AdminUsers() {
  const { user: me } = useAuth();
  const [data, setData]       = useState(null);
  const [page, setPage]       = useState(1);
  const [search, setSearch]   = useState('');
  const [query, setQuery]     = useState('');
  const [busyId, setBusyId]   = useState(null);
  const [error, setError]     = useState('');
  const [expanded, setExpanded] = useState(null);
  const [activity, setActivity] = useState({});

  const load = useCallback(async () => {
    try {
      const { data: res } = await adminService.getUsers({ page, pageSize: 10, search: query });
      setData(res.data);
      setError('');
    } catch (err) {
      setError(err.response?.data?.message || 'Failed to load users.');
    }
  }, [page, query]);

  useEffect(() => { load(); }, [load]);

  const run = async (id, fn) => {
    setBusyId(id); setError('');
    try { await fn(); await load(); }
    catch (err) { setError(err.response?.data?.message || 'Action failed.'); }
    finally { setBusyId(null); }
  };

  const toggleActivity = async (id) => {
    if (expanded === id) { setExpanded(null); return; }
    setExpanded(id);
    if (!activity[id]) {
      try {
        const { data: res } = await adminService.activity(id, 15);
        setActivity((a) => ({ ...a, [id]: res.data }));
      } catch {
        setActivity((a) => ({ ...a, [id]: [] }));
      }
    }
  };

  return (
    <div className="space-y-6 animate-fade-up">
      <div className="flex flex-col sm:flex-row sm:items-end sm:justify-between gap-4">
        <div>
          <h1 className="text-2xl font-extrabold text-dark">Users</h1>
          <p className="text-muted mt-1">Manage roles, access, and activity across the workspace.</p>
        </div>
        <form onSubmit={(e) => { e.preventDefault(); setPage(1); setQuery(search); }} className="flex gap-2">
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search name or email…"
            className="w-64 px-4 py-2.5 rounded-xl border border-border bg-white text-sm font-medium placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent transition-all"
          />
          <button type="submit" className="px-4 py-2.5 bg-dark text-white text-sm font-semibold rounded-xl hover:bg-black transition-colors">Search</button>
        </form>
      </div>

      {error && <div className="p-4 bg-red-50 border border-red-200 rounded-xl text-red-600 text-sm">{error}</div>}

      <div className="bg-white rounded-2xl border border-border overflow-hidden">
        <div className="hidden md:grid grid-cols-[2fr,1fr,1fr,1.2fr,1.4fr] gap-4 px-6 py-3 border-b border-border bg-light text-[11px] font-bold text-muted uppercase tracking-widest">
          <span>User</span><span>Role</span><span>Status</span><span>Last login</span><span className="text-right">Actions</span>
        </div>

        {!data && <div className="p-10 text-center text-muted text-sm">Loading…</div>}
        {data?.items?.length === 0 && <div className="p-10 text-center text-muted text-sm">No users found.</div>}

        {data?.items?.map((u) => {
          const isMe = u.id === me?.id;
          const role = u.roles?.[0] ?? 'User';
          return (
            <div key={u.id} className="border-b border-border last:border-0">
              <div className="grid md:grid-cols-[2fr,1fr,1fr,1.2fr,1.4fr] gap-3 md:gap-4 px-6 py-4 items-center">
                <div className="flex items-center gap-3 min-w-0">
                  <span className={`w-9 h-9 rounded-xl flex items-center justify-center text-xs font-bold shrink-0 ${role === 'Admin' ? 'bg-dark text-brand' : 'bg-brand text-dark'}`}>
                    {`${u.firstName?.[0] ?? ''}${u.lastName?.[0] ?? ''}`.toUpperCase()}
                  </span>
                  <div className="min-w-0">
                    <p className="text-sm font-bold text-dark truncate">{u.firstName} {u.lastName} {isMe && <span className="text-[10px] font-bold text-muted">(you)</span>}</p>
                    <p className="text-xs text-muted truncate">{u.email}</p>
                  </div>
                </div>

                <div>
                  <select
                    value={role}
                    disabled={isMe || busyId === u.id}
                    onChange={(e) => run(u.id, () => adminService.updateRole(u.id, e.target.value))}
                    className="text-sm font-semibold text-dark bg-white border border-border rounded-lg px-2.5 py-1.5 focus:outline-none focus:ring-2 focus:ring-brand disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    <option value="User">User</option>
                    <option value="Admin">Admin</option>
                  </select>
                </div>

                <div>
                  {u.isLockedOut
                    ? <span className="inline-flex items-center gap-1.5 text-[11px] font-bold text-red-600 bg-red-50 border border-red-200 rounded-full px-2.5 py-1">● Locked</span>
                    : <span className="inline-flex items-center gap-1.5 text-[11px] font-bold text-green-700 bg-green-50 border border-green-200 rounded-full px-2.5 py-1">● Active</span>}
                </div>

                <div className="text-xs text-body">{fmtDate(u.lastLoginAt)}</div>

                <div className="flex md:justify-end gap-2">
                  <button
                    onClick={() => toggleActivity(u.id)}
                    className="text-xs font-semibold text-body border border-border rounded-lg px-3 py-1.5 hover:border-dark/30 hover:text-dark transition-colors"
                  >
                    {expanded === u.id ? 'Hide activity' : 'Activity'}
                  </button>
                  {!isMe && (u.isLockedOut ? (
                    <button
                      disabled={busyId === u.id}
                      onClick={() => run(u.id, () => adminService.unlock(u.id))}
                      className="text-xs font-semibold text-green-700 border border-green-200 bg-green-50 rounded-lg px-3 py-1.5 hover:bg-green-100 disabled:opacity-50 transition-colors"
                    >Unlock</button>
                  ) : (
                    <button
                      disabled={busyId === u.id}
                      onClick={() => run(u.id, () => adminService.lock(u.id))}
                      className="text-xs font-semibold text-red-600 border border-red-200 bg-red-50 rounded-lg px-3 py-1.5 hover:bg-red-100 disabled:opacity-50 transition-colors"
                    >Lock</button>
                  ))}
                </div>
              </div>

              {expanded === u.id && (
                <div className="px-6 pb-5 animate-scale-in origin-top">
                  <div className="bg-light rounded-xl border border-border p-4">
                    <p className="text-[11px] font-bold text-muted uppercase tracking-widest mb-3">Recent activity</p>
                    {!activity[u.id] && <p className="text-xs text-muted">Loading…</p>}
                    {activity[u.id]?.length === 0 && <p className="text-xs text-muted">No recorded activity.</p>}
                    <ul className="space-y-2">
                      {activity[u.id]?.map((a, i) => (
                        <li key={i} className="flex flex-wrap items-center gap-2 text-xs">
                          <span className={`font-bold border rounded-full px-2 py-0.5 ${EVENT_STYLES[a.event] || 'bg-white text-body border-border'}`}>{a.event}</span>
                          {a.detail && <span className="text-body">{a.detail}</span>}
                          <span className="text-muted ml-auto">{a.ipAddress ?? ''} · {fmtDate(a.createdAt)}</span>
                        </li>
                      ))}
                    </ul>
                  </div>
                </div>
              )}
            </div>
          );
        })}
      </div>

      {data && data.totalPages > 1 && (
        <div className="flex items-center justify-between">
          <p className="text-xs text-muted">{data.totalCount} users · page {data.page} of {data.totalPages}</p>
          <div className="flex gap-2">
            <button disabled={page <= 1} onClick={() => setPage((p) => p - 1)}
              className="px-4 py-2 text-sm font-semibold border border-border rounded-xl bg-white disabled:opacity-40 hover:border-dark/30 transition-colors">Previous</button>
            <button disabled={page >= data.totalPages} onClick={() => setPage((p) => p + 1)}
              className="px-4 py-2 text-sm font-semibold border border-border rounded-xl bg-white disabled:opacity-40 hover:border-dark/30 transition-colors">Next</button>
          </div>
        </div>
      )}
    </div>
  );
}
