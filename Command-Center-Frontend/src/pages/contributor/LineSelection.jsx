import { useEffect, useMemo, useState } from "react";
import { ArrowLeft, ArrowRight, Check, Factory, Loader2, MapPin, Search, Sparkles } from "lucide-react";
import { useLocation, useNavigate, useParams } from "react-router-dom";
import { api } from "../../api";
import ContributorLayout from "../../components/contributor/ContributorLayout";
import "./LineSelection.css";

function today() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

export default function LineSelection({ session, onSignOut }) {
  const navigate = useNavigate();
  const location = useLocation();
  const { customerId, categoryId } = useParams();
  const [customer, setCustomer] = useState(location.state?.customer || null);
  const [category, setCategory] = useState(location.state?.category || null);
  const [lines, setLines] = useState([]);
  const [search, setSearch] = useState("");
  const [filter, setFilter] = useState("all");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const notice = location.state?.message || "";
  const reportDate = location.state?.reportDate || today();

  useEffect(() => {
    let active = true;

    Promise.all([
      api.get("/catalog/customers"),
      api.get(`/daily-reports/line-status?customerId=${customerId}&categoryId=${categoryId}&reportDate=${reportDate}`),
    ])
      .then(([customers, statusLines]) => {
        if (!active) return;
        const foundCustomer = customers.find((item) => Number(item.customerId) === Number(customerId));
        const foundCategory = foundCustomer?.categories?.find((item) => Number(item.categoryId) === Number(categoryId));
        setCustomer(foundCustomer || null);
        setCategory(foundCategory || null);
        setLines(Array.isArray(statusLines) ? statusLines : []);
      })
      .catch((requestError) => active && setError(requestError.message))
      .finally(() => active && setLoading(false));

    window.history.replaceState({}, document.title);
    return () => { active = false; };
  }, [customerId, categoryId, reportDate]);

  const counts = useMemo(() => ({
    submitted: lines.filter((line) => line.status === "SUBMITTED").length,
    draft: lines.filter((line) => line.status === "DRAFT").length,
    pending: lines.filter((line) => line.status === "NOT_STARTED").length,
  }), [lines]);

  const filteredLines = useMemo(() => lines.filter((line) => {
    const matchesSearch = `${line.lineName} ${line.sapLocation || ""}`.toLowerCase().includes(search.toLowerCase());
    if (!matchesSearch) return false;
    if (filter === "submitted") return line.status === "SUBMITTED";
    if (filter === "draft") return line.status === "DRAFT";
    if (filter === "pending") return line.status === "NOT_STARTED";
    return true;
  }), [lines, search, filter]);

  const openLine = (line) => {
    if (line.status === "SUBMITTED") return;
    navigate(`/contributor/brands/${customerId}/categories/${categoryId}/lines/${line.lineId}/report`, {
      state: { customer, category, line },
    });
  };

  return (
    <ContributorLayout user={session?.user} onSignOut={onSignOut}>
      <div className="process-page">
        <main className="process-content">
          <div className="journey">
            <JourneyStep number="1" label="Brand" done /><JourneyLine active />
            <JourneyStep number="2" label="Category" done /><JourneyLine active />
            <JourneyStep number="3" label="Line" active /><JourneyLine />
            <JourneyStep number="4" label="Report" />
          </div>

          <section className="page-header">
            <div className="header-left">
              <button type="button" className="back-link" onClick={() => navigate(`/contributor/brands/${customerId}/categories`)}><ArrowLeft size={16} /> Back to categories</button>
              <div className="breadcrumb"><span>{customer?.customerName || "Brand"}</span><span>›</span><span className="breadcrumb-active">{category?.categoryName || "Category"}</span></div>
              <div className="title-row"><div className="title-icon"><Factory size={22} /></div><div><h1>Select a production line</h1><p>Choose the next line you want to report for today.</p></div></div>
            </div>
            <div className="context-card"><div className="context-avatar">{customer?.customerName?.slice(0, 1) || "D"}</div><div className="context-info"><span className="context-label">REPORTING FOR</span><strong>{customer?.customerName || "Loading..."}</strong><span>{category?.categoryName || "Loading category..."}</span></div></div>
          </section>

          {notice && <div className="success-banner"><Check size={17} /><div><strong>{notice}</strong><span>The submitted line is now marked green below.</span></div></div>}
          {error && <div className="error-banner"><strong>Unable to load line status</strong><p>{error}</p></div>}

          <section className="line-selection-card">
            <div className="line-selection-head">
              <div><span className="section-eyebrow">TODAY&apos;S PROGRESS</span><h2>Production lines</h2><p>{counts.submitted} submitted · {counts.draft} draft · {counts.pending} pending</p></div>
              <div className="progress-chip"><strong>{counts.submitted}/{lines.length}</strong><span>submitted</span></div>
            </div>

            <div className="line-toolbar">
              <div className="search-box"><Search size={16} /><input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search line or SAP location..." /></div>
              <div className="line-filters">
                {[['all','All'],['pending','Pending'],['draft','Draft'],['submitted','Submitted']].map(([key,label]) => <button key={key} className={filter === key ? "active" : ""} onClick={() => setFilter(key)}>{label}</button>)}
              </div>
            </div>

            {loading ? <div className="line-loading"><Loader2 className="spin" size={20} /> Loading today&apos;s line status…</div> : filteredLines.length === 0 ? <div className="line-empty"><Factory size={24} /><h3>No lines found</h3><p>Try another filter or search.</p></div> : <div className="line-grid">
              {filteredLines.map((line) => {
                const submitted = line.status === "SUBMITTED";
                const draft = line.status === "DRAFT";
                return <button key={line.lineId} type="button" className={`line-card ${submitted ? "line-card-completed" : draft ? "line-card-draft" : ""}`} onClick={() => openLine(line)} disabled={submitted}>
                  <div className="line-card-top"><div className={`line-icon ${submitted ? "line-icon-completed" : ""}`}><Factory size={21} /></div><div className={`status-badge ${submitted ? "submitted" : draft ? "draft" : "pending"}`}>{submitted ? <><Check size={12}/> Submitted</> : draft ? "Draft saved" : "Not started"}</div></div>
                  <div className="line-information"><span className="line-label">PRODUCTION LINE</span><h3>{line.lineName}</h3><div className="sap-location"><MapPin size={14}/><span>{line.sapLocation || "SAP location not set"}</span></div></div>
                  <div className="line-card-footer"><span>{submitted ? "Completed for today" : draft ? "Continue report" : "Start report"}</span>{submitted ? <Check size={15}/> : <ArrowRight size={15}/>}</div>
                </button>;
              })}
            </div>}
          </section>

          <div className="page-hint"><Sparkles size={13}/><span>Complete one line, return here, then continue with the next pending line.</span></div>
        </main>
      </div>
    </ContributorLayout>
  );
}

function JourneyStep({ number, label, done = false, active = false }) {
  return <div className="journey-step"><div className={`journey-number ${done ? "journey-done" : active ? "journey-active" : ""}`}>{done ? <Check size={13}/> : number}</div><span className={done || active ? "journey-label-active" : ""}>{label}</span></div>;
}
function JourneyLine({ active = false }) { return <div className={`journey-connector ${active ? "journey-connector-active" : ""}`} />; }
