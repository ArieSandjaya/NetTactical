// Applies saved display choices before the page paints (no flash): colour theme and whether the desktop sidebar is hidden.
// Loaded as a plain script in <head>; the strict CSP allows it because it is a same-origin file.
(function () {
  var root = document.documentElement, theme = 'light', nav = 'open';
  try {
    var t = localStorage.getItem('nt.theme'); if (t === 'dark' || t === 'light') theme = t;
    if (localStorage.getItem('nt.nav') === 'closed') nav = 'closed';
  } catch (e) { /* storage blocked: defaults */ }
  root.setAttribute('data-theme', theme);
  root.setAttribute('data-nav', nav);
})();
