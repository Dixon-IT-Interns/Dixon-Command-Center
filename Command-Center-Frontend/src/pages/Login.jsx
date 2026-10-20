import { useEffect, useState } from "react";
import { api } from "../api";
import dixonLogo from "../assets/dixon-logo.jpeg";
import "./Login.css";

function Login({ onSignIn }) {
  const [setupRequired, setSetupRequired] = useState(null);
  const [fullName, setFullName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [showPassword, setShowPassword] = useState(false);

  useEffect(() => {
    api.get("/auth/setup-status")
      .then((status) => setSetupRequired(status.setupRequired))
      .catch((requestError) => setError(requestError.message));
  }, []);

  const handleSubmit = async (event) => {
    event.preventDefault();
    setBusy(true);
    setError("");

    try {
      const session = setupRequired
        ? await api.post("/auth/bootstrap", { fullName, email, password })
        : await api.post("/auth/login", { email, password });
      onSignIn(session);
    } catch (requestError) {
      setError(requestError.message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="login-page-shell">
      <main className="login-shell">
        <section className="login-panel">
          <div className="login-card">
            <div className="login-brand-row">
              <img src={dixonLogo} alt="Dixon" />
              <span>Digital Command Center</span>
            </div>

            <div className="login-heading">
              <span className="login-eyebrow">SECURE WORKSPACE</span>
              <h2>{setupRequired ? "Set up your workspace" : "Welcome back"}</h2>
            </div>

          <form onSubmit={handleSubmit} className="login-form">
            {setupRequired && (
              <label className="field">
                <span>Full name</span>
                <input autoComplete="name" required maxLength="100" placeholder="Your full name" value={fullName} onChange={(event) => setFullName(event.target.value)} />
              </label>
            )}

            <label className="field">
              <span>Work email</span>
              <input autoComplete="username" type="email" required maxLength="150" placeholder="name@dixoninfo.com" value={email} onChange={(event) => setEmail(event.target.value)} />
            </label>

            <label className="field">
              <span>Password</span>
              <div className="password-wrap">
                <input autoComplete={setupRequired ? "new-password" : "current-password"} type={showPassword ? "text" : "password"} required minLength="8" maxLength="128" placeholder="Enter your password" value={password} onChange={(event) => setPassword(event.target.value)} />
                <button type="button" className="password-toggle" onClick={() => setShowPassword((value) => !value)} aria-label={showPassword ? "Hide password" : "Show password"}>
                  {showPassword ? "Hide" : "Show"}
                </button>
              </div>
            </label>

            {!setupRequired && (
              <div className="login-options">
                <label className="remember"><input type="checkbox" defaultChecked /> <span>Keep me signed in</span></label>
                <button type="button" className="text-button" onClick={() => setError("Please contact your administrator to reset your password.")}>Forgot password?</button>
              </div>
            )}

            {error && <div className="form-error" role="alert"><span>!</span>{error}</div>}

            <button className="login-button" type="submit" disabled={busy || setupRequired === null}>
              <span>{busy ? "Connecting…" : setupRequired === null ? "Checking connection…" : setupRequired ? "Create administrator" : "Sign in"}</span>
              <span className="button-arrow">↗</span>
            </button>
            </form>
          </div>

          <p className="login-bottom">Dixon Digital Command Center · Factory reporting platform</p>
        </section>
      </main>
    </div>
  );
}

export default Login;
