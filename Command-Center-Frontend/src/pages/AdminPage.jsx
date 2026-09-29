import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../api";
import Header from "../components/Header";
import "./AdminPage.css";

const initialForm = { fullName: "", email: "", password: "", roleId: "", plantId: "" };

function AdminPage({ session, onSignOut }) {
  const [users, setUsers] = useState([]);
  const [roles, setRoles] = useState([]);
  const [plants, setPlants] = useState([]);
  const [form, setForm] = useState(initialForm);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);

  const loadData = () => Promise.all([
    api.get("/admin/users"),
    api.get("/admin/roles"),
    api.get("/admin/plants"),
  ]).then(([nextUsers, nextRoles, nextPlants]) => {
    setUsers(nextUsers);
    setRoles(nextRoles);
    setPlants(nextPlants);
    setForm((current) => ({ ...current, roleId: current.roleId || String(nextRoles[0]?.roleId || ""), plantId: current.plantId || String(nextPlants[0]?.plantId || "") }));
  });

  useEffect(() => {
    loadData().catch((requestError) => setError(requestError.message));
  }, []);

  const updateField = (event) => setForm({ ...form, [event.target.name]: event.target.value });

  const createUser = async (event) => {
    event.preventDefault();
    setBusy(true);
    setError("");
    setMessage("");
    try {
      await api.post("/admin/users", { ...form, roleId: Number(form.roleId), plantId: Number(form.plantId) });
      setForm({ ...initialForm, roleId: form.roleId, plantId: form.plantId });
      setMessage("User added.");
      await loadData();
    } catch (requestError) {
      setError(requestError.message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="workspace-page">
      <Header user={session.user} onSignOut={onSignOut} />
      <main className="workspace-content admin-content">
        <Link className="admin-back" to="/dashboard">← Dashboard</Link>
        <div className="admin-heading"><div><p className="eyebrow">Access control</p><h1>User administration</h1></div><span>{users.length} users</span></div>
        <div className="admin-layout">
          <section className="user-table-section">
            <h2>Workspace users</h2>
            <div className="user-list">
              {users.map((user) => (
                <div className="user-row" key={user.userId}>
                  <span className="user-initial">{user.fullName.slice(0, 1).toUpperCase()}</span>
                  <span className="user-detail"><strong>{user.fullName}</strong><small>{user.email || "No email"}</small></span>
                  <span className="user-role">{user.roleName}</span>
                  <span className={`user-status ${user.isActive ? "active" : "inactive"}`}>{user.isActive ? "Active" : "Inactive"}</span>
                </div>
              ))}
              {!users.length && <p className="empty-state">No users found.</p>}
            </div>
          </section>
          <section className="add-user-section">
            <h2>Add a user</h2>
            <form onSubmit={createUser}>
              <label>Full name<input name="fullName" required maxLength="100" value={form.fullName} onChange={updateField} /></label>
              <label>Work email<input name="email" type="email" required maxLength="150" value={form.email} onChange={updateField} /></label>
              <label>Temporary password<input name="password" type="password" required minLength="8" maxLength="128" value={form.password} onChange={updateField} /></label>
              <label>Role<select name="roleId" required value={form.roleId} onChange={updateField}>{roles.map((role) => <option value={role.roleId} key={role.roleId}>{role.roleName}</option>)}</select></label>
              <label>Plant<select name="plantId" required value={form.plantId} onChange={updateField}>{plants.map((plant) => <option value={plant.plantId} key={plant.plantId}>{plant.plantName}</option>)}</select></label>
              {error && <p className="notice error" role="alert">{error}</p>}
              {message && <p className="notice success" role="status">{message}</p>}
              <button className="admin-submit" type="submit" disabled={busy || !roles.length || !plants.length}>{busy ? "Adding…" : "Add user"}</button>
            </form>
          </section>
        </div>
      </main>
    </div>
  );
}

export default AdminPage;