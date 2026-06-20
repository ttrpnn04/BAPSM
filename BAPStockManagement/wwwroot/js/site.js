(function () {
  'use strict';

  /* ── Sidebar mobile toggle ── */
  var sidebar = document.getElementById('sidebar');
  var overlay = document.getElementById('sidebarOverlay');
  var toggle  = document.getElementById('sidebarToggle');
  var collapseToggle = document.getElementById('sidebarCollapseToggle');
  var collapsedStorageKey = 'bap-stock-sidebar-collapsed';

  function setSidebarCollapsed(collapsed) {
    document.body.classList.toggle('sidebar-collapsed', collapsed);

    if (collapseToggle) {
      collapseToggle.setAttribute('aria-expanded', collapsed ? 'false' : 'true');
      collapseToggle.setAttribute('aria-label', collapsed ? 'ขยายเมนู' : 'หุบเมนู');
    }

    try {
      window.localStorage.setItem(collapsedStorageKey, collapsed ? '1' : '0');
    } catch (_) {
      // Ignore storage failures; the toggle still works for the current page.
    }
  }

  try {
    setSidebarCollapsed(window.localStorage.getItem(collapsedStorageKey) === '1');
  } catch (_) {
    setSidebarCollapsed(false);
  }

  function openSidebar() {
    sidebar && sidebar.classList.add('is-open');
    overlay && overlay.classList.add('is-open');
    document.body.style.overflow = 'hidden';
  }

  function closeSidebar() {
    sidebar && sidebar.classList.remove('is-open');
    overlay && overlay.classList.remove('is-open');
    document.body.style.overflow = '';
  }

  toggle  && toggle.addEventListener('click', openSidebar);
  overlay && overlay.addEventListener('click', closeSidebar);
  collapseToggle && collapseToggle.addEventListener('click', function () {
    setSidebarCollapsed(!document.body.classList.contains('sidebar-collapsed'));
  });

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
    if (matched) {
      link.classList.add('active');
    }
  });

  /* ── Auto-dismiss alerts after 4s ── */
  document.querySelectorAll('.alert.alert-success').forEach(function (el) {
    setTimeout(function () {
      el.style.transition = 'opacity .4s';
      el.style.opacity = '0';
      setTimeout(function () { el.remove(); }, 400);
    }, 4000);
  });
})();
