import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { api } from "../api";
import Header from "../components/Header";
import "./BrandPage.css";

function BrandPage({ session, onSignOut }) {
  const navigate = useNavigate();
  const { customerId } = useParams();
  const [customer, setCustomer] = useState(null);
  const [error, setError] = useState("");

  useEffect(() => {
    api.get("/catalog/customers")
      .then((customers) => setCustomer(customers.find((item) => item.customerId === Number(customerId)) || null))
      .catch((requestError) => setError(requestError.message));
  }, [customerId]);

  return (
    <div className="brand-container">
      <Header user={session.user} onSignOut={onSignOut} />
      <main className="brand-content">
        <div className="brand-heading">
          <button className="back-button" onClick={() => navigate("/products")}>← Customers</button>
          <p className="eyebrow">Customer profile</p>
          <h1>{customer?.customerName || "Customer"}</h1>
          <p>Choose a category to inspect its production lines.</p>
        </div>
        {error && <p className="notice error" role="alert">{error}</p>}
        <div className="process-grid">
          {customer?.categories.map((category) => (
            <button className="process-card" key={category.categoryId} onClick={() => navigate(`/products/${customer.customerId}/${category.categoryId}`)}>
              <span className="process-name">{category.categoryName}</span>
              <span className="process-description">{category.productionLines.length} production lines</span>
              <span className="process-arrow" aria-hidden="true">→</span>
            </button>
          ))}
          {!customer && !error && <p className="empty-state">Loading customer…</p>}
          {customer && !customer.categories.length && <p className="empty-state">No categories have been configured for this customer.</p>}
        </div>
        {customer?.models.length > 0 && (
          <section className="model-section">
            <div className="model-heading"><h2>Models</h2><span>{customer.models.length} configured</span></div>
            <div className="model-list">{customer.models.map((model) => <div className="model-row" key={model.modelId}>{model.modelName}</div>)}</div>
          </section>
        )}
      </main>
    </div>
  );
}

export default BrandPage;