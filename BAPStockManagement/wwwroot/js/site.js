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

  var palette = [
    { keys: ['ดำ'], bg: '#1e293b', text: '#f8fafc', dot: '#94a3b8' },
    { keys: ['แดง'], bg: '#fee2e2', text: '#991b1b', dot: '#ef4444' },
    { keys: ['น้ำเงิน'], bg: '#dbeafe', text: '#1e40af', dot: '#3b82f6' },
    { keys: ['ฟ้า', 'ธงฟ้า'], bg: '#e0f2fe', text: '#0369a1', dot: '#0ea5e9' },
    { keys: ['ส้ม'], bg: '#ffedd5', text: '#9a3412', dot: '#f97316' },
    { keys: ['เขียวออ่อน', 'เขียวอ่อน'], bg: '#dcfce7', text: '#166534', dot: '#4ade80' },
    { keys: ['เขียว'], bg: '#d1fae5', text: '#065f46', dot: '#10b981' },
    { keys: ['ชมพูอ่อน'], bg: '#fdf2f8', text: '#9d174d', dot: '#f9a8d4' },
    { keys: ['ชมพูเข้ม', 'ชมพู', 'ชม'], bg: '#fce7f3', text: '#9d174d', dot: '#ec4899' },
    { keys: ['ทอง'], bg: '#fef3c7', text: '#78350f', dot: '#f59e0b' },
    { keys: ['เหลือง', 'ครีม'], bg: '#fef9c3', text: '#713f12', dot: '#eab308' },
    { keys: ['ม่วง'], bg: '#ede9fe', text: '#4c1d95', dot: '#8b5cf6' },
    { keys: ['น้ำตาล'], bg: '#fef3c7', text: '#92400e', dot: '#b45309' },
    { keys: ['เทา'], bg: '#f1f5f9', text: '#475569', dot: '#94a3b8' },
    { keys: ['ขาว'], bg: '#f8fafc', text: '#334155', dot: '#cbd5e1' },
    { keys: ['วัว'], bg: '#fef2f2', text: '#7f1d1d', dot: '#fca5a5' }
  ];

  function resolveColor(name) {
    name = name || '';
    for (var i = 0; i < palette.length; i += 1) {
      if (palette[i].keys.some(function (key) { return name.indexOf(key) >= 0; })) {
        return palette[i];
      }
    }
    return { bg: '#f4f4f5', text: '#52525b', dot: '#94a3b8' };
  }

  function applyVariantColors(root) {
    root = root || document;
    root.querySelectorAll('.color-chip[data-vname]').forEach(function (chip) {
      var col = resolveColor(chip.dataset.vname || '');
      chip.style.setProperty('--chip-bg', col.bg);
      chip.style.setProperty('--chip-text', col.text);
      chip.style.setProperty('--chip-dot', col.dot);
    });

    root.querySelectorAll('.color-swatch[data-vname]').forEach(function (swatch) {
      var col = resolveColor(swatch.dataset.vname || '');
      swatch.style.setProperty('--swatch-color', col.dot);
    });
  }

  window.BapStockColors = {
    resolve: resolveColor,
    apply: applyVariantColors
  };

  applyVariantColors(document);
})();
