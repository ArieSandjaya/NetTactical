const dt = new Intl.DateTimeFormat('id-ID', { dateStyle: 'medium', timeStyle: 'short' });
const day = new Intl.DateTimeFormat('id-ID', { day: 'numeric', month: 'short' });

export const fmtDate = (iso) => (iso ? dt.format(new Date(iso)) : '—');
export const fmtDay = (iso) => day.format(new Date(iso));

export function ago(iso, now = Date.now()) {
  if (!iso) return '—';
  const sec = Math.max(0, Math.round((now - new Date(iso).getTime()) / 1000));
  if (sec < 60) return 'baru saja';
  const min = Math.round(sec / 60); if (min < 60) return `${min} mnt lalu`;
  const hr = Math.round(min / 60); if (hr < 48) return `${hr} jam lalu`;
  return `${Math.round(hr / 24)} hari lalu`;
}

// Level skor dari API -> kelas warna dan teks.
export const levelClass = (l) => ({ healthy: 'good', attention: 'warn', at_risk: 'crit' }[l] || 'warn');
export const levelWord = (l) => ({ healthy: 'Sehat', attention: 'Perlu perhatian', at_risk: 'Berisiko' }[l] || '—');
// Untuk rata-rata per client (API hanya memberi angka): ambang bawaan sama dengan HealthWeights.
export const scoreClass = (n) => (n >= 80 ? 'good' : n >= 60 ? 'warn' : 'crit');

export const sevClass = (s) => ({ error: 'crit', warning: 'warn', info: 'info' }[s] || 'info');
export const sevWord = (s) => ({ error: 'Kritis', warning: 'Peringatan', info: 'Info' }[s] || s);

export const statusClass = (s) => ({ passing: 'good', failing: 'crit', pending: 'neutral' }[s] || 'neutral');
export const statusWord = (s) => ({ passing: 'Lulus', failing: 'Gagal', pending: 'Menunggu' }[s] || s);
export const syncWord = (s) => ({ synced: 'Tersinkron', notsynced: 'Belum tersinkron', pendingdeletion: 'Menunggu dihapus', initial: 'Baru' }[s] || s);
export const patchWord = (s) => ({ failed: 'Gagal dipasang', pending: 'Tertunda', ignored: 'Diabaikan' }[s] || s);
export const patchClass = (s) => ({ failed: 'crit', pending: 'warn', ignored: 'neutral' }[s] || 'neutral');

export const CHECK_TYPES = { cpuload: 'CPU', memory: 'Memori', diskspace: 'Disk', ping: 'Ping', winsvc: 'Layanan Windows', script: 'Script', eventlog: 'Event log' };
export const checkType = (t) => CHECK_TYPES[t] || t;
export const KIND_LABELS = { check: 'Check', task: 'Task terjadwal', availability: 'Ketersediaan', alert: 'Alert', patch: 'Patch Windows', agent: 'Agent' };
export const HINTS = {
  check: 'Buka output di atas, lalu jalankan ulang check setelah memperbaiki penyebabnya.',
  task: 'Periksa stderr dan kode keluar. Task akan dijalankan lagi sesuai jadwalnya, atau jalankan manual.',
  availability: 'Periksa daya dan jaringan perangkat, lalu layanan "Tactical RMM Agent" dan firewall lokal.',
  patch: 'Jadwalkan reboot di luar jam kerja, lalu pasang ulang update yang gagal.',
  agent: 'Perbarui agent ke versi terbaru.',
  alert: 'Tinjau pesan alert dan tindak lanjuti sesuai prosedur.',
};
export const METRIC_NAMES = { cpu: 'CPU', memory: 'Memori', disk: 'Disk' };
