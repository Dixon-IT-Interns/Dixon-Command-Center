import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { CheckCircle2, Clock3, ArrowRight } from "lucide-react";
import { api } from "../../api";
import ContributorLayout from "../../components/contributor/ContributorLayout";
import ProcessTracker from "../../components/contributor/ProcessTracker";
import "./CategorySelection.css";

const categoryMeta = {
  SMT: { tone: "violet", eyebrow: "ELECTRONICS · SMT", title: "Surface Mount Technology", icon: "⌁" },
  FATP: { tone: "blue", eyebrow: "ASSEMBLY · FATP", title: "Final Assembly, Test & Pack", icon: "◈" },
};

export default function CategorySelection({ session, onSignOut }) {
  const navigate = useNavigate();
  const { customerId } = useParams();
  const [customer, setCustomer] = useState(null);
  const [progress, setProgress] = useState({});
  const [error, setError] = useState("");

  useEffect(() => {
    let active = true;
    async function load() {
      try {
        const customers = await api.get("/catalog/customers");
        const found = customers.find((item) => Number(item.customerId) === Number(customerId));
        if (!found) throw new Error("Brand configuration was not found.");
        if (!active) return;
        setCustomer(found);

        const results = await Promise.all(found.categories.map(async (category) => {
          try {
            const statuses = await api.get(`/daily-reports/line-status?customerId=${customerId}&categoryId=${category.categoryId}&reportDate=${today()}`);
            const lines = Array.isArray(statuses) ? statuses : [];
            const done = lines.filter((line) => line.status === "SUBMITTED" || line.status === "APPROVED" || line.status === "INACTIVE").length;
            const completed = lines.length > 0 && done === lines.length;
            return [category.categoryId, { done, total: lines.length, completed }];
          } catch {
            return [category.categoryId, { done: 0, total: category.productionLines.length, completed: false }];
          }
        }));
        if (active) setProgress(Object.fromEntries(results));
      } catch (requestError) {
        if (active) setError(requestError.message || "Unable to load categories.");
      }
    }
    load();
    return () => { active = false; };
  }, [customerId]);

  return (
    <ContributorLayout user={session?.user} onSignOut={onSignOut}>
      <div className="brand-container">
        <main className="brand-content">
          <ProcessTracker activeStep="category" customerId={customerId} />

          {customer && (
            <div className="selected-brand">
              <div className="selected-brand-icon">{customer.customerName.slice(0, 1)}</div>
              <div><small>REPORTING FOR</small><strong>{customer.customerName}</strong></div>
              <button onClick={() => navigate("/contributor/brands")}>Change brand</button>
            </div>
          )}

          <section className="category-hero">
            <span className="page-kicker">STEP 02 · CHOOSE YOUR WORK</span>
            <h1>What do you want to report?</h1>
            <p>Choose a production category. You&apos;ll go directly to today&apos;s report for all its lines.</p>
          </section>

          {error && <p className="notice error" role="alert">{error}</p>}

          <section className="category-grid">
            {customer?.categories.map((category) => {
              const meta = categoryMeta[category.categoryName] || { tone: "violet", eyebrow: "PRODUCTION", title: category.categoryName, icon: "•" };
              const status = progress[category.categoryId] || { done: 0, total: category.productionLines.length, completed: false };
              return (
                <button
                  className={`category-card category-card-${meta.tone} ${status.completed ? "category-card-complete" : ""}`}
                  key={category.categoryId}
                  onClick={() => navigate(`/contributor/brands/${customer.customerId}/categories/${category.categoryId}/report`)}
                >
                  <div className="category-top">
                    <span className="category-icon">{meta.icon}</span>
                    {status.completed ? <span className="category-complete"><CheckCircle2 size={14} /> Today&apos;s done</span> : <span className="category-arrow"><ArrowRight size={15} /></span>}
                  </div>
                  <div className="category-copy">
                    <span>{meta.eyebrow}</span>
                    <h2>{category.categoryName}</h2>
                    <p>{meta.title}</p>
                  </div>
                  <div className="category-bottom">
                    <span>{status.done}/{status.total} lines completed today</span>
                    <strong>{status.completed ? "View report →" : status.done ? "Continue →" : "Start reporting →"}</strong>
                  </div>
                  {!status.completed && status.total > 0 && (
                    <div className="category-progress"><span style={{ width: `${Math.min(100, (status.done / status.total) * 100)}%` }} /></div>
                  )}
                </button>
              );
            })}
            {!customer && !error && <div className="loading-card">Preparing your reporting workspace…</div>}
            {customer && !customer.categories.length && <div className="loading-card">No production categories are configured for this brand.</div>}
          </section>

          <div className="brand-tip"><Clock3 size={15} /><p><strong>Today&apos;s progress stays saved.</strong> Complete SMT and come back here — it will show as completed. You can then report FATP.</p></div>
        </main>
      </div>
    </ContributorLayout>
  );
}

function today() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}
