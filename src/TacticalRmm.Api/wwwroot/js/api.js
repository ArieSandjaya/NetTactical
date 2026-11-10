const KEY = 'nt.token';
const TIMEOUT_MS = 30000;

export const session = {
  get token() { try { return sessionStorage.getItem(KEY); } catch { return null; } },
  set(t) { try { sessionStorage.setItem(KEY, t); } catch { /* storage blocked */ } },
  clear() { try { sessionStorage.removeItem(KEY); } catch { /* ignore */ } },
};

export class ApiError extends Error {
  constructor(status, message) { super(message); this.status = status; }
}

async function parse(res) {
  const text = await res.text();
  if (!text) return null;
  try { return JSON.parse(text); } catch { return text; }
}

export async function api(path, { method = 'GET', body, signal } = {}) {
  const headers = { Accept: 'application/json' };
  if (session.token) headers.Authorization = `Token ${session.token}`;
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  // Every request has a deadline so a stuck server call can never hold the browser's few connections per site (which freezes the whole UI).
  const ac = new AbortController(); let timedOut = false;
  const timer = setTimeout(() => { timedOut = true; ac.abort(); }, TIMEOUT_MS);
  if (signal) { if (signal.aborted) ac.abort(); else signal.addEventListener('abort', () => ac.abort(), { once: true }); }
  let res, data;
  try {
    res = await fetch(path, { method, headers, signal: ac.signal, body: body === undefined ? undefined : JSON.stringify(body) });
    data = await parse(res);
  } catch (e) {
    if (timedOut) throw new ApiError(0, 'The server took too long to respond. Please try again.');
    if (e.name === 'AbortError') throw e;
    throw new ApiError(0, 'Cannot reach the server.');
  } finally { clearTimeout(timer); }

  if (res.status === 401 && path !== 'v2/login') {
    session.clear();
    location.hash = '#/login';
    throw new ApiError(401, 'Session expired, please sign in again.');
  }
  if (!res.ok) {
    const msg = (data && (data.detail || data.error)) || (res.status === 403 ? 'You do not have permission for this.' : `Request failed (${res.status}).`);
    throw new ApiError(res.status, msg);
  }
  return data;
}

/** POST/GET a file response. Returns { blob, filename }. Errors are reported like api(). */
export async function download(path, { method = 'POST' } = {}) {
  const headers = {};
  if (session.token) headers.Authorization = `Token ${session.token}`;
  let res;
  try { res = await fetch(path, { method, headers }); } catch { throw new ApiError(0, 'Cannot reach the server.'); }
  if (res.status === 401) { session.clear(); location.hash = '#/login'; throw new ApiError(401, 'Session expired, please sign in again.'); }
  if (!res.ok) {
    const data = await parse(res);
    throw new ApiError(res.status, (data && (data.detail || data.error)) || (res.status === 403 ? 'You do not have permission for this.' : `Request failed (${res.status}).`));
  }
  const cd = res.headers.get('Content-Disposition') || '';
  const m = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(cd);
  return { blob: await res.blob(), filename: m ? decodeURIComponent(m[1]) : 'connection.vnc' };
}

export async function login(username, password, twoFactor) {
  const body = { username, password };
  if (twoFactor) body.two_factor = twoFactor;
  return api('v2/login', { method: 'POST', body });
}

export const logout = () => api('logout', { method: 'POST' }).catch(() => {});
