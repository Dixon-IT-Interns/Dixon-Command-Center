import { ClipboardList, Factory, LayoutDashboard, FileClock } from "lucide-react";
import { NavLink } from "react-router-dom";
import "./ContributorShell.css";
import dixonLogo from "../../assets/dixon-logo.jpeg";

export default function Sidebar() {
  return (
    <aside className="cc-sidebar" aria-label="Contributor navigation">
      <div className="cc-sidebar-logo"><img src={dixonLogo} alt="Dixon" /></div>
      <nav className="cc-sidebar-nav">
        <NavLink to="/contributor/brands" className="cc-sidebar-item" title="Reporting">
          <Factory size={19} />
          <span>Reporting</span>
        </NavLink>
        <NavLink to="/contributor/dashboard" className="cc-sidebar-item" title="Dashboard">
          <LayoutDashboard size={19} />
          <span>Dashboard</span>
        </NavLink>
        <NavLink to="/contributor/reports" className="cc-sidebar-item" title="Reports">
          <ClipboardList size={19} />
          <span>Reports</span>
        </NavLink>
        <NavLink to="/contributor/activity" className="cc-sidebar-item" title="Activity">
          <FileClock size={19} />
          <span>Activity</span>
        </NavLink>
      </nav>
      <div className="cc-sidebar-bottom"><span className="cc-sidebar-dot" /></div>
    </aside>
  );
}
