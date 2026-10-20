import { useCallback, useEffect, useMemo, useState } from "react";
import { Download, RefreshCw, X, Search, RotateCcw, ChevronLeft, ChevronRight } from "lucide-react";
import { api } from "../../api";
import Header from "../../components/Header";
import AppFooter from "../../components/AppFooter";
import "./viewerDashboard.css";
import "./ViewerProgress.css";

const today = new Date(Date.now() - new Date().getTimezoneOffset() * 60_000).toISOString().slice(0, 10);

export default function ViewerDashboard({ session, onSignOut }) {
  const [date, setDate] = useState(today);
  const [plantId, setPlantId] = useState("");
  const [plants, setPlants] = useState([]);
  const [plantsLoading, setPlantsLoading] = useState(true);
  const [customerId, setCustomerId] = useState("");
  const [process, setProcess] = useState("ALL");
  const [trendDays, setTrendDays] = useState(7);
  const [customers, setCustomers] = useState([]);
  const [data, setData] = useState({ summary: {}, lines: [] });
  const [trend, setTrend] = useState([]);
  const [loading, setLoading] = useState(true);
  const [exportOpen, setExportOpen] = useState(false);
  const [exportPlantId, setExportPlantId] = useState("");
  const [exporting, setExporting] = useState(false);
  const [error, setError] = useState("");
  const [searchTerm, setSearchTerm] = useState("");
  const [statusFilter, setStatusFilter] = useState("ALL");
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(10);

  const query = useMemo(() => {
    const q = new URLSearchParams({ reportDate: date, process });
    if (plantId) q.set("plantId", plantId);
    if (customerId) q.set("customerId", customerId);
    return q.toString();
  }, [date, plantId, customerId, process]);

  const fetchDashboardData = useCallback(() => {
    const end = new Date(`${date}T00:00:00Z`);
    const start = new Date(end); start.setUTCDate(end.getUTCDate() - (trendDays - 1));
    const trendQuery = new URLSearchParams({
      startDate: start.toISOString().slice(0, 10),
      endDate: date,
      process,
    });
    if (plantId) trendQuery.set("plantId", plantId);
    if (customerId) trendQuery.set("customerId", customerId);
    return Promise.all([
      api.get(`/viewer/progress?${query}`),
      api.get(`/viewer/trend?${trendQuery.toString()}`),
    ]);
  }, [date, plantId, trendDays, process, customerId, query]);

  async function load() {
    setLoading(true);
    setError("");
    try {
      const [progress, trendRows] = await fetchDashboardData();
      setData(progress);
      setTrend(trendRows);
    } catch (e) { setError(e.message); }
    finally { setLoading(false); }
  }

  useEffect(() => {
    api.get("/viewer/plants")
      .then(rows => {
        setPlants(rows);
        const assignedPlant = rows.find(plant => String(plant.plantId) === String(session?.user?.plantId));
        setPlantId(String(assignedPlant?.plantId || rows[0]?.plantId || ""));
        if (!rows.length) setLoading(false);
      })
      .catch(e => {
        setError(e.message);
        setLoading(false);
      })
      .finally(() => setPlantsLoading(false));
  }, [session?.user?.plantId]);

  useEffect(() => {
    if (!plantId) return;
    let isCurrent = true;
    api.get(`/viewer/customers?plantId=${encodeURIComponent(plantId)}`)
      .then(rows => {
        if (isCurrent) setCustomers(rows);
      })
      .catch(e => {
        if (isCurrent) setError(e.message);
      });
    return () => { isCurrent = false; };
  }, [plantId]);

  useEffect(() => {
    if (!plantId) return;
    let isCurrent = true;
    fetchDashboardData()
      .then(([progress, trendRows]) => {
        if (!isCurrent) return;
        setData(progress);
        setTrend(trendRows);
        setError("");
      })
      .catch(e => {
        if (isCurrent) setError(e.message);
      })
      .finally(() => {
        if (isCurrent) setLoading(false);
      });
    return () => { isCurrent = false; };
  }, [fetchDashboardData, plantId]);

  function openExport() {
    setExportPlantId(plantId);
    setExportOpen(true);
  }

  async function downloadExcel(event) {
    event.preventDefault();
    if (!exportPlantId) return;
    setExporting(true);
    try {
      const token = localStorage.getItem("dcc-token");
      const base = import.meta.env.VITE_API_URL || "http://localhost:5211/api";
      const exportQuery = new URLSearchParams({ reportDate: date, process, plantId: exportPlantId });
      if (exportPlantId === plantId && customerId) exportQuery.set("customerId", customerId);
      const response = await fetch(`${base}/viewer/export?${exportQuery.toString()}`, { headers: { Authorization: `Bearer ${token}` } });
      if (!response.ok) throw new Error("Excel download failed.");
      const blob = await response.blob();
      const url = URL.createObjectURL(blob);
      const selectedPlant = plants.find(plant => String(plant.plantId) === String(exportPlantId));
      const plantFileName = String(selectedPlant?.plantName || "Plant").replace(/[^a-z0-9-_]+/gi, "_");
      const a = document.createElement("a"); a.href = url; a.download = `Dixon_${plantFileName}_${date.replaceAll("-", "")}.xlsx`; a.click();
      URL.revokeObjectURL(url);
      setExportOpen(false);
    } catch (e) { setError(e.message); }
    finally { setExporting(false); }
  }

  const maxTrend = Math.max(1, ...trend.map(x => Math.max(Number(x.productionPlan), Number(x.productionActual))));
  const achievementLines = useMemo(() => {
    const byLine = new Map();
    data.lines.forEach(line => {
      const key = `${line.customerId}-${line.lineId}`;
      const current = byLine.get(key) || { ...line, productionPlan: 0, productionActual: 0 };
      current.productionPlan += Number(line.productionPlan || 0);
      current.productionActual += Number(line.productionActual || 0);
      byLine.set(key, current);
    });
    return [...byLine.values()].map(line => ({
      ...line,
      achievement: line.productionPlan > 0 ? line.productionActual / line.productionPlan * 100 : 0,
    }));
  }, [data.lines]);
  const s = data.summary || {};
  const filteredLines = useMemo(() => {
    const needle = searchTerm.trim().toLowerCase();
    return data.lines.filter(line => {
      const status = String(line.status || "").toUpperCase();
      const matchesStatus = statusFilter === "ALL" || status === statusFilter;
      const haystack = [line.lineName, line.sapLocation, line.customerName, line.process, line.modelName, line.status, line.issueDescription]
        .map(value => String(value ?? "").toLowerCase()).join(" ");
      return matchesStatus && (!needle || haystack.includes(needle));
    });
  }, [data.lines, searchTerm, statusFilter]);
  const pageCount = Math.max(1, Math.ceil(filteredLines.length / pageSize));
  const safePage = Math.min(page, pageCount);
  const visibleLines = filteredLines.slice((safePage - 1) * pageSize, safePage * pageSize);
  const statusCounts = useMemo(() => data.lines.reduce((counts, line) => {
    const key = String(line.status || "UNKNOWN").toUpperCase();
    counts[key] = (counts[key] || 0) + 1;
    return counts;
  }, {}), [data.lines]);

  return <div className="viewer-page">
    <Header user={session?.user} onSignOut={onSignOut} />
    <div className="viewer-module-nav-wrap"><nav className="viewer-module-nav" aria-label="Viewer pages"><a href="/viewer">Dashboard</a><a href="/viewer/exceptions">Operations Exceptions</a><a href="/viewer/comparison">Period Comparison</a><a href="/viewer/tracker">Report Tracker</a><a href="/viewer/reports">Report Center</a></nav></div>
    <main className="viewer-main">
      <div className="viewer-title-row">
        <div><span className="eyebrow">READ ONLY</span><h1>FATP & SMT Work Progress</h1><p>Date-wise production and KPI visibility for the selected plant.</p></div>
        <div className="viewer-actions"><button className="secondary-btn" onClick={load} disabled={loading||!plantId}><RefreshCw size={16}/> Refresh</button><button className="primary-btn" onClick={openExport} disabled={!plantId}><Download size={16}/> Excel</button></div>
      </div>

      <section className="filters">
        <label>Plant<select value={plantId} onChange={e => { setPlantId(e.target.value); setCustomerId(""); setCustomers([]); setLoading(true); }} disabled={plantsLoading||!plants.length}><option value="">{plantsLoading?"Loading plants…":"Select a plant"}</option>{plants.map(plant => <option key={plant.plantId} value={plant.plantId}>{plant.plantName}</option>)}</select></label>
        <label>Report Date<input type="date" value={date} onChange={e => { setDate(e.target.value); setLoading(true); }} /></label>
        <label>Customer<select value={customerId} onChange={e => { setCustomerId(e.target.value); setLoading(true); }}><option value="">All Customers</option>{customers.map(c => <option key={c.customerId} value={c.customerId}>{c.customerName}</option>)}</select></label>
        <label>Process<select value={process} onChange={e => { setProcess(e.target.value); setLoading(true); }}><option>ALL</option><option>FATP</option><option>SMT</option></select></label>
        <label>Trend Range<select value={trendDays} onChange={e => { setTrendDays(Number(e.target.value)); setLoading(true); }}>{Array.from({ length: 30 }, (_, i) => i + 1).map(days => <option key={days} value={days}>{days} {days === 1 ? "Day" : "Days"}</option>)}</select></label>
      </section>

      {error && <div className="viewer-error">{error}</div>}

      {exportOpen&&<div className="viewer-modal-backdrop" role="presentation" onMouseDown={event=>{if(event.target===event.currentTarget&&!exporting)setExportOpen(false);}}><section className="viewer-export-dialog" role="dialog" aria-modal="true" aria-labelledby="viewer-export-title"><div className="viewer-export-heading"><div><span className="eyebrow">EXCEL EXPORT</span><h2 id="viewer-export-title">Choose plant</h2></div><button type="button" className="viewer-export-close" aria-label="Close export dialog" onClick={()=>setExportOpen(false)} disabled={exporting}><X size={18}/></button></div><form onSubmit={downloadExcel}><label className="viewer-export-field">Plant<select required value={exportPlantId} onChange={event=>setExportPlantId(event.target.value)} disabled={exporting}><option value="">Select a plant</option>{plants.map(plant=><option key={plant.plantId} value={plant.plantId}>{plant.plantName}</option>)}</select></label><p>Export date: {date} · Process: {process}{exportPlantId===plantId&&customerId?" · Selected customer only":" · All customers for this plant"}</p><div className="viewer-export-actions"><button type="button" className="secondary-btn" onClick={()=>setExportOpen(false)} disabled={exporting}>Cancel</button><button type="submit" className="primary-btn" disabled={!exportPlantId||exporting}><Download size={16}/>{exporting?"Preparing…":"Download Excel"}</button></div></form></section></div>}

      <section className="cards">
        <Card label="Production Plan" value={fmt(s.productionPlan)} />
        <Card label="Production Actual" value={fmt(s.productionActual)} />
        <Card label="Achievement" value={`${Number(s.achievement || 0).toFixed(1)}%`} />
        <Card label="Lines" value={`${s.completedLines || 0} / ${s.totalLines || 0}`} sub="completed" />
      </section>

      <section className="viewer-grid">
        <div className="panel trend-panel"><div className="panel-head"><div><h2>Plan vs Actual Trend</h2><small className="panel-subtitle">{trendDays}-day production performance</small></div><span>{process}</span></div><TrendChart data={trend} maxValue={maxTrend} days={trendDays} /><div className="legend"><span><i className="dot plan-dot"/> Plan</span><span><i className="dot actual-dot"/> Actual</span></div></div>
        <div className="panel"><div className="panel-head"><h2>Achievement by Line</h2></div><div className="achievement-list">{achievementLines.map(line => <div className="achievement-row" key={`${line.customerId}-${line.lineId}`}><div><strong>{line.lineName}</strong><small>{line.customerName} · {line.process}</small></div><div className="progress-wrap"><div className="progress-track"><div className="progress-fill" style={{width:`${Math.min(100, Number(line.achievement || 0))}%`}}/></div><b>{Number(line.achievement || 0).toFixed(1)}%</b></div></div>)}</div></div>
      </section>

      <section className="panel table-panel">
        <div className="panel-head"><div><h2>Line Work Progress</h2><small className="panel-subtitle">{date} · {process === "ALL" ? "FATP first, then SMT" : `${process} only`} · Brand/model-wise daily records{loading ? " · Loading..." : ""}</small></div><span>{filteredLines.length} of {data.lines.length} records</span></div>
        <div className="viewer-table-tools">
          <label className="viewer-search"><Search size={16}/><input aria-label="Search line records" placeholder="Search line, customer, model, SAP location…" value={searchTerm} onChange={event => { setSearchTerm(event.target.value); setPage(1); }}/></label>
          <label className="viewer-status-filter">Status<select value={statusFilter} onChange={event => { setStatusFilter(event.target.value); setPage(1); }}><option value="ALL">All statuses ({data.lines.length})</option>{Object.keys(statusCounts).sort().map(status => <option key={status} value={status}>{status.replaceAll("_", " ")} ({statusCounts[status]})</option>)}</select></label>
          <label className="viewer-page-size">Rows<select value={pageSize} onChange={event => { setPageSize(Number(event.target.value)); setPage(1); }}><option value={10}>10</option><option value={25}>25</option><option value={50}>50</option></select></label>
          <button className="secondary-btn viewer-reset-btn" onClick={() => { setSearchTerm(""); setStatusFilter("ALL"); setPage(1); }}><RotateCcw size={14}/> Reset</button>
        </div>
        <div className="table-scroll detail-table-scroll">
          <table className="detail-table">
            <thead>
              <tr>
                <th>Line No.</th><th>SAP Location</th><th>Customer</th><th>Process</th><th>Model</th><th>Status</th>
                <th>Month Plan</th><th>Month Actual</th><th>Production Plan</th><th>Production Actual</th>
                <th>UPH Target</th><th>UPH Actual</th><th>UPPH Installed</th><th>UPPH Actual</th>
                <th>FPY Target</th><th>FPY Actual</th><th>FTY Target</th><th>FTY Actual</th><th>RTY Target</th><th>RTY Actual</th>
                <th>OS&amp;D Reporting Date Value</th><th>OS&amp;D Reporting Date %</th><th>OS&amp;D MTD Value</th><th>OS&amp;D MTD %</th>
                <th>Actual OT</th><th>Planned OT</th><th>Unplanned OT</th><th>Open WO Qty</th><th>&gt;7 Days WO</th>
                <th>Daily TRC In</th><th>Daily TRC Out</th><th>TRC Overall Failure %</th><th>TRC Lying &gt;3 Days</th><th>Issue Description</th>
              </tr>
            </thead>
            <tbody>{visibleLines.map(line => <tr key={line.dailyReportId || `${line.customerId}-${line.lineId}-empty`}>
              <td><strong>{line.lineName}</strong></td><td>{line.sapLocation || "—"}</td><td>{line.customerName}</td>
              <td><span className={`process ${String(line.process || "").toLowerCase()}`}>{line.process || "—"}</span></td><td>{line.modelName || "—"}</td>
              <td><span className={`status ${String(line.status || "unknown").toLowerCase().replaceAll("_", "-")}`}>{String(line.status || "UNKNOWN").replaceAll("_", " ")}</span></td>
              <td>{metric(line.monthPlan)}</td><td>{metric(line.monthActual)}</td><td>{metric(line.productionPlan)}</td><td>{metric(line.productionActual)}</td>
              <td>{metric(line.uphTarget)}</td><td>{metric(line.uphActual)}</td><td>{metric(line.upphInstalled)}</td><td>{metric(line.upphActual)}</td>
              <td>{metric(line.fpyTarget, true)}</td><td>{metric(line.fpyActual, true)}</td><td>{metric(line.ftyTarget, true)}</td><td>{metric(line.ftyActual, true)}</td><td>{metric(line.rtyTarget, true)}</td><td>{metric(line.rtyActual, true)}</td>
              <td>{metric(line.osdReportingDateValue)}</td><td>{metric(line.osdReportingDatePercent, true)}</td><td>{metric(line.osdMtdValue)}</td><td>{metric(line.osdMtdPercent, true)}</td>
              <td>{metric(line.actualOTManhours)}</td><td>{metric(line.plannedOTManhours)}</td><td>{metric(line.unplannedOTManhours)}</td>
              <td>{metric(line.openWOQty)}</td><td>{metric(line.over7DaysWOBalanceQty)}</td><td>{metric(line.dailyTRCInQty)}</td><td>{metric(line.dailyTRCOutQty)}</td>
              <td>{metric(line.trcOverallFailureInflowPercent, true)}</td><td>{metric(line.trcLyingOver3DaysCr)}</td>
              <td className="issue-cell" title={line.issueDescription || ""}>{line.issueDescription || "—"}</td>
            </tr>)}</tbody>
          </table>
          {!visibleLines.length && !loading && <div className="empty">{data.lines.length ? "No records match your search or status filter." : "No production records found for the selected date and filters."}</div>}
        </div>
        <div className="viewer-pagination"><span>Showing {filteredLines.length ? (safePage - 1) * pageSize + 1 : 0}–{Math.min(safePage * pageSize, filteredLines.length)} of {filteredLines.length}</span><div><button className="secondary-btn" onClick={() => setPage(current => Math.max(1, current - 1))} disabled={safePage <= 1}><ChevronLeft size={15}/> Previous</button><span className="viewer-page-indicator">Page {safePage} of {pageCount}</span><button className="secondary-btn" onClick={() => setPage(current => Math.min(pageCount, current + 1))} disabled={safePage >= pageCount}>Next <ChevronRight size={15}/></button></div></div>
      </section>
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
function metric(value, isPercent = false) {
  if (value === null || value === undefined || value === "") return "—";
  const formatted = Number(value).toLocaleString("en-IN", { maximumFractionDigits: 2 });
  return isPercent ? `${formatted}%` : formatted;
}