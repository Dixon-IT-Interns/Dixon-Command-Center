import { useEffect, useState } from "react";
import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import AdminPage from "./pages/AdminPage";
import DashboardPage from "./pages/DashboardPage";
import Login from "./pages/Login";
import Products from "./pages/product";
import BrandPage from "./pages/BrandPage";
import ProcessPage from "./pages/ProcessPage";
import "./App.css";

function ProtectedRoute({ session, adminOnly = false, children }) {
  if (!session) {
    return <Navigate to="/" replace />;
  }

  if (adminOnly && session.user.roleName !== "Admin") {
    return <Navigate to="/dashboard" replace />;
  }

  return children;
}

function App() {
  return (
    <BrowserRouter>
      <AppRoutes />
    </BrowserRouter>
  );
}

function AppRoutes() {
  const [session, setSession] = useState(() => {
    try {
      return JSON.parse(localStorage.getItem("dcc-session"));
    } catch {
      return null;
    }
  });

  useEffect(() => {
    const expireSession = () => setSession(null);
    window.addEventListener("dcc-session-expired", expireSession);
    return () => window.removeEventListener("dcc-session-expired", expireSession);
  }, []);

  const signIn = (nextSession) => {
    localStorage.setItem("dcc-session", JSON.stringify(nextSession));
    localStorage.setItem("dcc-token", nextSession.token);
    setSession(nextSession);
  };

  const signOut = () => {
    localStorage.removeItem("dcc-session");
    localStorage.removeItem("dcc-token");
    setSession(null);
  };

  return (
    <Routes>
      <Route path="/" element={session ? <Navigate to="/dashboard" replace /> : <Login onSignIn={signIn} />} />
      <Route path="/dashboard" element={<ProtectedRoute session={session}><DashboardPage session={session} onSignOut={signOut} /></ProtectedRoute>} />
      <Route path="/products" element={<ProtectedRoute session={session}><Products session={session} onSignOut={signOut} /></ProtectedRoute>} />
      <Route path="/products/:customerId" element={<ProtectedRoute session={session}><BrandPage session={session} onSignOut={signOut} /></ProtectedRoute>} />
      <Route path="/products/:customerId/:categoryId" element={<ProtectedRoute session={session}><ProcessPage session={session} onSignOut={signOut} /></ProtectedRoute>} />
      <Route path="/admin" element={<ProtectedRoute session={session} adminOnly><AdminPage session={session} onSignOut={signOut} /></ProtectedRoute>} />
      <Route path="*" element={<Navigate to={session ? "/dashboard" : "/"} replace />} />
    </Routes>
  );
}

export default App;