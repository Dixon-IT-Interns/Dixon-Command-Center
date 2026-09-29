import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../api";
import Header from "../components/Header";
import "./DashboardPage.css";

function DashboardPage({ session, onSignOut }) {
  const [customers, setCustomers] = useState([]);
  const [error, setError] = useState("");

  useEffect(() => {
    api.get("/catalog/customers")
      .then(setCustomers)
      .catch((requestError) => setError(requestError.message));
  }, []);

  const modelCount = customers.reduce((count, customer) => count + customer.models.length, 0);
  const categoryCount = customers.reduce((count, customer) => count + customer.categories.length, 0);
  const lineCount = customers.reduce((count, customer) => count + customer.categories.reduce((subtotal, category) => subtotal + category.productionLines.length, 0), 0);
  const roleMetrics = {
    Admin: [["Customers", customers.length], ["Models", modelCount], ["Production lines", lineCount]],
    Approver: [["Categories", categoryCount], ["Customer programs", customers.length], ["Production lines", lineCount]],
    Contributor: [["Models", modelCount], ["Categories", categoryCount], ["Production lines", lineCount]],
    Viewer: [["Customers", customers.length], ["Categories", categoryCount], ["Models", modelCount]],
  };
  const metrics = roleMetrics[session.user.roleName] || roleMetrics.Viewer;

  return (
    <div className="workspace-page">
      <Header user={session.user} onSignOut={onSignOut} />
      <main className="workspace-content">
        <div className="workspace-title">
          <div>
            <p className="eyebrow">{session.user.plantName || "Dixon Command Center"}</p>
            <h1>{session.user.roleName} overview</h1>
          </div>
          <span className="role-label">{session.user.roleName}</span>
        </div>
        <div className="metric-row">
          {metrics.map(([label, value]) => <div className="metric" key={label}><span>{label}</span><strong>{value}</strong></div>)}
        </div>
        {error && <p className="notice error" role="alert">{error}</p>}
        <section className="workspace-section">
          <div className="section-heading">
            <div><p className="eyebrow">Operations</p><h2>Production structure</h2></div>
            <Link className="text-link" to="/products">View all customers <span aria-hidden="true">→</span></Link>
          </div>
          <div className="customer-list">
            {customers.map((customer) => (
              <Link className="customer-row" to={`/products/${customer.customerId}`} key={customer.customerId}>
                <span className="customer-mark">{customer.customerName.slice(0, 2).toUpperCase()}</span>
                <span className="customer-info"><strong>{customer.customerName}</strong><small>{customer.categories.length} categories · {customer.models.length} models</small></span>
                <span className="row-arrow" aria-hidden="true">↗</span>
              </Link>
            ))}
            {!customers.length && !error && <p className="empty-state">Loading production structure…</p>}
          </div>
        </section>
        {session.user.roleName === "Admin" && (
          <Link className="admin-entry" to="/admin"><span><strong>Administration</strong><small>Manage workspace access and roles</small></span><span aria-hidden="true">→</span></Link>
        )}
      </main>
    </div>
  );
}

export default DashboardPage;