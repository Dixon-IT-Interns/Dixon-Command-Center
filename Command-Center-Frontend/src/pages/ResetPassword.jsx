import { useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { api } from "../api";
import "./Login.css";

export default function ResetPassword() {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const token = params.get("token") || "";
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");
  const [busy, setBusy] = useState(false);

  async function handleSubmit(event) {
    event.preventDefault();
    setError("");
    setSuccess("");

    if (!token) {
      setError("Reset link is missing or invalid. Request a new link.");
      return;
    }
    if (password !== confirmPassword) {
      setError("Passwords do not match.");
      return;
    }

    setBusy(true);
    try {
      const result = await api.post("/auth/reset-password", { token, password });
      setSuccess(result?.message || "Password reset successfully. You can now sign in.");
      setPassword("");
      setConfirmPassword("");
    } catch (err) {
      setError(err.message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="login-page-shell">
      <main className="login-shell">
        <section className="login-panel">
          <div className="login-card">
            <div className="login-brand-row">
              <span>Dixon</span>
              <span>Digital Command Center</span>
            </div>
            <div className="login-heading">
              <span className="login-eyebrow">ACCOUNT SECURITY</span>
              <h2>Reset password</h2>
              <p>Choose a new password with at least 8 characters.</p>
            </div>
            <form className="login-form" onSubmit={handleSubmit}>
              <label className="field">
                <span>New password</span>
                <input type="password" required minLength="8" maxLength="128"
                  autoComplete="new-password" value={password}
                  onChange={(e) => setPassword(e.target.value)} />
              </label>
              <label className="field">
                <span>Confirm new password</span>
                <input type="password" required minLength="8" maxLength="128"
                  autoComplete="new-password" value={confirmPassword}
                  onChange={(e) => setConfirmPassword(e.target.value)} />
              </label>
              {error && <div className="form-error" role="alert">{error}</div>}
              {success && <p role="status">{success}</p>}
              {success
                ? <button className="login-button" type="button" onClick={() => navigate("/")}>Go to sign in</button>
                : <button className="login-button" type="submit" disabled={busy || !token}>{busy ? "Updating..." : "Reset password"}</button>}
              <Link className="text-button" to="/">Back to sign in</Link>
            </form>
          </div>
        </section>
      </main>
    </div>
  );
}
