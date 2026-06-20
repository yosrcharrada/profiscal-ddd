import { createContext, useCallback, useContext, useEffect, useState } from 'react';
import authService from '../services/authService';

const AuthContext = createContext(null);

export function AuthProvider({ children }) {
  const [user, setUser]       = useState(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const hydrate = async () => {
      const token  = localStorage.getItem('accessToken');
      const stored = localStorage.getItem('user');
      if (!token || !stored) { setLoading(false); return; }

      try { setUser(JSON.parse(stored)); } catch { localStorage.clear(); setLoading(false); return; }

      // Re-validate against the server so roles/lockout changes take effect.
      try {
        const { data: res } = await authService.me();
        localStorage.setItem('user', JSON.stringify(res.data));
        setUser(res.data);
      } catch {
        // 401 handling (refresh / redirect) is done by the axios interceptor.
      } finally {
        setLoading(false);
      }
    };
    hydrate();
  }, []);

  const login = useCallback(async (email, password) => {
    const { data: res } = await authService.login({ email, password });
    _persist(res.data);
    return res.data;
  }, []);

  const register = useCallback(async (firstName, lastName, email, password) => {
    const { data: res } = await authService.register({ firstName, lastName, email, password });
    _persist(res.data);
    return res.data;
  }, []);

  const logout = useCallback(async (everywhere = false) => {
    const refreshToken = localStorage.getItem('refreshToken');
    try { await authService.logout(refreshToken, everywhere); } catch {}
    localStorage.clear();
    setUser(null);
  }, []);

  /** Persist a fresh AuthResponse (e.g. after change-password rotates all tokens). */
  const applyAuth = useCallback((authData) => _persist(authData), []);

  function _persist(authData) {
    localStorage.setItem('accessToken',  authData.accessToken);
    localStorage.setItem('refreshToken', authData.refreshToken);
    localStorage.setItem('user',         JSON.stringify(authData.user));
    setUser(authData.user);
  }

  const hasRole = useCallback((role) => !!user?.roles?.includes(role), [user]);

  return (
    <AuthContext.Provider value={{
      user, loading, login, register, logout, applyAuth, hasRole,
      isAuthenticated: !!user,
      isAdmin: !!user?.roles?.includes('Admin'),
    }}>
      {children}
    </AuthContext.Provider>
  );
}

export const useAuth = () => {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
};
