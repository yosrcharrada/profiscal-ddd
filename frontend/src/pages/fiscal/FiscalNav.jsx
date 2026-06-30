import { NavLink } from 'react-router-dom';
import SystemStatus from './SystemStatus';

const tabs = [
  { to: '/app/search', label: 'Search' },
  { to: '/app/chat', label: 'Chatbot' },
  { to: '/app/consultations', label: 'Consultations' },
];

/** Tab strip + engine status, shared across the fiscal workspace pages. */
export default function FiscalNav() {
  return (
    <div className="flex items-center justify-between gap-4 mb-6 flex-wrap">
      <div className="flex items-center gap-1.5 bg-white border border-border rounded-2xl p-1.5 w-fit">
        {tabs.map((t) => (
          <NavLink
            key={t.to}
            to={t.to}
            className={({ isActive }) =>
              `px-4 py-2 rounded-xl text-sm font-semibold transition-all ${
                isActive ? 'bg-dark text-white' : 'text-body hover:text-dark hover:bg-light'
              }`
            }
          >
            {t.label}
          </NavLink>
        ))}
      </div>
      <SystemStatus />
    </div>
  );
}
