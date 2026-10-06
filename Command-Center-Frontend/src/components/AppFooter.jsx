import dixonLogo from "../assets/dixon-logo.jpeg";
import "./AppFooter.css";
import {
  ArrowUpRight,
  Factory,
  Mail,
  MapPin,
  Phone,
  ShieldCheck,
} from "lucide-react";
import { Link } from "react-router-dom";

const workspaceLinks = {
  Contributor: [
    { label: "Contributor dashboard", to: "/contributor/dashboard" },
    { label: "Start a daily report", to: "/contributor/brands" },
    { label: "Submitted reports", to: "/contributor/reports" },
    { label: "Activity", to: "/contributor/activity" },
  ],
  Approver: [
    { label: "Approval overview", to: "/approvals/overview" },
    { label: "Review queue", to: "/approvals" },
    { label: "Review history", to: "/approvals/history" },
  ],
  Admin: [
    { label: "Operations dashboard", to: "/dashboard" },
    { label: "Administration", to: "/admin" },
  ],
};

function getCurrentUser() {
  try {
    return JSON.parse(localStorage.getItem("dcc-session"))?.user ?? null;
  } catch {
    return null;
  }
}

export default function AppFooter({ user = getCurrentUser(), variant }) {
  if (variant === "admin") {
    return <AdminFooter />;
  }

  const links = workspaceLinks[user?.roleName] ?? [];

  return (
    <footer className="app-footer">
      <div className="app-footer-inner">
        <section
          className="app-footer-brand"
          aria-label="Dixon Command Center"
        >
          <span className="admin-footer__logo">
            <img src={dixonLogo} alt="Dixon" />
            <span aria-hidden="true" />
          </span>

          <span className="app-footer-kicker">
            <span />
            FACTORY OPERATIONS
          </span>

          <h2>
            Dixon <strong>Command Center</strong>
          </h2>

          <p>
            Daily KPI reporting, production visibility and accountable
            approvals for Dixon factory teams.
          </p>
        </section>

        <nav className="app-footer-nav" aria-label="Workspace navigation">
          <div className="app-footer-heading">
            <span>Your workspace</span>
            <strong>{user?.roleName || "Factory operations"}</strong>
          </div>

          {links.map(({ label, to }) => (
            <Link key={to} to={to}>
              <span>{label}</span>
              <ArrowUpRight size={14} aria-hidden="true" />
            </Link>
          ))}
        </nav>

        <section
          className="app-footer-context"
          aria-label="Current workspace"
        >
          <div className="app-footer-context-label">
            <Factory size={15} aria-hidden="true" />
            CURRENT PLANT
          </div>

          <strong>{user?.plantName || "Dixon factory workspace"}</strong>

          <div className="app-footer-session">
            <ShieldCheck size={14} aria-hidden="true" />
            Internal workspace
          </div>
        </section>
      </div>

      <div className="app-footer-bottom">
        <span>Factory KPI reporting · Approvals · Administration</span>
        <span>© {new Date().getFullYear()} Dixon Command Center</span>
      </div>
    </footer>
  );
}

function AdminFooter() {
  const quickLinks = [
    { label: "Home", to: "/dashboard" },
    { label: "Brands", to: "/admin" },
    { label: "About Us", href: "https://www.dixoninfo.com" },
    { label: "Help & Support", href: "mailto:support@dixoninfo.com" },
  ];

  const resources = [
    "User Guide",
    "FAQs",
    "Privacy Policy",
    "Terms & Conditions",
  ];

  return (
    <footer className="app-footer admin-footer">
      <div className="admin-footer__main">
        <section
          className="admin-footer__brand"
          aria-label="Dixon Command Center"
        >
          <div className="admin-footer__brand-logo">
            <img src={dixonLogo} alt="Dixon" />
          </div>

          <span className="admin-footer__tagline">
            The brand behind brands
          </span>

          <h2>Dixon Command Center</h2>

          <p>Smarter processes. Better decisions. Together.</p>
        </section>

        <nav className="admin-footer__column" aria-label="Quick Links">
          <h3>Quick Links</h3>

          <ul>
            {quickLinks.map(({ label, to, href }) => (
              <li key={label}>
                {to ? (
                  <Link to={to}>{label}</Link>
                ) : (
                  <a
                    href={href}
                    target={href?.startsWith("http") ? "_blank" : undefined}
                    rel={href?.startsWith("http") ? "noreferrer" : undefined}
                  >
                    {label}
                  </a>
                )}
              </li>
            ))}
          </ul>
        </nav>

        <section
          className="admin-footer__column"
          aria-labelledby="admin-footer-resources"
        >
          <h3 id="admin-footer-resources">Resources</h3>

          <ul>
            {resources.map((label) => (
              <li key={label}>
                <a
                  href={`mailto:support@dixoninfo.com?subject=${encodeURIComponent(
                    label
                  )}`}
                >
                  {label}
                </a>
              </li>
            ))}
          </ul>
        </section>

        <section
          className="admin-footer__column admin-footer__contact"
          aria-labelledby="admin-footer-contact"
        >
          <h3 id="admin-footer-contact">Contact Us</h3>

          <ul>
            <li>
              <Phone size={15} aria-hidden="true" />
              <a href="tel:+9118001234567">+91 1800 123 4567</a>
            </li>

            <li>
              <Mail size={15} aria-hidden="true" />
              <a href="mailto:support@dixoninfo.com">
                support@dixoninfo.com
              </a>
            </li>

            <li>
              <MapPin size={15} aria-hidden="true" />
              <span>Noida, Uttar Pradesh, India</span>
            </li>
          </ul>
        </section>
      </div>

      <div className="admin-footer__bottom">
        <span>
          © {new Date().getFullYear()} Dixon Technologies (India) Limited. All
          rights reserved.
        </span>

        <span className="admin-footer__motto">
          <span className="admin-footer__motto-line" />
          Built for a smarter tomorrow
        </span>
      </div>
    </footer>
  );
}
