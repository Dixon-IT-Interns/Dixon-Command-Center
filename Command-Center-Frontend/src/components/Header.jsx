import { Link, useNavigate } from "react-router-dom";
import { Bell, LogOut } from "lucide-react";
import dixonLogo from "../assets/dixon-logo.jpeg";
import "./Header.css";

export default function Header({ user, onSignOut }) {
  const navigate = useNavigate();
  return (
    <header className="header">
      <Link className="header-brand" to="/dashboard">
        <img src={dixonLogo} alt="Dixon" />
        <span className="header-brand-divider" />
        <span>Dixon <strong>Command Center</strong></span>
      </Link>
      <div className="header-actions">
        {user?.roleName === "Approver" && <Link className="header-nav-link" to="/approvals/overview">Overview</Link>}
        {user?.roleName === "Approver" && <Link className="header-nav-link" to="/approvals">Approval Queue</Link>}
        {user?.roleName === "Approver" && <Link className="header-nav-link" to="/approvals/history">History</Link>}
        {user?.roleName === "Admin" && <Link className="header-nav-link" to="/admin">Administration</Link>}
        {user && <div className="header-user"><span className="header-avatar">{user.fullName?.slice(0,1).toUpperCase()}</span><span>{user.fullName}<small>{user.roleName}</small></span></div>}
        <button className="header-icon" title="Notifications"><Bell size={16}/></button>
        <button className="header-signout" onClick={() => { onSignOut?.(); navigate("/"); }}><LogOut size={15}/><span>Sign out</span></button>
      </div>
    </header>
  );
}
