import { useCallback, useEffect, useState } from 'react';
import { adminService } from '../../services/authService';
import { useAuth } from '../../context/AuthContext';
import { useLanguage } from '../../context/LanguageContext';

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
  const { t } = useLanguage();
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
    <div className="space-y-4 animate-fade-up">
      <div className="flex flex-col sm:flex-row sm:items-end sm:justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold text-dark tracking-tight">{t("admin.users.title")}</h1>
          <p className="text-[13px] text-muted mt-0.5">{t("admin.users.subtitle")}</p>
        </div>
        <form onSubmit={(e) => { e.preventDefault(); setPage(1); setQuery(search); }} className="flex gap-2">
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder={t("admin.users.search")}
            className="w-56 px-3 py-2 rounded-lg border border-border/60 bg-white text-[13px] font-medium placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent transition-all"
          />
          <button type="submit" className="px-3.5 py-2 bg-dark text-white text-[13px] font-semibold rounded-lg hover:bg-black transition-colors">{t("admin.users.searchBtn")}</button>
        </form>
      </div>

      {error && <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-red-600 text-[13px]">{error}</div>}

      <div className="bg-white rounded-xl border border-border/60 shadow-card overflow-hidden">
        <div className="hidden md:grid grid-cols-[2fr,1fr,1fr,1.2fr,1.4fr] gap-3 px-4 py-2.5 border-b border-border/40 bg-light/40 text-[10px] font-semibold text-muted uppercase tracking-[0.1em]">
          <span>{t("admin.users.user")}</span><span>{t("admin.users.role")}</span><span>{t("admin.users.status")}</span><span>{t("admin.users.lastLogin")}</span><span className="text-right">{t("admin.users.actions")}</span>
        </div>

        {!data && <div className="p-8 text-center text-muted text-[13px]">{t("admin.users.loading")}</div>}
        {data?.items?.length === 0 && <div className="p-8 text-center text-muted text-[13px]">{t("admin.users.noUsers")}</div>}

        {data?.items?.map((u) => {
          const isMe = u.id === me?.id;
          const role = u.roles?.[0] ?? 'User';
          return (
            <div key={u.id} className="border-b border-border/30 last:border-0">
              <div className="grid md:grid-cols-[2fr,1fr,1fr,1.2fr,1.4fr] gap-2 md:gap-3 px-4 py-3 items-center">
                <div className="flex items-center gap-2.5 min-w-0">
                  <span className={`w-8 h-8 rounded-lg flex items-center justify-center text-[10px] font-bold shrink-0 ${role === 'Admin' ? 'bg-dark text-brand' : 'bg-brand/25 text-dark'}`}>
                    {`${u.firstName?.[0] ?? ''}${u.lastName?.[0] ?? ''}`.toUpperCase()}
                  </span>
                  <div className="min-w-0">
                    <p className="text-[13px] font-semibold text-dark truncate">{u.firstName} {u.lastName} {isMe && <span className="text-[10px] font-medium text-muted">({t("admin.users.you")})</span>}</p>
                    <p className="text-[11px] text-muted truncate">{u.email}</p>
                  </div>
                </div>

                <div>
                  <select
                    value={role}
                    disabled={isMe || busyId === u.id}
                    onChange={(e) => run(u.id, () => adminService.updateRole(u.id, e.target.value))}
                    className="text-[12px] font-medium text-dark bg-white border border-border/60 rounded-md px-2 py-1.5 focus:outline-none focus:ring-2 focus:ring-brand disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    <option value="User">User</option>
                    <option value="Admin">Admin</option>
                  </select>
                </div>

                <div>
                  {u.isLockedOut
                    ? <span className="inline-flex items-center gap-1 text-[10px] font-semibold text-red-600 bg-red-50 border border-red-200 rounded-md px-2 py-0.5">{t("admin.users.locked")}</span>
                    : <span className="inline-flex items-center gap-1 text-[10px] font-semibold text-green-700 bg-green-50 border border-green-200 rounded-md px-2 py-0.5">{t("admin.users.active")}</span>}
                </div>

                <div className="text-[11px] text-muted">{fmtDate(u.lastLoginAt)}</div>

                <div className="flex md:justify-end gap-1.5">
                  <button
                    onClick={() => toggleActivity(u.id)}
                    className="text-[11px] font-medium text-body border border-border/60 rounded-md px-2.5 py-1 hover:border-dark/30 hover:text-dark transition-colors"
                  >
                    {expanded === u.id ? t('admin.users.hide') : t('admin.users.activity')}
                  </button>
                  {!isMe && (u.isLockedOut ? (
                    <button
                      disabled={busyId === u.id}
                      onClick={() => run(u.id, () => adminService.unlock(u.id))}
                      className="text-[11px] font-medium text-green-700 border border-green-200 bg-green-50 rounded-md px-2.5 py-1 hover:bg-green-100 disabled:opacity-50 transition-colors"
                    >{t("admin.users.unlock")}</button>
                  ) : (
                    <button
                      disabled={busyId === u.id}
                      onClick={() => run(u.id, () => adminService.lock(u.id))}
                      className="text-[11px] font-medium text-red-600 border border-red-200 bg-red-50 rounded-md px-2.5 py-1 hover:bg-red-100 disabled:opacity-50 transition-colors"
                    >{t("admin.users.lock")}</button>
                  ))}
                </div>
              </div>

              {expanded === u.id && (
                <div className="px-4 pb-3 animate-scale-in origin-top">
                  <div className="bg-light/50 rounded-lg border border-border/40 p-3">
                    <p className="text-[10px] font-semibold text-muted uppercase tracking-[0.12em] mb-2">{t("admin.users.recentActivity")}</p>
                    {!activity[u.id] && <p className="text-[12px] text-muted">{t("admin.users.loading")}</p>}
                    {activity[u.id]?.length === 0 && <p className="text-[12px] text-muted">{t("admin.users.noActivity")}</p>}
                    <ul className="space-y-1.5">
                      {activity[u.id]?.map((a, i) => (
                        <li key={i} className="flex flex-wrap items-center gap-1.5 text-[11px]">
                          <span className={`font-semibold border rounded-md px-1.5 py-0.5 ${EVENT_STYLES[a.event] || 'bg-white text-body border-border'}`}>{a.event}</span>
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
          <p className="text-[11px] text-muted">{data.totalCount} {t("admin.users.usersLabel")} · {t("admin.users.page")} {data.page}/{data.totalPages}</p>
          <div className="flex gap-1.5">
            <button disabled={page <= 1} onClick={() => setPage((p) => p - 1)}
              className="px-3 py-1.5 text-[12px] font-medium border border-border/60 rounded-lg bg-white disabled:opacity-40 hover:border-dark/30 transition-colors">{t("admin.users.previous")}</button>
            <button disabled={page >= data.totalPages} onClick={() => setPage((p) => p + 1)}
              className="px-3 py-1.5 text-[12px] font-medium border border-border/60 rounded-lg bg-white disabled:opacity-40 hover:border-dark/30 transition-colors">{t("admin.users.next")}</button>
          </div>
        </div>
      )}
    </div>
  );
}
