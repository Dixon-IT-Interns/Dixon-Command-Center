import dixonLogo from "../assets/dixon-logo.jpeg";
import "./AppFooter.css";

export default function AppFooter() {
  return (
    <footer className="app-footer">
      <div className="app-footer-brand">
        <img src={dixonLogo} alt="Dixon" />
        <span>Dixon Digital Command Center</span>
      </div>
      <div className="app-footer-meta">
        <span>Factory KPI Reporting Platform</span>
        <span>Internal workspace</span>
      </div>
    </footer>
  );
}
