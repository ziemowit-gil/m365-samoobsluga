// Karty wstążki (wzorzec WAI-ARIA tabs): kliknięcie, strzałki lewo/prawo, Home i End. Bez skryptu widoczna jest karta ustawiona przez serwer.
(function () {
  function select(tabs, tab, focus) {
    tabs.forEach(function (t) {
      var on = t === tab;
      t.setAttribute('aria-selected', on ? 'true' : 'false');
      t.tabIndex = on ? 0 : -1;
      var panel = document.getElementById(t.getAttribute('aria-controls'));
      if (panel) panel.hidden = !on;
    });
    if (focus) tab.focus();
  }
  document.addEventListener('click', function (e) {
    var tab = e.target.closest && e.target.closest('.ribbon-tab');
    if (!tab) return;
    var tabs = Array.prototype.slice.call(tab.parentElement.querySelectorAll('.ribbon-tab'));
    select(tabs, tab, false);
  });
  document.addEventListener('keydown', function (e) {
    var tab = e.target.closest && e.target.closest('.ribbon-tab');
    if (!tab) return;
    var tabs = Array.prototype.slice.call(tab.parentElement.querySelectorAll('.ribbon-tab'));
    var i = tabs.indexOf(tab), n = tabs.length, next = null;
    if (e.key === 'ArrowRight') next = tabs[(i + 1) % n];
    else if (e.key === 'ArrowLeft') next = tabs[(i - 1 + n) % n];
    else if (e.key === 'Home') next = tabs[0];
    else if (e.key === 'End') next = tabs[n - 1];
    if (next) { e.preventDefault(); select(tabs, next, true); }
  });
})();
