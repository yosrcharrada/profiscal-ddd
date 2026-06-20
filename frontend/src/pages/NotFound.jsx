import { Link } from 'react-router-dom';
export default function NotFound() {
  return (
    <div className="min-h-screen bg-cream flex items-center justify-center p-6">
      <div className="text-center">
        <div className="font-display italic text-8xl text-dark/10">404</div>
        <h1 className="mt-4 text-2xl font-bold tracking-tight text-dark">Page not found</h1>
        <p className="mt-2 text-muted">The page you're looking for doesn't exist.</p>
        <Link to="/" className="inline-block mt-6 px-7 py-3 bg-brand text-dark font-semibold rounded-full hover:shadow-lg hover:shadow-brand/50 transition-all">Go home</Link>
      </div>
    </div>
  );
}
