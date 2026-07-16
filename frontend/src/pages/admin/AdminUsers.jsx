import { useCallback, useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { adminService } from '../../services/authService';
import { useAuth } from '../../context/AuthContext';
import { useLanguage } from '../../context/LanguageContext';
import AdminLayout from '../../components/admin/AdminLayout';

const fmtDate = (d) => (d ? new Date(d).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' }) : '—');

const EVENT_STYLES = {
  LoginSucceeded:     'bg-green-50 text-green-700 border-green-200',
  LoginFailed:        'bg-red-50 text-red-600 border-red-200',
  AccountLockedOut:   'bg-red-50 text-red-600 border-red-200',
  TokenReuseDetected: 'bg-red-50 text-red-600 border-red-200',
  RoleChanged:        'bg-brand/20 text-dark border-brand/40',
  UserProvisioned:    'bg-brand/20 text-dark border-brand/40',
  ManagerAssigned:    'bg-blue-50 text-blue-700 border-blue-200',
  AccountLocked:      'bg-red-50 text-red-600 border-red-200',
  AccountUnlocked:    'bg-green-50 text-green-700 border-green-200',
};

const ROLE_BADGE = {
  Admin: 'bg-dark text-brand',
  Manager: 'bg-brand text-dark',
  Consultant: 'bg-brand/25 text-dark',
  User: 'bg-light text-body',
};

/* ───────────── Inline create-user panel (no popup) ───────────── */
function CreateUserPanel({ managers, onClose, onCreated, t }) {
  const [form, setForm] = useState({ firstName: '', lastName: '', email: '', role: 'Consultant', managerId: '' });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [result, setResult] = useState(null); // CreateUserResponse
  const [copied, setCopied] = useState(false);
  const set = (k) => (e) => setForm((f) => ({ ...f, [k]: e.target.value }));

  const submit = async (e) => {
    e.preventDefault();
    setError('');
    if (!form.firstName.trim() || !form.lastName.trim()) { setError(t('admin.users.new.nameRequired')); return; }
    // The backend enforces the allowed email domains (Registration:AllowedDomains) —
    // here we only check the shape so its error message stays the single source of truth.
    if (!/^[^@\s]+@[^@\s]+\.[^@\s]+$/.test(form.email.trim())) { setError(t('admin.users.new.emailInvalid')); return; }
    if (form.role === 'Consultant' && !form.managerId) { setError(t('admin.users.new.managerRequired')); return; }
    setBusy(true);
    try {
      const { data: res } = await adminService.createUser({
        firstName: form.firstName.trim(),
        lastName: form.lastName.trim(),
        email: form.email.trim(),
        role: form.role,
        managerId: form.managerId || null,
      });
      setResult(res.data);
      onCreated();
    } catch (err) {
      setError(err.response?.data?.message || t('admin.users.new.failed'));
    } finally {
      setBusy(false);
    }
  };

  const copyPassword = async () => {
    try { await navigator.clipboard.writeText(result.temporaryPassword); setCopied(true); setTimeout(() => setCopied(false), 1600); } catch {}
  };

  const inputCls = 'w-full px-3 py-2.5 rounded-lg border border-border/60 bg-white text-[13px] font-medium text-dark placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent transition-all';

  return (
    <div className="bg-cream rounded-xl border border-blue-200 overflow-hidden animate-scale-in origin-top shadow-card">
      <div className="h-[3px] bg-gradient-to-r from-blue-500 via-brand to-emerald-500" />
      <div className="p-4 sm:p-5">
        {!result ? (
          <>
            <div className="flex items-start justify-between">
              <div className="flex items-center gap-2.5">
                <span className="w-9 h-9 rounded-lg bg-blue-50 text-blue-700 flex items-center justify-center">
                  <svg className="w-4.5 h-4.5 w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}><path strokeLinecap="round" strokeLinejoin="round" d="M18 7.5v3m0 0v3m0-3h3m-3 0h-3m-2.25-4.125a3.375 3.375 0 11-6.75 0 3.375 3.375 0 016.75 0zM3 19.235v-.11a6.375 6.375 0 0112.75 0v.109A12.318 12.318 0 019.374 21c-2.331 0-4.512-.645-6.374-1.766z" /></svg>
                </span>
                <div>
                  <h2 className="text-[14px] font-bold text-dark">{t('admin.users.new.title')}</h2>
                  <p className="text-[12px] text-muted">{t('admin.users.new.subtitle')}</p>
                </div>
              </div>
              <button onClick={onClose} className="w-7 h-7 rounded-md text-muted hover:text-dark hover:bg-light flex items-center justify-center transition-colors" aria-label="Close">
                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" /></svg>
              </button>
            </div>

            {error && <div className="mt-3 p-2.5 bg-red-50 border border-red-200 rounded-lg text-red-600 text-[12px]">{error}</div>}

            {/* Horizontal field row — collapses to a grid on small screens */}
            <form onSubmit={submit} className="mt-4">
              <div className="grid sm:grid-cols-2 lg:grid-cols-5 gap-3">
                <div>
                  <label className="block text-[11px] font-semibold text-muted uppercase tracking-[0.08em] mb-1.5">{t('admin.users.new.firstName')}</label>
                  <input value={form.firstName} onChange={set('firstName')} placeholder="Prénom" className={inputCls} />
                </div>
                <div>
                  <label className="block text-[11px] font-semibold text-muted uppercase tracking-[0.08em] mb-1.5">{t('admin.users.new.lastName')}</label>
                  <input value={form.lastName} onChange={set('lastName')} placeholder="Nom" className={inputCls} />
                </div>
                <div>
                  <label className="block text-[11px] font-semibold text-muted uppercase tracking-[0.08em] mb-1.5">{t('admin.users.new.email')}</label>
                  <input type="email" value={form.email} onChange={set('email')} placeholder="prenom.nom@esprit.tn" className={inputCls} />
                </div>
                <div>
                  <label className="block text-[11px] font-semibold text-muted uppercase tracking-[0.08em] mb-1.5">{t('admin.users.new.role')}</label>
                  <select
                    value={form.role}
                    onChange={(e) => setForm((f) => ({ ...f, role: e.target.value, managerId: e.target.value === 'Consultant' ? f.managerId : '' }))}
                    className={inputCls}
                  >
                    <option value="Consultant">Consultant</option>
                    <option value="Manager">Manager</option>
                    <option value="Admin">Admin</option>
                  </select>
                </div>
                {form.role === 'Consultant' ? (
                  <div>
                    <label className="block text-[11px] font-semibold text-muted uppercase tracking-[0.08em] mb-1.5">
                      {t('admin.users.new.manager')}
                    </label>
                    <select value={form.managerId} onChange={set('managerId')} className={inputCls}>
                      <option value="">—</option>
                      {managers.map((m) => (
                        <option key={m.id} value={m.id}>{m.firstName} {m.lastName}</option>
                      ))}
                    </select>
                  </div>
                ) : (
                  <div className="flex items-end">
                    <p className="text-[11px] text-muted italic pb-2.5">{t('admin.users.new.noManagerRole')}</p>
                  </div>
                )}
              </div>

              <div className="mt-3 flex flex-col sm:flex-row sm:items-center gap-3">
                <div className="flex items-start gap-2 p-2.5 bg-brand/10 border border-brand/30 rounded-lg flex-1">
                  <svg className="w-4 h-4 mt-0.5 shrink-0 text-dark" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}><path strokeLinecap="round" strokeLinejoin="round" d="M21.75 6.75v10.5a2.25 2.25 0 01-2.25 2.25h-15a2.25 2.25 0 01-2.25-2.25V6.75m19.5 0A2.25 2.25 0 0019.5 4.5h-15a2.25 2.25 0 00-2.25 2.25m19.5 0v.243a2.25 2.25 0 01-1.07 1.916l-7.5 4.615a2.25 2.25 0 01-2.36 0L3.32 8.91a2.25 2.25 0 01-1.07-1.916V6.75" /></svg>
                  <p className="text-[11px] text-dark/80 leading-relaxed">{t('admin.users.new.emailInfo')}</p>
                </div>
                <button type="submit" disabled={busy} className="shrink-0 px-5 py-2.5 bg-brand text-dark text-[13px] font-bold rounded-lg hover:shadow-lg hover:shadow-brand/40 disabled:opacity-50 transition-all flex items-center justify-center gap-2">
                  {busy && <span className="w-3.5 h-3.5 border-2 border-dark border-t-transparent rounded-full animate-spin" />}
                  {busy ? t('admin.users.new.creating') : t('admin.users.new.create')}
                </button>
              </div>
            </form>
          </>
        ) : (
          <div className="flex flex-col sm:flex-row sm:items-center gap-4 animate-fade-in">
            <div className="flex items-center gap-3 flex-1 min-w-0">
              <span className="w-10 h-10 rounded-full bg-green-50 border border-green-200 flex items-center justify-center shrink-0">
                <svg className="w-5 h-5 text-green-600" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path strokeLinecap="round" strokeLinejoin="round" d="M4.5 12.75l6 6 9-13.5" /></svg>
              </span>
              <div className="min-w-0">
                <h2 className="text-[14px] font-bold text-dark">{t('admin.users.new.createdTitle')}</h2>
                <p className="text-[12px] text-muted truncate">
                  {result.user.email} · {result.user.roles?.[0]}
                  {result.user.managerName ? ` · ${t('admin.users.manager')}: ${result.user.managerName}` : ''}
                </p>
              </div>
            </div>

            {result.emailSent ? (
              <p className="text-[12px] text-body bg-green-50 border border-green-200 rounded-lg px-3 py-2">
                ✉️ {t('admin.users.new.emailSent')}
              </p>
            ) : (
              <div className="bg-brand/10 border border-brand/40 rounded-lg p-3 sm:max-w-md">
                <p className="text-[11px] font-semibold text-dark">{t('admin.users.new.emailNotSent')}</p>
                <div className="mt-1.5 flex items-center gap-2">
                  <code className="flex-1 bg-white border border-border/60 rounded-md px-2.5 py-1.5 text-[13px] font-bold text-dark tracking-wider">{result.temporaryPassword}</code>
                  <button onClick={copyPassword} className="px-2.5 py-1.5 bg-dark text-white text-[11px] font-semibold rounded-md hover:bg-black transition-colors">
                    {copied ? t('admin.users.new.copied') : t('admin.users.new.copy')}
                  </button>
                </div>
                <p className="mt-1.5 text-[10px] text-muted">{t('admin.users.new.passwordOnce')}</p>
              </div>
            )}

            <button onClick={onClose} className="shrink-0 px-4 py-2.5 bg-dark text-white text-[13px] font-semibold rounded-lg hover:bg-black transition-colors">
              {t('admin.users.new.done')}
            </button>
          </div>
        )}
      </div>
    </div>
  );
}

/* ───────────── Page ───────────── */
export default function AdminUsers() {
  const { user: me } = useAuth();
  const { t } = useLanguage();
  const [params, setParams] = useSearchParams();
  const [data, setData]       = useState(null);
  const [page, setPage]       = useState(1);
  const [search, setSearch]   = useState('');
  const [query, setQuery]     = useState('');
  const [roleFilter, setRoleFilter] = useState('');
  const [busyId, setBusyId]   = useState(null);
  const [error, setError]     = useState('');
  const [expanded, setExpanded] = useState(null);
  const [confirmDelete, setConfirmDelete] = useState(null); // user id armed for deletion
  const [activity, setActivity] = useState({});
  const [managers, setManagers] = useState([]);
  const [showCreate, setShowCreate] = useState(params.get('new') === '1');

  const load = useCallback(async () => {
    try {
      const { data: res } = await adminService.getUsers({ page, pageSize: 10, search: query, role: roleFilter });
      setData(res.data);
      setError('');
    } catch (err) {
      setError(err.response?.data?.message || 'Failed to load users.');
    }
  }, [page, query, roleFilter]);

  const loadManagers = useCallback(async () => {
    try {
      const { data: res } = await adminService.managers();
      setManagers(res.data);
    } catch { /* dropdown stays empty */ }
  }, []);

  useEffect(() => { load(); }, [load]);
  useEffect(() => { loadManagers(); }, [loadManagers]);

  const run = async (id, fn) => {
    setBusyId(id); setError('');
    try { await fn(); await load(); await loadManagers(); }
    catch (err) { setError(err.response?.data?.message || 'Action failed.'); }
    finally { setBusyId(null); }
  };

  // Two-step delete: first click arms the button, second confirms. Auto-disarm.
  useEffect(() => {
    if (!confirmDelete) return;
    const timer = setTimeout(() => setConfirmDelete(null), 3000);
    return () => clearTimeout(timer);
  }, [confirmDelete]);

  const deleteUser = (u) => {
    if (confirmDelete !== u.id) { setConfirmDelete(u.id); return; }
    setConfirmDelete(null);
    run(u.id, () => adminService.removeUser(u.id));
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

  const closeCreate = () => {
    setShowCreate(false);
    if (params.get('new')) { params.delete('new'); setParams(params, { replace: true }); }
  };

  const newUserTab = {
    id: 'new-user',
    label: t('admin.users.new.tab'),
    active: showCreate,
    color: 'text-blue-500',
    activeBg: 'bg-blue-500',
    onActivate: () => setShowCreate(true),
    onClose: closeCreate,
    iconPath: 'M18 7.5v3m0 0v3m0-3h3m-3 0h-3m-2.25-4.125a3.375 3.375 0 11-6.75 0 3.375 3.375 0 016.75 0zM3 19.235v-.11a6.375 6.375 0 0112.75 0v.109A12.318 12.318 0 019.374 21c-2.331 0-4.512-.645-6.374-1.766z',
  };

  return (
    <AdminLayout extraTabs={showCreate ? [newUserTab] : []}>
      {/* Active "New user" onglet → its content replaces the list, like a browser tab */}
      {showCreate ? (
        <CreateUserPanel
          managers={managers}
          onClose={closeCreate}
          onCreated={() => { load(); loadManagers(); }}
          t={t}
        />
      ) : (
      <div className="space-y-4 animate-fade-up">
      <div className="flex flex-col sm:flex-row sm:items-end sm:justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold text-dark tracking-tight">{t("admin.users.title")}</h1>
          <p className="text-[13px] text-muted mt-0.5">{t("admin.users.subtitle")}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <select
            value={roleFilter}
            onChange={(e) => { setPage(1); setRoleFilter(e.target.value); }}
            className="px-2.5 py-2 rounded-lg border border-border/60 bg-white text-[13px] font-medium text-dark focus:outline-none focus:ring-2 focus:ring-brand"
          >
            <option value="">{t('admin.users.allRoles')}</option>
            <option value="Admin">Admin</option>
            <option value="Manager">Manager</option>
            <option value="Consultant">Consultant</option>
          </select>
          <form onSubmit={(e) => { e.preventDefault(); setPage(1); setQuery(search); }} className="flex gap-2">
            <input
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={t("admin.users.search")}
              className="w-44 sm:w-56 px-3 py-2 rounded-lg border border-border/60 bg-white text-[13px] font-medium placeholder-gray-400 focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent transition-all"
            />
            <button type="submit" className="px-3.5 py-2 bg-dark text-white text-[13px] font-semibold rounded-lg hover:bg-black transition-colors">{t("admin.users.searchBtn")}</button>
          </form>
          <button
            onClick={() => setShowCreate(true)}
            className="px-3.5 py-2 text-[13px] font-bold rounded-lg transition-all inline-flex items-center gap-1.5 bg-brand text-dark hover:shadow-lg hover:shadow-brand/40"
          >
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}><path strokeLinecap="round" strokeLinejoin="round" d="M12 4.5v15m7.5-7.5h-15" /></svg>
            {t('admin.users.addUser')}
          </button>
        </div>
      </div>

      {error && <div className="p-3 bg-red-50 border border-red-200 rounded-lg text-red-600 text-[13px]">{error}</div>}

      <div className="bg-white rounded-xl border border-border/60 shadow-card overflow-hidden">
        <div className="hidden md:grid grid-cols-[2fr,1fr,1.2fr,1fr,1.2fr,1.4fr] gap-3 px-4 py-2.5 border-b border-border/40 bg-light/40 text-[10px] font-semibold text-muted uppercase tracking-[0.1em]">
          <span>{t("admin.users.user")}</span><span>{t("admin.users.role")}</span><span>{t("admin.users.manager")}</span><span>{t("admin.users.status")}</span><span>{t("admin.users.lastLogin")}</span><span className="text-right">{t("admin.users.actions")}</span>
        </div>

        {!data && <div className="p-8 text-center text-muted text-[13px]">{t("admin.users.loading")}</div>}
        {data?.items?.length === 0 && <div className="p-8 text-center text-muted text-[13px]">{t("admin.users.noUsers")}</div>}

        {data?.items?.map((u) => {
          const isMe = u.id === me?.id;
          const role = u.roles?.[0] ?? 'Consultant';
          return (
            <div key={u.id} className="border-b border-border/30 last:border-0">
              <div className="grid md:grid-cols-[2fr,1fr,1.2fr,1fr,1.2fr,1.4fr] gap-2 md:gap-3 px-4 py-3 items-center">
                <div className="flex items-center gap-2.5 min-w-0">
                  <span className={`w-8 h-8 rounded-lg flex items-center justify-center text-[10px] font-bold shrink-0 ${ROLE_BADGE[role] || ROLE_BADGE.User}`}>
                    {`${u.firstName?.[0] ?? ''}${u.lastName?.[0] ?? ''}`.toUpperCase()}
                  </span>
                  <div className="min-w-0">
                    <p className="text-[13px] font-semibold text-dark truncate">
                      {u.firstName} {u.lastName} {isMe && <span className="text-[10px] font-medium text-muted">({t("admin.users.you")})</span>}
                      {u.mustChangePassword && <span className="ml-1 text-[9px] font-bold text-orange-600 bg-orange-50 border border-orange-200 rounded px-1 py-px uppercase align-middle">{t('admin.users.pendingFirstLogin')}</span>}
                    </p>
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
                    <option value="Consultant">Consultant</option>
                    <option value="Manager">Manager</option>
                    <option value="Admin">Admin</option>
                    {role === 'User' && <option value="User">User</option>}
                  </select>
                </div>

                <div>
                  {role === 'Consultant' ? (
                    <select
                      value={u.managerId ?? ''}
                      disabled={busyId === u.id}
                      onChange={(e) => run(u.id, () => adminService.assignManager(u.id, e.target.value || null))}
                      className="w-full max-w-[150px] text-[12px] font-medium text-dark bg-white border border-border/60 rounded-md px-2 py-1.5 focus:outline-none focus:ring-2 focus:ring-brand disabled:opacity-50"
                    >
                      <option value="">{t('admin.users.noManager')}</option>
                      {managers.filter((m) => m.id !== u.id).map((m) => (
                        <option key={m.id} value={m.id}>{m.firstName} {m.lastName}</option>
                      ))}
                    </select>
                  ) : (
                    <span className="text-[12px] text-muted">—</span>
                  )}
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
                    <>
                      <button
                        disabled={busyId === u.id}
                        onClick={() => run(u.id, () => adminService.unlock(u.id))}
                        className="text-[11px] font-medium text-green-700 border border-green-200 bg-green-50 rounded-md px-2.5 py-1 hover:bg-green-100 disabled:opacity-50 transition-colors"
                      >{t("admin.users.unlock")}</button>
                      {/* Delete only unlocks once the account is locked — and never for admins. */}
                      {role !== 'Admin' && (
                        <button
                          disabled={busyId === u.id}
                          onClick={() => deleteUser(u)}
                          className={`text-[11px] font-bold rounded-md px-2.5 py-1 disabled:opacity-50 transition-all ${
                            confirmDelete === u.id
                              ? 'text-white bg-red-600 border border-red-600 hover:bg-red-700'
                              : 'text-red-600 border border-red-200 bg-white hover:bg-red-50'
                          }`}
                          title={confirmDelete === u.id ? t('admin.users.deleteConfirm') : t('admin.users.delete')}
                        >
                          {confirmDelete === u.id ? t('admin.users.deleteConfirm') : t('admin.users.delete')}
                        </button>
                      )}
                    </>
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
      )}
    </AdminLayout>
  );
}
