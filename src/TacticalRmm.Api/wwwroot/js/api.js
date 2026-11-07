const KEY = 'nt.token';

export const session = {
  get token() { try { return sessionStorage.getItem(KEY); } catch { return null; } },
  set(t) { try { sessionStorage.setItem(KEY, t); } catch { /* storage diblokir */ } },
  clear() { try { sessionStorage.removeItem(KEY); } catch { /* abaikan */ } },
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
  let res;
  try { res = await fetch(path, { method, headers, signal, body: body === undefined ? undefined : JSON.stringify(body) }); }
  catch (e) { if (e.name === 'AbortError') throw e; throw new ApiError(0, 'Tidak dapat menghubungi server.'); }

  const data = await parse(res);
  if (res.status === 401 && path !== 'v2/login') {
    session.clear();
    location.hash = '#/login';
    throw new ApiError(401, 'Sesi berakhir, silakan masuk lagi.');
  }
  if (!res.ok) {
    const msg = (data && (data.detail || data.error)) || (res.status === 403 ? 'Anda tidak punya izin untuk ini.' : `Permintaan gagal (${res.status}).`);
    throw new ApiError(res.status, msg);
  }
  return data;
}

export async function login(username, password, twoFactor) {
  const body = { username, password };
  if (twoFactor) body.two_factor = twoFactor;
  return api('v2/login', { method: 'POST', body });
}

export const logout = () => api('logout', { method: 'POST' }).catch(() => {});
