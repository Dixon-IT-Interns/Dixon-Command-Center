const API_BASE_URL = import.meta.env.VITE_API_URL || "http://localhost:5210/api";

async function request(path, options = {}) {
  const headers = new Headers(options.headers);
  const token = localStorage.getItem("dcc-token");

  if (options.body) {
    headers.set("Content-Type", "application/json");
  }

  if (token) {
    headers.set("Authorization", `Bearer ${token}`);
  }

  let response;
  try {
    response = await fetch(`${API_BASE_URL}${path}`, { ...options, headers });
  } catch {
    throw new Error("Could not reach the API. Check the backend URL and that the API is running.");
  }

  if (!response.ok) {
    const error = await response.json().catch(() => null);
    if (response.status === 401) {
      localStorage.removeItem("dcc-session");
      localStorage.removeItem("dcc-token");
      window.dispatchEvent(new Event("dcc-session-expired"));
    }
    throw new Error(error?.message || `Request failed (${response.status}).`);
  }

  return response.status === 204 ? null : response.json();
}

export const api = {
  get: (path) => request(path),
  post: (path, body) => request(path, { method: "POST", body: JSON.stringify(body) }),
};