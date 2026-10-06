import { useEffect, useState } from "react";
import { BrowserRouter, Navigate, Route, Routes, useParams } from "react-router-dom";
import AdminPage from "./pages/AdminPage";
import DashboardPage from "./pages/DashboardPage";
import ApprovalPage from "./pages/ApprovalPage";
import ApprovalOverview from "./pages/ApprovalOverview";
import ApprovalHistory from "./pages/ApprovalHistory";
import ContributorReports from "./pages/contributor/ContributorReports";
import ContributorActivity from "./pages/contributor/ContributorActivity";
import ContributorDashboard from "./pages/contributor/ContributorDashboard";
import Login from "./pages/Login";
import BrandSelection from "./pages/contributor/BrandSelection";
import CategorySelection from "./pages/contributor/CategorySelection";
import DailyReport from "./pages/contributor/DailyReport";
import "./App.css";

function ProtectedRoute({ session, children }) {
  return session ? children : <Navigate to="/" replace />;
}

function ContributorRoute({ session, children }) {
  if (!session) return <Navigate to="/" replace />;
  if (session.user.roleName !== "Contributor") return <Navigate to="/dashboard" replace />;
  return children;
}

function ApproverRoute({ session, children }) {
  if (!session) return <Navigate to="/" replace />;
  if (session.user.roleName !== "Approver") return <Navigate to="/dashboard" replace />;
  return children;
}

function AdminRoute({ session, children }) {
  if (!session) return <Navigate to="/" replace />;
  if (session.user.roleName !== "Admin") return <Navigate to="/dashboard" replace />;
  return children;
}

export default function App() {
  return <BrowserRouter><AppRoutes /></BrowserRouter>;
}

function AppRoutes() {
  const [session, setSession] = useState(() => {
    try { return JSON.parse(localStorage.getItem("dcc-session")); } catch { return null; }
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

  const home = session
    ? session.user.roleName === "Contributor" ? "/contributor/dashboard"
      : session.user.roleName === "Approver" ? "/approvals/overview"
      : "/dashboard"
    : "/";

  return (
    <Routes>
      <Route path="/" element={session ? <Navigate to={home} replace /> : <Login onSignIn={signIn} />} />

      <Route path="/contributor/dashboard" element={<ContributorRoute session={session}><ContributorDashboard session={session} onSignOut={signOut}/></ContributorRoute>} />
      <Route path="/contributor/reports" element={<ContributorRoute session={session}><ContributorReports session={session} onSignOut={signOut}/></ContributorRoute>} />
      <Route path="/contributor/activity" element={<ContributorRoute session={session}><ContributorActivity session={session} onSignOut={signOut}/></ContributorRoute>} />
      <Route path="/approvals/overview" element={<ApproverRoute session={session}><ApprovalOverview session={session} onSignOut={signOut}/></ApproverRoute>} />
      <Route path="/approvals" element={<ApproverRoute session={session}><ApprovalPage session={session} onSignOut={signOut}/></ApproverRoute>} />
      <Route path="/approvals/history" element={<ApproverRoute session={session}><ApprovalHistory session={session} onSignOut={signOut}/></ApproverRoute>} />

      <Route path="/contributor/brands" element={<ContributorRoute session={session}><BrandSelection session={session} onSignOut={signOut}/></ContributorRoute>} />
      <Route path="/contributor/brands/:customerId/categories" element={<ContributorRoute session={session}><CategorySelection session={session} onSignOut={signOut}/></ContributorRoute>} />
      <Route path="/contributor/brands/:customerId/categories/:categoryId/report" element={<ContributorRoute session={session}><DailyReport session={session} onSignOut={signOut}/></ContributorRoute>} />
      <Route path="/contributor/brands/:customerId/categories/:categoryId/lines" element={<Navigate to="../report" replace />} />
      <Route path="/contributor/brands/:customerId/categories/:categoryId/lines/:lineId/report" element={<Navigate to="../../report" replace />} />

      {/* Backward-compatible redirects for the old contributor URLs. */}
      <Route path="/products" element={<Navigate to="/contributor/brands" replace />} />
      <Route path="/products/:customerId" element={<LegacyCategoryRedirect />} />
      <Route path="/products/:customerId/:categoryId" element={<LegacyLineRedirect />} />
      <Route path="/line-selection" element={<Navigate to="/contributor/brands" replace />} />
      <Route path="/daily-report" element={<Navigate to="/contributor/brands" replace />} />

      <Route path="/dashboard" element={<ProtectedRoute session={session}><DashboardPage session={session} onSignOut={signOut}/></ProtectedRoute>} />
      <Route path="/admin" element={<AdminRoute session={session}><AdminPage session={session} onSignOut={signOut}/></AdminRoute>} />
      <Route path="*" element={<Navigate to={home} replace />} />
    </Routes>
  );
}

function LegacyCategoryRedirect() {
  const { customerId } = useParams();
  return <Navigate to={`/contributor/brands/${customerId}/categories`} replace />;
}

function LegacyLineRedirect() {
  const { customerId, categoryId } = useParams();
  return <Navigate to={`/contributor/brands/${customerId}/categories/${categoryId}/lines`} replace />;
}
