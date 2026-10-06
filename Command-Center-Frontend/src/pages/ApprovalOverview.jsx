import { useEffect, useState } from "react";
import {
  ArrowRight,
  CheckCircle2,
  Clock3,
  FileCheck2,
  RefreshCw,
  XCircle,
} from "lucide-react";
import { Link } from "react-router-dom";

import { api } from "../api";
import Header from "../components/Header";
import AppFooter from "../components/AppFooter";

import "./ApprovalOverview.css";

export default function ApprovalOverview({ session, onSignOut }) {
  const [summary, setSummary] = useState(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);

  const loadSummary = async () => {
    setError("");
    setLoading(true);

    try {
      const data = await api.get("/approvals/summary");
      setSummary(data);
    } catch (requestError) {
      setError(
        requestError?.message ||
          "Unable to load approval summary."
      );
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadSummary();
  }, []);

  return (
    <div className="approval-overview-page">
      <Header
        user={session.user}
        onSignOut={onSignOut}
      />

      <main className="approval-overview">
        <div className="ao-head">
          <div>
            <span>APPROVER WORKSPACE</span>

            <h1>Approval overview</h1>

            <p>
              Monitor the review queue, approved submissions and
              rejected reports across your assigned brands.
            </p>
          </div>

          <button
            type="button"
            onClick={loadSummary}
            disabled={loading}
          >
            <RefreshCw
              size={15}
              className={loading ? "ao-spin" : ""}
            />

            {loading ? "Refreshing..." : "Refresh"}
          </button>
        </div>

        {error && (
          <div className="ao-error">
            {error}
          </div>
        )}

        <div className="ao-stat-grid">
          <Stat
            icon={Clock3}
            label="Pending review"
            value={summary?.pending ?? "—"}
            tone="pending"
          />

          <Stat
            icon={CheckCircle2}
            label="Approved"
            value={summary?.approved ?? "—"}
            tone="approved"
          />

          <Stat
            icon={XCircle}
            label="Rejected"
            value={summary?.rejected ?? "—"}
            tone="rejected"
          />

          <Stat
            icon={FileCheck2}
            label="Reviewed"
            value={summary?.reviewed ?? "—"}
            tone="reviewed"
          />
        </div>

        <section className="ao-actions">
          <Action
            title="Open approval queue"
            text="Inspect submitted daily KPI reports and approve or return them with a comment."
            to="/approvals"
            label="Review queue"
          />

          <Action
            title="Review history"
            text="Browse the reports already handled by your approval account."
            to="/approvals/history"
            label="View history"
          />

          <Action
            title="Monthly plan reference"
            text="Approved reports can be checked against the KPI plan maintained by the administrator."
            to="/approvals"
            label="Open a report"
          />
        </section>
      </main>

      <AppFooter />
    </div>
  );
}

function Stat({
  icon: Icon,
  label,
  value,
  tone,
}) {
  return (
    <div className={`ao-stat ${tone}`}>
      <span>
        <Icon size={17} />
      </span>

      <small>{label}</small>

      <strong>{value}</strong>
    </div>
  );
}

function Action({
  title,
  text,
  to,
  label,
}) {
  return (
    <article>
      <h2>{title}</h2>

      <p>{text}</p>

      <Link to={to}>
        {label}

        <ArrowRight size={14} />
      </Link>
    </article>
  );
}