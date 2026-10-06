import Navbar from "./Navbar";
import Sidebar from "./Sidebar";
import AppFooter from "../AppFooter";
import "./ContributorShell.css";

export default function ContributorLayout({ user, onSignOut, children }) {
  return (
    <div className="cc-contributor-shell">
      <Sidebar />
      <div className="cc-workspace">
        <Navbar user={user} onSignOut={onSignOut} />
        <div className="cc-content">{children}</div>
        <AppFooter user={user} />
      </div>
    </div>
  );
}
