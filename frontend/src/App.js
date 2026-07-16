import { BrowserRouter, Route, Routes } from 'react-router-dom';
import Layout from './components/layout/Layout';
import ProtectedRoute from './components/common/ProtectedRoute';
import { AuthProvider } from './context/AuthContext';
import { LanguageProvider } from './context/LanguageContext';
import { ToastProvider } from './components/common/Toast';
import AdminOverview from './pages/admin/AdminOverview';
import AdminUsers from './pages/admin/AdminUsers';
import AdminReclamations from './pages/admin/AdminReclamations';
import AdminActivity from './pages/admin/AdminActivity';
import AdminKnowledge from './pages/admin/AdminKnowledge';
import Consultants from './pages/manager/Consultants';
import ConsultantDetail from './pages/manager/ConsultantDetail';
import MyTasks from './pages/tasks/MyTasks';
import Support from './pages/Support';
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
import News from './pages/news/News';

function App() {
  return (
    <BrowserRouter>
      <LanguageProvider>
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
            <Route path="/app/news" element={<News />} />
            <Route path="/app/tasks" element={<MyTasks />} />
            <Route path="/app/support" element={<Support />} />
            <Route path="/settings" element={<Settings />} />
            <Route path="/settings/password" element={<ChangePassword />} />

            {/* Manager space */}
            <Route path="/manager/consultants" element={<ProtectedRoute roles={['Manager', 'Admin']}><Consultants /></ProtectedRoute>} />
            <Route path="/manager/consultants/:id" element={<ProtectedRoute roles={['Manager', 'Admin']}><ConsultantDetail /></ProtectedRoute>} />

            {/* Admin space */}
            <Route path="/admin" element={<ProtectedRoute roles={['Admin']}><AdminOverview /></ProtectedRoute>} />
            <Route path="/admin/users" element={<ProtectedRoute roles={['Admin']}><AdminUsers /></ProtectedRoute>} />
            <Route path="/admin/reclamations" element={<ProtectedRoute roles={['Admin']}><AdminReclamations /></ProtectedRoute>} />
            <Route path="/admin/activity" element={<ProtectedRoute roles={['Admin']}><AdminActivity /></ProtectedRoute>} />
            <Route path="/admin/knowledge" element={<ProtectedRoute roles={['Admin']}><AdminKnowledge /></ProtectedRoute>} />
          </Route>
          <Route path="*" element={<NotFound />} />
        </Routes>
        </ToastProvider>
      </AuthProvider>
      </LanguageProvider>
    </BrowserRouter>
  );
}

export default App;
