import { BrowserRouter, Route, Routes } from 'react-router-dom';
import Layout from './components/layout/Layout';
import ProtectedRoute from './components/common/ProtectedRoute';
import { AuthProvider } from './context/AuthContext';
import { ToastProvider } from './components/common/Toast';
import AdminUsers from './pages/admin/AdminUsers';
import ChangePassword from './pages/ChangePassword';
import Dashboard from './pages/Dashboard';
import Landing from './pages/Landing';
import Login from './pages/Login';
import NotFound from './pages/NotFound';
import Register from './pages/Register';
import Settings from './pages/Settings';
import Search from './pages/fiscal/Search';
import Chat from './pages/fiscal/Chat';
import Consultations from './pages/fiscal/Consultations';
import ConsultationEditor from './pages/fiscal/ConsultationEditor';

function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <ToastProvider>
        <Routes>
          <Route path="/" element={<Landing />} />
          <Route path="/login" element={<Login />} />
          <Route path="/register" element={<Register />} />
          <Route element={<ProtectedRoute><Layout /></ProtectedRoute>}>
            <Route path="/dashboard" element={<Dashboard />} />
            <Route path="/app/search" element={<Search />} />
            <Route path="/app/chat" element={<Chat />} />
            <Route path="/app/consultations" element={<Consultations />} />
            <Route path="/app/consultations/:id" element={<ConsultationEditor />} />
            <Route path="/settings" element={<Settings />} />
            <Route path="/settings/password" element={<ChangePassword />} />
            <Route path="/admin/users" element={<ProtectedRoute roles={['Admin']}><AdminUsers /></ProtectedRoute>} />
          </Route>
          <Route path="*" element={<NotFound />} />
        </Routes>
        </ToastProvider>
      </AuthProvider>
    </BrowserRouter>
  );
}

export default App;
