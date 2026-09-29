import { useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { api } from "../api";
import Header from "../components/Header";
import "./ProcessPage.css";

function ProcessPage({ session, onSignOut }) {
  const navigate = useNavigate();
  const { customerId, categoryId } = useParams();
  const [customer, setCustomer] = useState(null);
  const [error, setError] = useState("");

  useEffect(() => {
    api.get("/catalog/customers")
      .then((customers) => setCustomer(customers.find((item) => item.customerId === Number(customerId)) || null))
      .catch((requestError) => setError(requestError.message));
  }, [customerId]);

  const category = customer?.categories.find((item) => item.categoryId === Number(categoryId));

  return (
    <div className="process-container">
      <Header user={session.user} onSignOut={onSignOut} />
      <main className="process-content">
        <div className="process-heading">
          <button className="back-button" onClick={() => navigate(`/products/${customerId}`)}>← {customer?.customerName || "Customer"}</button>
          <span className="brand-label">{customer?.customerName || "Customer"}</span>
          <h1>{category?.categoryName || "Category"}</h1>
          <p>Production lines and SAP locations</p>
        </div>
        {error && <p className="notice error" role="alert">{error}</p>}
        <section className="line-table">
          <div className="line-table-heading"><span>Line</span><span>SAP location</span></div>
          {category?.productionLines.map((line) => (
            <div className="line-row" key={line.lineId}><strong>{line.lineName}</strong><span>{line.sapLocation || "Not set"}</span></div>
          ))}
          {!category && !error && <p className="empty-state">Loading production lines…</p>}
          {category && !category.productionLines.length && <p className="empty-state">No lines have been configured for this category.</p>}
        </section>
      </main>
    </div>
  );
}

export default ProcessPage;