(function () {
  'use strict';

  var sidebar  = document.getElementById('sidebar');
  var overlay  = document.getElementById('sidebarOverlay');
  var toggle   = document.getElementById('sidebarCollapseToggle');
  var storageKey = 'bap-stock-sidebar-collapsed';

  /* ── Desktop: collapse / expand ── */
  function setSidebarCollapsed(collapsed) {
    document.body.classList.toggle('sidebar-collapsed', collapsed);
    try { window.localStorage.setItem(storageKey, collapsed ? '1' : '0'); } catch (_) {}
  }

  try {
    setSidebarCollapsed(window.localStorage.getItem(storageKey) === '1');
  } catch (_) {
    setSidebarCollapsed(false);
  }

  /* ── Mobile: slide open / close ── */
  function openSidebar() {
    sidebar  && sidebar.classList.add('is-open');
    overlay  && overlay.classList.add('is-open');
    document.body.style.overflow = 'hidden';
  }

  function closeSidebar() {
    sidebar  && sidebar.classList.remove('is-open');
    overlay  && overlay.classList.remove('is-open');
    document.body.style.overflow = '';
  }

  toggle && toggle.addEventListener('click', function () {
    if (window.innerWidth < 768) {
      /* mobile — slide open/close */
      if (sidebar && sidebar.classList.contains('is-open')) {
        closeSidebar();
      } else {
        openSidebar();
      }
    } else {
      /* desktop — collapse/expand */
      setSidebarCollapsed(!document.body.classList.contains('sidebar-collapsed'));
    }
  });

  overlay && overlay.addEventListener('click', closeSidebar);

  /* Close sidebar on nav link click (mobile) */
  sidebar && sidebar.querySelectorAll('.sb-link').forEach(function (link) {
    link.addEventListener('click', function () {
      if (window.innerWidth < 768) closeSidebar();
    });
  });

  /* ── Active nav highlight ── */
  var path = window.location.pathname.toLowerCase().replace(/\/$/, '');

  sidebar && sidebar.querySelectorAll('.sb-link[data-match]').forEach(function (link) {
    var patterns = link.getAttribute('data-match').toLowerCase().split(',');
    var matched = patterns.some(function (p) {
      p = p.trim();
      return p === path || (p.length > 1 && path.startsWith(p));
    });
    if (matched) link.classList.add('active');
  });

  /* ── Auto-dismiss success alerts after 4s ── */
  document.querySelectorAll('.alert.alert-success').forEach(function (el) {
    setTimeout(function () {
      el.style.transition = 'opacity .4s';
      el.style.opacity = '0';
      setTimeout(function () { el.remove(); }, 400);
    }, 4000);
  });
})();
