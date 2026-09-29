import { useEffect, useState } from "react";
import leftPanel from "../assets/hero.png";
import { api } from "../api";
import "./Login.css";

function Login({ onSignIn }) {
  const [setupRequired, setSetupRequired] = useState(null);
  const [fullName, setFullName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

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
    <div className="login-page">

      {/* LEFT VISUAL SECTION */}
      <div className="login-left">
        <img
          src={leftPanel}
          alt="Dixon Command Center"
          className="left-panel-image"
        />
      </div>


      {/* RIGHT LOGIN SECTION */}
      <div className="login-right">

        <div className="login-form">

          {/* Logo */}
          <div className="login-logo">DIXON TECHNOLOGIES</div>

          <h1>{setupRequired ? "Create the first admin" : "Welcome back"}</h1>

          <p className="welcome-text">
            {setupRequired ? "Initialize administrator access for this workspace." : "Sign in to access the Dixon Command Center."}
          </p>

          <form onSubmit={handleSubmit}>
            {setupRequired && (
              <label>
                Full name
                <input autoComplete="name" required maxLength="100" value={fullName} onChange={(event) => setFullName(event.target.value)} />
              </label>
            )}
            <label>
              Work email
              <input autoComplete="username" type="email" required maxLength="150" placeholder="name@company.com" value={email} onChange={(event) => setEmail(event.target.value)} />
            </label>
            <label>
              Password
              <input autoComplete={setupRequired ? "new-password" : "current-password"} type="password" required minLength="8" maxLength="128" value={password} onChange={(event) => setPassword(event.target.value)} />
            </label>
            {error && <p className="form-error" role="alert">{error}</p>}
            <button className="login-button" type="submit" disabled={busy || setupRequired === null}>
              {busy ? "Connecting..." : setupRequired === null ? "Checking connection..." : setupRequired ? "Set up administrator" : "Sign in"}
              <span aria-hidden="true">→</span>
            </button>
          </form>

        </div>

      </div>

    </div>
  );
}

export default Login;