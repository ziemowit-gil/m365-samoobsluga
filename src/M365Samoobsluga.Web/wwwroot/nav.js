// Zamyka rozwinięte menu Administrator po kliknięciu poza nim, po wybraniu pozycji i klawiszem Esc (fokus wraca na przycisk menu).
(function () {
  function menus() { return document.querySelectorAll('details.nav-dd'); }
  document.addEventListener('click', function (e) {
    menus().forEach(function (d) { if (d.open && !d.contains(e.target)) d.open = false; });
    var a = e.target.closest && e.target.closest('details.nav-dd a');
    if (a) { var d = a.closest('details.nav-dd'); if (d) d.open = false; }
  });
  document.addEventListener('keydown', function (e) {
    if (e.key !== 'Escape') return;
    menus().forEach(function (d) { if (d.open) { d.open = false; var s = d.querySelector('summary'); if (s) s.focus(); } });
  });
})();
