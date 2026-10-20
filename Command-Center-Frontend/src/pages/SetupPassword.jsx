import { useState } from "react";
import { Link, useNavigate, useSearchParams } from "react-router-dom";
import { api } from "../api";
import "./SetupPassword.css";

export default function SetupPassword() {
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const token = searchParams.get("token") || "";

  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState("");
  const [success, setSuccess] = useState("");

  async function handleSubmit(event) {
    event.preventDefault();
    setError("");
    setSuccess("");

    if (!token) {
      setError("Invitation token missing. Please open the complete link from your email.");
      return;
    }

    if (password.length < 8 || password.length > 128) {
      setError("Password must be between 8 and 128 characters.");
      return;
    }

    if (password !== confirmPassword) {
      setError("Passwords do not match.");
      return;
    }

    setLoading(true);

    try {
      const result = await api.post("/auth/set-password", {
        token,
        password,
      });

      setSuccess(result?.message || "Password created successfully. You can now sign in.");
      setPassword("");
      setConfirmPassword("");

      window.setTimeout(() => navigate("/", { replace: true }), 1800);
    } catch (err) {
      setError(err.message || "Unable to set your password. Please try again.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <main className="setup-page">
      <section className="setup-brand-panel">
        <div className="setup-brand-content">
          <div className="setup-brand-mark">D</div>
          <p className="setup-eyebrow">DIXON TECHNOLOGIES</p>
          <h1>Command Center</h1>
          <p className="setup-brand-description">
            One connected workspace for production visibility, reporting and operational decisions.
          </p>

          <div className="setup-feature-list">
            <div><span>01</span><p>Secure account access</p></div>
            <div><span>02</span><p>Real-time operational visibility</p></div>
            <div><span>03</span><p>Connected plant workflows</p></div>
          </div>
        </div>
        <div className="setup-brand-footer">INTERNAL OPERATIONS PLATFORM</div>
      </section>

      <section className="setup-form-panel">
        <div className="setup-form-card">
          <div className="setup-mobile-brand">
            <span className="setup-brand-mark">D</span>
            <span>Dixon Command Center</span>
          </div>

          <div className="setup-icon" aria-hidden="true">
            <svg viewBox="0 0 24 24" fill="none">
              <rect x="5" y="10" width="14" height="11" rx="2" />
              <path d="M8 10V7a4 4 0 0 1 8 0v3" />
              <path d="M12 14v3" />
            </svg>
          </div>

          <p className="setup-form-eyebrow">ACCOUNT ACTIVATION</p>
          <h2>Create your password</h2>
          <p className="setup-form-description">
            Your account invitation is ready. Choose a strong password to complete your setup.
          </p>

          {!token && (
            <div className="setup-alert setup-alert-error" role="alert">
              Invitation token is missing. Open the complete setup link from your email.
            </div>
          )}

          {error && <div className="setup-alert setup-alert-error" role="alert">{error}</div>}
          {success && <div className="setup-alert setup-alert-success" role="status">{success}</div>}

          <form onSubmit={handleSubmit}>
            <label htmlFor="setup-password">New password</label>
            <div className="setup-password-field">
              <input
                id="setup-password"
                type={showPassword ? "text" : "password"}
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                placeholder="Enter at least 8 characters"
                minLength={8}
                maxLength={128}
                autoComplete="new-password"
                required
                disabled={loading || !token || Boolean(success)}
              />
              <button
                type="button"
                className="setup-show-password"
                onClick={() => setShowPassword((value) => !value)}
                aria-label={showPassword ? "Hide password" : "Show password"}
              >
                {showPassword ? "Hide" : "Show"}
              </button>
            </div>

            <label htmlFor="setup-confirm-password">Confirm password</label>
            <input
              id="setup-confirm-password"
              type={showPassword ? "text" : "password"}
              value={confirmPassword}
              onChange={(event) => setConfirmPassword(event.target.value)}
              placeholder="Enter your password again"
              minLength={8}
              maxLength={128}
              autoComplete="new-password"
              required
              disabled={loading || !token || Boolean(success)}
            />

            <p className="setup-password-hint">
              Use 8–128 characters. Keep your password private.
            </p>

            <button
              className="setup-submit-button"
              type="submit"
              disabled={loading || !token || Boolean(success)}
            >
              {loading ? "Setting up account..." : success ? "Password created" : "Set password"}
              {!loading && !success && <span aria-hidden="true">→</span>}
            </button>
          </form>

          <p className="setup-login-link">
            Already set up your account? <Link to="/">Back to sign in</Link>
          </p>

          <p className="setup-security-note">
            <span aria-hidden="true">●</span> Secure account setup
          </p>
        </div>
      </section>
    </main>
  );
}
