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

  document.querySelectorAll('input[data-date-format="dd/MM/yyyy"], input[placeholder="วัน/เดือน/ปี"]').forEach(function (control) {
    control.addEventListener('input', function () {
      var cursorAtEnd = control.selectionStart === control.value.length;
      var digits = control.value.replace(/\D/g, '').slice(0, 8);
      var parts = [];

      if (digits.length > 0) parts.push(digits.slice(0, 2));
      if (digits.length > 2) parts.push(digits.slice(2, 4));
      if (digits.length > 4) parts.push(digits.slice(4, 8));

      control.value = parts.join('/');
      if (cursorAtEnd) {
        control.setSelectionRange(control.value.length, control.value.length);
      }
    });
  });

  /* ── Thai date pickers (Flatpickr) ── */
  function initThaiDatePickers(root) {
    if (typeof flatpickr === 'undefined') return;

    var locale = (flatpickr.l10ns && flatpickr.l10ns.th) || 'default';
    (root || document).querySelectorAll('input.js-datepicker').forEach(function (control) {
      if (control._flatpickr) return;

      flatpickr(control, {
        locale: locale,
        dateFormat: 'Y-m-d',
        altInput: true,
        altFormat: 'j F Y',
        altInputClass: 'form-control',
        allowInput: false,
        disableMobile: true,
        onChange: function () {
          control.dispatchEvent(new Event('change', { bubbles: true }));
        }
      });
    });
  }

  initThaiDatePickers(document);

  /* ── Auto-submit filter bars ── */
  document.querySelectorAll('.filter-bar form[method="get"]').forEach(function (form) {
    var debounceTimer = null;
    var suggestTimer = null;
    var isComposing = false;

    function submitFilter() {
      window.clearTimeout(debounceTimer);
      if (typeof form.requestSubmit === 'function') {
        form.requestSubmit();
      } else {
        form.submit();
      }
    }

    function submitFilterSoon() {
      window.clearTimeout(debounceTimer);
      debounceTimer = window.setTimeout(submitFilter, 900);
    }

    form.querySelectorAll('select, input[type="date"], input[type="number"], input.js-datepicker').forEach(function (control) {
      control.addEventListener('change', submitFilter);
    });

    function attachSuggestions(control) {
      var wrap = document.createElement('div');
      var list = document.createElement('div');
      wrap.className = 'search-suggest-wrap';
      list.className = 'search-suggest-list d-none';

      control.parentNode.insertBefore(wrap, control);
      wrap.appendChild(control);
      wrap.appendChild(list);

      function hideSuggestions() {
        list.classList.add('d-none');
        list.innerHTML = '';
      }

      function renderSuggestions(items) {
        list.innerHTML = '';
        if (!items.length) {
          hideSuggestions();
          return;
        }

        items.forEach(function (item) {
          var button = document.createElement('button');
          button.type = 'button';
          button.className = 'search-suggest-item';
          button.textContent = item;
          button.addEventListener('mousedown', function (event) {
            event.preventDefault();
            control.value = item;
            hideSuggestions();
            submitFilter();
          });
          list.appendChild(button);
        });

        list.classList.remove('d-none');
      }

      function fetchSuggestions() {
        var value = control.value.trim();
        if (value.length < 1) {
          hideSuggestions();
          return;
        }

        fetch('/Search/Suggestions?term=' + encodeURIComponent(value), {
          headers: { 'X-Requested-With': 'XMLHttpRequest' }
        })
          .then(function (response) { return response.ok ? response.json() : []; })
          .then(renderSuggestions)
          .catch(hideSuggestions);
      }

      control.addEventListener('input', function () {
        window.clearTimeout(suggestTimer);
        if (!isComposing) {
          suggestTimer = window.setTimeout(fetchSuggestions, 180);
        }
      });

      control.addEventListener('focus', fetchSuggestions);
      control.addEventListener('blur', function () {
        window.setTimeout(hideSuggestions, 120);
      });
    }

    form.querySelectorAll('input[type="text"], input[type="search"]').forEach(function (control) {
      control.addEventListener('compositionstart', function () {
        isComposing = true;
      });

      control.addEventListener('compositionend', function () {
        isComposing = false;
        submitFilterSoon();
      });

      control.addEventListener('input', function () {
        if (!isComposing) submitFilterSoon();
      });

      if (control.name === 'search') {
        attachSuggestions(control);
      }
    });
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

  function escapeHtml(value) {
    return String(value == null ? '' : value)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#39;');
  }

  function setToastMessage(el, msg, iconClass) {
    el.textContent = '';
    var icon = document.createElement('i');
    icon.className = (iconClass || 'bi bi-check-circle') + ' me-1';
    el.appendChild(icon);
    el.appendChild(document.createTextNode(String(msg == null ? '' : msg)));
    var closeBtn = document.createElement('button');
    closeBtn.type = 'button';
    closeBtn.className = 'btn-close';
    closeBtn.setAttribute('data-bs-dismiss', 'alert');
    el.appendChild(closeBtn);
  }

  window.BapStockColors = {
    resolve: resolveColor,
    apply: applyVariantColors
  };

  window.BapStockHtml = {
    escape: escapeHtml,
    setToastMessage: setToastMessage
  };

  applyVariantColors(document);
})();
