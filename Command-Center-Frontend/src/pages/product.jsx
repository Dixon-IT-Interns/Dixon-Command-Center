import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { api } from "../api";
import "./Product.css";
import Header from "../components/Header";

function Products({ session, onSignOut }) {
  const navigate = useNavigate();
  const [customers, setCustomers] = useState([]);
  const [error, setError] = useState("");

  useEffect(() => {
    api.get("/catalog/customers")
      .then(setCustomers)
      .catch((requestError) => setError(requestError.message));
  }, []);

  return (
    <div className="products-container">
      <Header user={session.user} onSignOut={onSignOut} />
      <main className="products-content">
        <div className="page-heading">
          <p className="eyebrow">Manufacturing directory</p>
          <h1>Customers</h1>
          <p>Select a customer to view its categories, models, and production lines.</p>
        </div>
        {error && <p className="notice error" role="alert">{error}</p>}
        <div className="brand-grid">
          {customers.map((customer) => (
            <button
              className="brand-card"
              key={customer.customerId}
              onClick={() => navigate(`/products/${customer.customerId}`)}
            >
              <span className="brand-name">{customer.customerName}</span>
              <span className="customer-summary">{customer.categories.length} categories · {customer.models.length} models</span>
              <span className="brand-arrow" aria-hidden="true">→</span>
            </button>
          ))}
          {!customers.length && !error && <p className="empty-state">Loading customers…</p>}
        </div>
      </main>
    </div>
  );
}

export default Products;