import { useState } from "react";
import { Link } from "react-router-dom";
import { api } from "../api";
import "./Login.css";

export default function ForgotPassword() {
  const [email, setEmail] = useState("");
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  async function handleSubmit(event) {
    event.preventDefault();
    setBusy(true);
    setMessage("");
    setError("");

    try {
      const result = await api.post("/auth/forgot-password", { email });
      setMessage(result?.message || "If an active account exists for that email, a password reset link will be sent.");
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
              <span className="login-eyebrow">ACCOUNT RECOVERY</span>
              <h2>Forgot password?</h2>
              <p>Enter your work email and we'll send a reset link if your account is active.</p>
            </div>
            <form className="login-form" onSubmit={handleSubmit}>
              <label className="field">
                <span>Work email</span>
                <input type="email" required maxLength="150" autoComplete="email"
                  placeholder="name@dixoninfo.com" value={email}
                  onChange={(e) => setEmail(e.target.value)} />
              </label>
              {message && <p role="status">{message}</p>}
              {error && <div className="form-error" role="alert">{error}</div>}
              <button className="login-button" type="submit" disabled={busy}>
                {busy ? "Sending..." : "Send reset link"}
              </button>
              <Link className="text-button" to="/">Back to sign in</Link>
            </form>
          </div>
        </section>
      </main>
    </div>
  );
}
