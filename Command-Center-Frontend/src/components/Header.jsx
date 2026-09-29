import { Link, useNavigate } from "react-router-dom";
import "./Header.css";
function Header({ user, onSignOut }) {
  const navigate = useNavigate();

  return (
    <header className="header">
      <Link className="header-brand" to="/dashboard"><span className="brand-symbol">D</span><span>Dixon <strong>Command Center</strong></span></Link>
      <div className="header-actions">
        {user && <span className="header-user">{user.fullName} <small>{user.roleName}</small></span>}
        {user?.roleName === "Admin" && <Link className="header-admin-link" to="/admin">Admin</Link>}
        <button onClick={() => { onSignOut?.(); navigate("/"); }}>Sign out</button>
      </div>
    </header>
  );
}

export default Header;