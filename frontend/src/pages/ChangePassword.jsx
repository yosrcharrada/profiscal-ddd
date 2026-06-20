import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import api from '../services/api';
import { useAuth } from '../context/AuthContext';
export default function ChangePassword() {
  const navigate=useNavigate();
  const { applyAuth } = useAuth();
  const [form,setForm]=useState({currentPassword:'',newPassword:'',confirm:''});
  const [errors,setErrors]=useState({}); const [msg,setMsg]=useState({type:'',text:''}); const [loading,setLoading]=useState(false); const [showPw,setShowPw]=useState(false);
  const set=(k)=>(e)=>setForm(f=>({...f,[k]:e.target.value}));
  const handleSubmit=async(e)=>{
    e.preventDefault(); const v={};
    if(!form.currentPassword) v.currentPassword='Required';
    if(!form.newPassword) v.newPassword='Required'; else if(form.newPassword.length<8) v.newPassword='Min 8 chars'; else if(!/[A-Z]/.test(form.newPassword)) v.newPassword='Needs uppercase'; else if(!/[0-9]/.test(form.newPassword)) v.newPassword='Needs number';
    if(form.newPassword===form.currentPassword&&form.newPassword) v.newPassword='Must differ';
    if(form.newPassword!==form.confirm) v.confirm="Doesn't match";
    if(Object.keys(v).length){setErrors(v);return;} setErrors({}); setMsg({type:'',text:''}); setLoading(true);
    try{const {data:res}=await api.post('/Auth/change-password',{currentPassword:form.currentPassword,newPassword:form.newPassword}); applyAuth(res.data); setMsg({type:'ok',text:'Password updated! All other sessions were signed out.'}); setForm({currentPassword:'',newPassword:'',confirm:''}); setTimeout(()=>navigate('/dashboard'),2000);}
    catch(err){setMsg({type:'err',text:err.response?.data?.message||'Failed.'});} finally{setLoading(false);}
  };
  const cls=(k)=>`w-full px-4 py-3.5 rounded-xl border text-sm font-medium placeholder-gray-400 bg-white text-dark focus:outline-none focus:ring-2 focus:ring-brand focus:border-transparent transition-all ${errors[k]?'border-red-400':'border-border'}`;
  return (
    <div className="max-w-lg mx-auto animate-fade-up">
      <h1 className="text-2xl font-extrabold text-dark">Change Password</h1><p className="text-muted mt-1 mb-6">Keep your account secure.</p>
      <div className="bg-white rounded-2xl border border-border p-8">
        {msg.text&&<div className={`mb-6 p-4 rounded-xl text-sm font-medium ${msg.type==='ok'?'bg-green-50 border border-green-200 text-green-700':'bg-red-50 border border-red-200 text-red-600'}`}>{msg.text}</div>}
        <form onSubmit={handleSubmit} className="space-y-5" noValidate>
          {[['currentPassword','Current password','Enter current password'],['newPassword','New password','Min 8 chars, 1 uppercase, 1 number'],['confirm','Confirm new password','Re-enter']].map(([k,l,p])=>(
            <div key={k}><label className="block text-sm font-semibold text-dark mb-2">{l}</label><input type={showPw?'text':'password'} value={form[k]} onChange={set(k)} placeholder={p} className={cls(k)}/>{errors[k]&&<p className="mt-1 text-xs text-red-500">{errors[k]}</p>}</div>
          ))}
          <div className="flex items-center gap-2"><input type="checkbox" id="s" checked={showPw} onChange={()=>setShowPw(!showPw)} className="rounded border-gray-300 text-brand focus:ring-brand"/><label htmlFor="s" className="text-sm text-muted">Show passwords</label></div>
          <div className="flex gap-3 pt-2">
            <button type="button" onClick={()=>navigate(-1)} className="flex-1 py-3.5 border border-border text-body font-semibold rounded-xl hover:bg-light transition-colors">Cancel</button>
            <button type="submit" disabled={loading} className="flex-1 py-3.5 bg-brand text-dark font-semibold rounded-xl hover:shadow-lg hover:shadow-brand/50 disabled:opacity-50 transition-all flex items-center justify-center gap-2">{loading&&<span className="w-4 h-4 border-2 border-dark border-t-transparent rounded-full animate-spin"/>}{loading?'Updating...':'Update'}</button>
          </div>
        </form>
      </div>
    </div>
  );
}
