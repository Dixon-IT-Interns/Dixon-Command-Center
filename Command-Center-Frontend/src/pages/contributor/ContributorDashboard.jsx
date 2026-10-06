import { useEffect, useMemo, useState } from "react";
import { Link } from "react-router-dom";
import { ArrowRight, CheckCircle2, Clock3, FileClock, RefreshCw, XCircle } from "lucide-react";
import { api } from "../../api";
import ContributorLayout from "../../components/contributor/ContributorLayout";
import "./ContributorDashboard.css";

export default function ContributorDashboard({ session, onSignOut }) {
  const [data, setData] = useState(null); const [error, setError] = useState(""); const [loading, setLoading] = useState(true); const [now, setNow] = useState(new Date());
  const load = () => { setLoading(true); api.get("/contributor/dashboard?days=7").then(setData).catch(e=>setError(e.message)).finally(()=>setLoading(false)); };
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(load, []);
  useEffect(() => { const timer = setInterval(() => setNow(new Date()), 1000); return () => clearInterval(timer); }, []);
  const maxProduction = useMemo(()=>Math.max(...(data?.trend||[]).map(x=>Number(x.production)||0),1),[data]);
  return <ContributorLayout user={session?.user} onSignOut={onSignOut}>
    <main className="contributor-dashboard"><header className="cd-header"><div><span className="cd-kicker">CONTRIBUTOR WORKSPACE</span><h1>Good to see you, {session?.user?.fullName?.split(" ")[0] || "there"}.</h1><p>Track today's reporting, approvals and your recent factory submissions.</p></div><button className="cd-refresh" onClick={load}><RefreshCw size={15}/> Refresh</button></header>
    {error&&<div className="cd-error">{error}</div>}
    <section className="cd-stat-grid">{[["Approved",data?.counts?.approved||0,CheckCircle2,"green"],["Pending approval",data?.counts?.submitted||0,Clock3,"indigo"],["Rejected",data?.counts?.rejected||0,XCircle,"red"],["Drafts",data?.counts?.draft||0,FileClock,"gray"]].map(([label,value,Icon,tone])=><div className="cd-stat" key={label}><span className={`cd-stat-icon ${tone}`}><Icon size={17}/></span><div><small>{label}</small><strong>{loading?"—":value}</strong></div></div>)}</section>
    <section className="cd-grid"><div className="cd-panel cd-trend"><div className="cd-panel-head"><div><span>LAST 7 DAYS</span><h2>Reporting activity</h2></div><Link to="/contributor/brands">Open reporting <ArrowRight size={14}/></Link></div><div className="bar-chart">{(data?.trend||[]).map(day=><div className="bar-day" key={day.date}><div className="bar-track"><div className="bar-fill" style={{height:`${Math.max(4,(Number(day.production)/maxProduction)*100)}%`}}/></div><strong>{day.approved}</strong><small>{new Date(`${day.date}T00:00:00`).toLocaleDateString("en-IN",{weekday:"short"})}</small></div>)}</div></div>
    <div className="cd-panel cd-clock"><span>TODAY</span><strong>{now.toLocaleDateString("en-IN",{day:"2-digit",month:"short",year:"numeric"})}</strong><div className="cd-time">{now.toLocaleTimeString("en-IN",{hour:"2-digit",minute:"2-digit",second:"2-digit"})}</div><p>Use the reporting workspace to submit each line. Rejected entries return to you with the Approver's comment.</p><Link className="cd-primary" to="/contributor/brands">Start / continue reporting <ArrowRight size={15}/></Link></div></section>
    <section className="cd-panel cd-recent"><div className="cd-panel-head"><div><span>RECENT SUBMISSIONS</span><h2>Your reporting history</h2></div></div><div className="recent-list">{(data?.recent||[]).map(item=><div className="recent-row" key={item.dailyReportId}><div className="recent-date">{new Date(`${item.reportDate}T00:00:00`).toLocaleDateString("en-IN",{day:"2-digit",month:"short"})}</div><div className="recent-main"><strong>{item.customerName} · {item.categoryName}</strong><span>{item.lineName}{item.modelName?` · ${item.modelName}`:""}</span></div><span className={`recent-status ${item.status.toLowerCase()}`}>{item.status}</span>{item.rejectionReason&&<span className="reject-note">{item.rejectionReason}</span>}</div>)}{!loading&&!data?.recent?.length&&<div className="cd-empty">No submissions yet.</div>}</div></section></main>
  </ContributorLayout>;
}
