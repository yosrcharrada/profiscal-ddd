import { useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import EYLockup from '../components/common/EYLockup';

export default function Register() {
  const { register } = useAuth();
  const navigate = useNavigate();
  const [form, setForm] = useState({ firstName:'', lastName:'', email:'', password:'', confirm:'' });
  const [errors, setErrors] = useState({});
  const [apiError, setApiError] = useState('');
  const [loading, setLoading] = useState(false);
  const [showPw, setShowPw] = useState(false);
  const set = (k) => (e) => setForm((f) => ({ ...f, [k]: e.target.value }));
  const validate = () => {
    const e = {};
    if (!form.firstName.trim()) e.firstName='Required'; if (!form.lastName.trim()) e.lastName='Required';
    if (!form.email) e.email='Required'; else if (!/\S+@\S+\.\S+/.test(form.email)) e.email='Invalid';
    if (!form.password) e.password='Required'; else if (form.password.length<8) e.password='Min 8 chars'; else if (!/[A-Z]/.test(form.password)) e.password='Needs uppercase'; else if (!/[0-9]/.test(form.password)) e.password='Needs number';
    if (form.password!==form.confirm) e.confirm="Doesn't match"; return e;
  };
  const handleSubmit = async (e) => {
    e.preventDefault(); const v=validate();
    if (Object.keys(v).length) { setErrors(v); return; }
    setErrors({}); setApiError(''); setLoading(true);
    try { await register(form.firstName, form.lastName, form.email, form.password); navigate('/dashboard',{replace:true}); }
    catch (err) { setApiError(err.response?.data?.message||'Registration failed.'); } finally { setLoading(false); }
  };
  const pw=form.password; const str=!pw?0:[pw.length>=8,/[A-Z]/.test(pw),/[0-9]/.test(pw),/[^A-Za-z0-9]/.test(pw)].filter(Boolean).length;
  const strC=['bg-gray-200','bg-red-400','bg-orange-400','bg-blue-400','bg-green-400'][str];
  const strL=['','Weak','Fair','Good','Strong'][str];
  const cls=(k)=>`w-full px-4 py-3.5 rounded-xl border text-sm font-medium placeholder-gray-400 bg-white text-dark focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent transition-all ${errors[k]?'border-red-400':'border-border'}`;

  return (
    <div className="min-h-screen flex">
      <div className="hidden lg:flex w-[480px] bg-dark p-12 flex-col justify-between relative overflow-hidden">
        <div className="absolute top-0 left-0 w-full h-[3px] bg-brand" /><div className="absolute -bottom-20 -left-20 w-80 h-80 bg-brand/15 rounded-full blur-3xl" />
        <Link to="/" className="relative inline-flex"><EYLockup /></Link>
        <div className="relative">
          <h2 className="font-display italic text-4xl text-white leading-tight">Start your journey.</h2>
          <p className="mt-4 text-gray-400 text-lg">Create your account and get instant access to AI-powered tax consultation tools.</p>
          <div className="mt-8 space-y-3">
            {['Free trial — no credit card','Search thousands of legal texts','Generate opinions in seconds'].map((t)=>(
              <div key={t} className="flex items-center gap-3"><div className="w-5 h-5 rounded-full bg-brand/15 flex items-center justify-center"><div className="w-1.5 h-1.5 bg-brand rounded-full"/></div><span className="text-sm text-gray-400">{t}</span></div>
            ))}
          </div>
        </div>
        <p className="relative text-sm text-gray-600">&copy; {new Date().getFullYear()} EY Taxmind</p>
      </div>
      <div className="flex-1 bg-cream flex items-center justify-center p-6">
        <div className="w-full max-w-md animate-fade-up">
          <div className="lg:hidden mb-8"><Link to="/" className="inline-flex"><EYLockup dark compact /></Link></div>
          <h1 className="text-2xl font-extrabold text-dark">Create account</h1>
          <p className="mt-2 text-body">Already registered? <Link to="/login" className="text-dark font-semibold hover:underline">Sign in</Link></p>
          {apiError && <div className="mt-6 p-4 bg-red-50 border border-red-200 rounded-xl text-red-600 text-sm">{apiError}</div>}
          <form onSubmit={handleSubmit} className="mt-8 space-y-4" noValidate>
            <div className="grid grid-cols-2 gap-4">
              {[['firstName','First name','Nadia'],['lastName','Last name','Trabelsi']].map(([k,l,p])=>(<div key={k}><label className="block text-sm font-semibold text-dark mb-2">{l}</label><input type="text" placeholder={p} value={form[k]} onChange={set(k)} className={cls(k)}/>{errors[k]&&<p className="mt-1 text-xs text-red-500">{errors[k]}</p>}</div>))}
            </div>
            <div><label className="block text-sm font-semibold text-dark mb-2">Email</label><input type="email" placeholder="you@company.com" value={form.email} onChange={set('email')} autoComplete="email" className={cls('email')}/>{errors.email&&<p className="mt-1 text-xs text-red-500">{errors.email}</p>}</div>
            <div><label className="block text-sm font-semibold text-dark mb-2">Password</label>
              <div className="relative"><input type={showPw?'text':'password'} placeholder="Min 8 chars, 1 uppercase, 1 number" value={form.password} onChange={set('password')} autoComplete="new-password" className={`${cls('password')} pr-12`}/>
                <button type="button" onClick={()=>setShowPw(!showPw)} className="absolute right-4 top-1/2 -translate-y-1/2 text-muted hover:text-dark"><svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>{showPw?<path strokeLinecap="round" strokeLinejoin="round" d="M3.98 8.223A10.477 10.477 0 001.934 12C3.226 16.338 7.244 19.5 12 19.5c.993 0 1.953-.138 2.863-.395M6.228 6.228A10.45 10.45 0 0112 4.5c4.756 0 8.773 3.162 10.065 7.498a10.523 10.523 0 01-4.293 5.774M6.228 6.228L3 3m3.228 3.228l3.65 3.65m7.894 7.894L21 21m-3.228-3.228l-3.65-3.65m0 0a3 3 0 10-4.243-4.243m4.242 4.242L9.88 9.88"/>:<><path strokeLinecap="round" strokeLinejoin="round" d="M2.036 12.322a1.012 1.012 0 010-.639C3.423 7.51 7.36 4.5 12 4.5c4.638 0 8.573 3.007 9.963 7.178.07.207.07.431 0 .639C20.577 16.49 16.64 19.5 12 19.5c-4.638 0-8.573-3.007-9.963-7.178z"/><path strokeLinecap="round" strokeLinejoin="round" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z"/></>}</svg></button>
              </div>
              {pw&&<div className="mt-2 flex items-center gap-2"><div className="flex-1 h-1.5 rounded-full overflow-hidden flex gap-0.5">{[1,2,3,4].map(n=><div key={n} className={`flex-1 rounded-full transition-all duration-300 ${n<=str?strC:'bg-gray-200'}`}/>)}</div><span className="text-xs text-muted font-medium w-10">{strL}</span></div>}
              {errors.password&&<p className="mt-1 text-xs text-red-500">{errors.password}</p>}
            </div>
            <div><label className="block text-sm font-semibold text-dark mb-2">Confirm password</label><input type="password" placeholder="Re-enter password" value={form.confirm} onChange={set('confirm')} autoComplete="new-password" className={cls('confirm')}/>{errors.confirm&&<p className="mt-1 text-xs text-red-500">{errors.confirm}</p>}</div>
            <button type="submit" disabled={loading} className="w-full py-3.5 bg-brand text-dark font-semibold rounded-xl hover:shadow-lg hover:shadow-brand/50 disabled:opacity-50 transition-all hover:-translate-y-0.5 flex items-center justify-center gap-2 mt-2">
              {loading&&<span className="w-4 h-4 border-2 border-dark border-t-transparent rounded-full animate-spin"/>}{loading?'Creating...':'Create Account'}
            </button>
          </form>
        </div>
      </div>
    </div>
  );
}
