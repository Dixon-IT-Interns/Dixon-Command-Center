import { Link, useNavigate } from "react-router-dom";
import dixonLogo from "../../assets/dixon-logo.jpeg";
import { LogOut } from "lucide-react";
import "./ContributorShell.css";

export default function Navbar({ user, onSignOut }) {
  const navigate = useNavigate();
  return (
    <header className="cc-navbar">
      <Link to="/contributor/brands" className="cc-navbar-brand">
        <img className="cc-brand-logo" src={dixonLogo} alt="Dixon" />
        <span>Dixon <strong>Command Center</strong></span>
      </Link>
      <div className="cc-navbar-actions">
        <div className="cc-user-pill">
          <span className="cc-user-name">{user?.fullName || "Contributor"}</span>
          <span className="cc-role-pill">{user?.roleName || "Contributor"}</span>
        </div>
        <button className="cc-signout" onClick={() => { onSignOut?.(); navigate("/"); }}>
          <LogOut size={15} />
          <span>Sign out</span>
        </button>
      </div>
    </header>
  );
}
