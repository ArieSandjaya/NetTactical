// Applies the saved colour theme before the page paints (no flash). Loaded as a plain script in <head>; the strict CSP allows it because it is a same-origin file.
(function () {
  var theme = 'light';
  try { var saved = localStorage.getItem('nt.theme'); if (saved === 'dark' || saved === 'light') theme = saved; } catch (e) { /* storage blocked: stay light */ }
  document.documentElement.setAttribute('data-theme', theme);
})();
