import { useEffect, useMemo, useState } from "react";
import { Download, RefreshCw } from "lucide-react";
import { api } from "../../api";
import Header from "../../components/Header";
import AppFooter from "../../components/AppFooter";
import "./ViewerDashboard.css";

const today = new Date().toISOString().slice(0, 10);

export default function ViewerDashboard({ session, onSignOut }) {
  const [date, setDate] = useState(today);
  const [customerId, setCustomerId] = useState("");
  const [process, setProcess] = useState("ALL");
  const [trendDays, setTrendDays] = useState(7);
  const [customers, setCustomers] = useState([]);
  const [data, setData] = useState({ summary: {}, lines: [] });
  const [trend, setTrend] = useState([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");

  const query = useMemo(() => {
    const q = new URLSearchParams({ reportDate: date, process });
    if (customerId) q.set("customerId", customerId);
    return q.toString();
  }, [date, customerId, process, trendDays]);

  async function load() {
    setLoading(true); setError("");
    try {
      const end = new Date(`${date}T00:00:00`);
      const start = new Date(end); start.setDate(end.getDate() - (trendDays - 1));
      const trendQuery = new URLSearchParams({
        startDate: start.toISOString().slice(0, 10),
        endDate: date,
        process,
      });
      if (customerId) trendQuery.set("customerId", customerId);
      const [progress, trendRows] = await Promise.all([
        api.get(`/viewer/progress?${query}`),
        api.get(`/viewer/trend?${trendQuery.toString()}`),
      ]);
      setData(progress); setTrend(trendRows);
    } catch (e) { setError(e.message); }
    finally { setLoading(false); }
  }

  useEffect(() => {
    api.get("/catalog/customers").then(setCustomers).catch(() => {});
  }, []);

  useEffect(() => { load(); }, [query, trendDays]);

  async function downloadExcel() {
    try {
      const token = localStorage.getItem("dcc-token");
      const base = import.meta.env.VITE_API_URL || "http://localhost:5211/api";
      const response = await fetch(`${base}/viewer/export?${query}`, { headers: { Authorization: `Bearer ${token}` } });
      if (!response.ok) throw new Error("Excel download failed.");
      const blob = await response.blob();
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a"); a.href = url; a.download = `Dixon_Viewer_${date.replaceAll("-", "")}.xlsx`; a.click();
      URL.revokeObjectURL(url);
    } catch (e) { setError(e.message); }
  }

  const maxTrend = Math.max(1, ...trend.map(x => Math.max(Number(x.productionPlan), Number(x.productionActual))));
  const s = data.summary || {};

  return <div className="viewer-page">
    <Header user={session?.user} onSignOut={onSignOut} />
    <main className="viewer-main">
      <div className="viewer-title-row">
        <div><span className="eyebrow">READ ONLY</span><h1>FATP & SMT Work Progress</h1><p>Production visibility from the existing Command Center data.</p></div>
        <div className="viewer-actions"><button className="secondary-btn" onClick={load} disabled={loading}><RefreshCw size={16}/> Refresh</button><button className="primary-btn" onClick={downloadExcel}><Download size={16}/> Excel</button></div>
      </div>

      <section className="filters">
        <label>Report Date<input type="date" value={date} onChange={e => setDate(e.target.value)} /></label>
        <label>Customer<select value={customerId} onChange={e => setCustomerId(e.target.value)}><option value="">All Customers</option>{customers.map(c => <option key={c.customerId} value={c.customerId}>{c.customerName}</option>)}</select></label>
        <label>Process<select value={process} onChange={e => setProcess(e.target.value)}><option>ALL</option><option>FATP</option><option>SMT</option></select></label>
        <label>Trend Range<select value={trendDays} onChange={e => setTrendDays(Number(e.target.value))}>{Array.from({ length: 30 }, (_, i) => i + 1).map(days => <option key={days} value={days}>{days} {days === 1 ? "Day" : "Days"}</option>)}</select></label>
      </section>

      {error && <div className="viewer-error">{error}</div>}

      <section className="cards">
        <Card label="Production Plan" value={fmt(s.productionPlan)} />
        <Card label="Production Actual" value={fmt(s.productionActual)} />
        <Card label="Achievement" value={`${Number(s.achievement || 0).toFixed(1)}%`} />
        <Card label="Lines" value={`${s.completedLines || 0} / ${s.totalLines || 0}`} sub="completed" />
      </section>

      <section className="viewer-grid">
        <div className="panel trend-panel"><div className="panel-head"><div><h2>Plan vs Actual Trend</h2><small className="panel-subtitle">{trendDays}-day production performance</small></div><span>{process}</span></div><TrendChart data={trend} maxValue={maxTrend} days={trendDays} /><div className="legend"><span><i className="dot plan-dot"/> Plan</span><span><i className="dot actual-dot"/> Actual</span></div></div>
        <div className="panel"><div className="panel-head"><h2>Achievement by Line</h2></div><div className="achievement-list">{data.lines.map(line => <div className="achievement-row" key={`${line.customerId}-${line.lineId}`}><div><strong>{line.lineName}</strong><small>{line.customerName} · {line.process}</small></div><div className="progress-wrap"><div className="progress-track"><div className="progress-fill" style={{width:`${Math.min(100, Number(line.achievement || 0))}%`}}/></div><b>{Number(line.achievement || 0).toFixed(1)}%</b></div></div>)}</div></div>
      </section>

      <section className="panel table-panel"><div className="panel-head"><h2>Line Work Progress</h2><span>{data.lines.length} lines</span></div><div className="table-scroll"><table><thead><tr><th>Customer</th><th>Process</th><th>Line</th><th>Status</th><th>Plan</th><th>Actual</th><th>Achievement</th><th>Open WO</th><th>7+ Days WO</th></tr></thead><tbody>{data.lines.map(line => <tr key={`${line.customerId}-${line.lineId}`}><td>{line.customerName}</td><td><span className={`process ${line.process.toLowerCase()}`}>{line.process}</span></td><td><strong>{line.lineName}</strong></td><td><span className={`status ${line.status.toLowerCase().replaceAll("_", "-")}`}>{line.status}</span></td><td>{fmt(line.productionPlan)}</td><td>{fmt(line.productionActual)}</td><td>{Number(line.achievement || 0).toFixed(1)}%</td><td>{line.openWOQty}</td><td>{line.over7DaysWOBalanceQty}</td></tr>)}</tbody></table>{!data.lines.length && !loading && <div className="empty">No production records found for the selected filters.</div>}</div></section>
    </main>
    <AppFooter />
  </div>;
}

function TrendChart({ data, maxValue, days }) {
  if (!data.length) return <div className="trend-empty">No trend data for the selected range.</div>;

  const width = 900;
  const height = 250;
  const pad = { top: 18, right: 22, bottom: 38, left: 48 };
  const chartWidth = width - pad.left - pad.right;
  const chartHeight = height - pad.top - pad.bottom;
  const safeMax = Math.max(1, Number(maxValue) || 1);
  const x = i => pad.left + (data.length === 1 ? chartWidth / 2 : (i / (data.length - 1)) * chartWidth);
  const y = value => pad.top + chartHeight - ((Number(value) || 0) / safeMax) * chartHeight;
  const points = key => data.map((row, i) => `${x(i)},${y(row[key])}`).join(" ");
  const labelStep = data.length <= 7 ? 1 : Math.ceil(data.length / 7);
  const gridValues = [0, .25, .5, .75, 1];

  return <div className="trend-visual" style={{"--trend-width": `${Math.max(100, days * 24)}px`}}>
    <svg viewBox={`0 0 ${width} ${height}`} role="img" aria-label={`${days}-day production plan versus actual trend`} preserveAspectRatio="none">
      {gridValues.map(ratio => <g key={ratio}>
        <line x1={pad.left} x2={width - pad.right} y1={y(safeMax * ratio)} y2={y(safeMax * ratio)} className="trend-grid-line" />
        <text x={pad.left - 8} y={y(safeMax * ratio) + 4} textAnchor="end" className="trend-axis-label">{fmt(safeMax * ratio)}</text>
      </g>)}
      {data.length > 1 && <polyline points={points("productionPlan")} className="trend-line plan-line" fill="none" />}
      {data.length > 1 && <polyline points={points("productionActual")} className="trend-line actual-line" fill="none" />}
      {data.map((row, i) => <g key={row.date}>
        <circle cx={x(i)} cy={y(row.productionPlan)} r={data.length > 20 ? 2 : 3.5} className="trend-point plan-point"><title>{row.date}: Plan {fmt(row.productionPlan)}</title></circle>
        <circle cx={x(i)} cy={y(row.productionActual)} r={data.length > 20 ? 2 : 3.5} className="trend-point actual-point"><title>{row.date}: Actual {fmt(row.productionActual)}</title></circle>
        {(i % labelStep === 0 || i === data.length - 1) && <text x={x(i)} y={height - 12} textAnchor="middle" className="trend-date-label">{row.date.slice(5)}</text>}
      </g>)}
    </svg>
  </div>;
}

function Card({ label, value, sub }) { return <div className="viewer-card"><span>{label}</span><strong>{value}</strong>{sub && <small>{sub}</small>}</div>; }
function fmt(v) { return Number(v || 0).toLocaleString("en-IN", { maximumFractionDigits: 0 }); }