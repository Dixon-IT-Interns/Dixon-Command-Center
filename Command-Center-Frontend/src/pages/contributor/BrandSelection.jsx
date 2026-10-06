import { useEffect, useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { api } from "../../api";
import ContributorLayout from "../../components/contributor/ContributorLayout";
import ProcessTracker from "../../components/contributor/ProcessTracker";
import "./BrandSelection.css";

const brandMeta = {
  HP: { tone: "blue", tag: "Laptop & PC manufacturing", mark: "hp" },
  ASUS: { tone: "violet", tag: "Computing & electronics", mark: "asus" },
  Acer: { tone: "green", tag: "Computing & electronics", mark: "acer" },
  Gigabyte: { tone: "orange", tag: "Motherboards & computing", mark: "gigabyte" },
};

function BrandMark({ name }) {
  const meta = brandMeta[name] || { tone: "violet", mark: "brand" };
  return <span className={`brand-mark brand-mark-${meta.tone}`}>{meta.mark}</span>;
}

export default function BrandSelection({ session, onSignOut }) {
  const navigate = useNavigate();
  const [customers, setCustomers] = useState([]);
  const [error, setError] = useState("");

  useEffect(() => {
    api.get("/catalog/customers")
      .then(setCustomers)
      .catch((requestError) => setError(requestError.message));
  }, []);

  const sortedCustomers = useMemo(() => {
    const order = ["HP", "ASUS", "Acer", "Gigabyte"];
    return [...customers].sort((a, b) => {
      const ai = order.indexOf(a.customerName);
      const bi = order.indexOf(b.customerName);
      return (ai === -1 ? 99 : ai) - (bi === -1 ? 99 : bi);
    });
  }, [customers]);

  return (
    <ContributorLayout user={session?.user} onSignOut={onSignOut}>
      <div className="products-container">
        <main className="products-content">
          <ProcessTracker activeStep="brand" />

          <section className="products-hero">
            <div>
              <span className="page-kicker">TODAY · {new Date().toLocaleDateString("en-IN", { day: "2-digit", month: "short", year: "numeric" })}</span>
              <h1>What are you working on first?</h1>
              <p>Pick a brand to start today&apos;s factory report. You can come back and complete another brand anytime.</p>
            </div>
            <div className="today-chip"><span /> Reporting workspace</div>
          </section>

          {error && <p className="notice error" role="alert">{error}</p>}

          <section className="brand-section">
            <div className="section-heading">
              <div><span>CHOOSE A BRAND</span><h2>Start your report</h2></div>
              <small>{sortedCustomers.length} brands available</small>
            </div>
            <div className="brand-grid">
              {sortedCustomers.map((customer) => {
                const meta = brandMeta[customer.customerName] || { tone: "violet", tag: "Manufacturing", mark: customer.customerName };
                return (
                  <button className={`brand-card brand-card-${meta.tone}`} key={customer.customerId}
                    onClick={() => navigate(`/contributor/brands/${customer.customerId}/categories`)}>
                    <div className="brand-card-top"><BrandMark name={customer.customerName} /><span className="brand-open">↗</span></div>
                    <div className="brand-card-copy"><h3>{customer.customerName}</h3><p>{meta.tag}</p></div>
                    <div className="brand-card-bottom"><span>{customer.categories.length} reporting categories</span><strong>{customer.categories.reduce((total, category) => total + category.productionLines.length, 0)} lines</strong></div>
                    <div className="brand-card-glow" />
                  </button>
                );
              })}
              {!sortedCustomers.length && !error && <div className="loading-card">Loading today&apos;s brands…</div>}
            </div>
          </section>

          <section className="workspace-tip"><div className="tip-icon">✦</div><div><strong>You choose the order. We remember the work.</strong><p>Finish one line, save it, then pick the next pending line. Your progress stays with you.</p></div></section>
        </main>
      </div>
    </ContributorLayout>
  );
}
