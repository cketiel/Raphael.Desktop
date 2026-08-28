/* Raphael help — client behaviour.
 *
 * Runs identically from a file:// path, from the app's virtual host and from the Portal, so it
 * never uses fetch(): the search index arrives as a plain script that assigns a global. A dispatch
 * office is the last place where the help should depend on anything being reachable.
 *
 * Everything here degrades. With scripting off the page is still a readable topic with its
 * contents rail and its links; what is lost is search, the scroll spy and the bridge back to the
 * application.
 */

(function () {
  'use strict';

  var PAGE = window.__HELP_PAGE__ || {};
  var INDEX = window.__HELP_INDEX__ || { docs: [], synonyms: {} };
  var T = PAGE.ui || {};

  function format(template, values) {
    return String(template || '').replace(/\{(\d+)\}/g, function (whole, position) {
      var value = values[Number(position)];
      return value === undefined ? whole : value;
    });
  }

  function el(tag, className, text) {
    var node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
  }

  // -------------------------------------------------------------------- host bridge

  /* The application injects this before the page settles. Outside the app it never arrives, and
   * everything that depends on it stays quietly switched off rather than failing loudly. */
  var host = null;

  window.__helpSetHost = function (info) {
    host = info || {};
    applyHostState();
  };

  function inApp() {
    return !!(window.chrome && window.chrome.webview);
  }

  function postToHost(message) {
    if (!inApp()) return false;
    try {
      window.chrome.webview.postMessage(message);
      return true;
    } catch (error) {
      return false;
    }
  }

  function applyHostState() {
    if (!host) return;

    if (host.theme === 'dark' || host.theme === 'light') {
      document.documentElement.setAttribute('data-theme', host.theme);
    }

    renderStaleBanner();
    applyRoleVisibility();
  }

  // -------------------------------------------------------------------- staleness

  /* The manifest says which application version this help was written against. If the application
   * is ahead, the page says so. A document that quietly describes the previous version is worse
   * than one that admits it is behind, because the reader believes it. */
  function compareVersions(left, right) {
    var a = String(left || '0').split('.').map(Number);
    var b = String(right || '0').split('.').map(Number);
    for (var position = 0; position < Math.max(a.length, b.length); position += 1) {
      var difference = (a[position] || 0) - (b[position] || 0);
      if (difference) return difference < 0 ? -1 : 1;
    }
    return 0;
  }

  function renderStaleBanner() {
    var slot = document.querySelector('[data-stale-slot]');
    if (!slot || !host || !host.appVersion) return;
    if (compareVersions(host.appVersion, PAGE.appVersion) <= 0) return;

    slot.innerHTML = '';
    var banner = el('div', 'rf-banner');
    banner.setAttribute('role', 'status');
    banner.appendChild(el('span', null, '▲'));

    var body = el('div');
    body.appendChild(el('strong', null, T.staleTitle || ''));
    body.appendChild(document.createTextNode(' '));
    body.appendChild(document.createTextNode(format(T.staleBody, [PAGE.appVersion, host.appVersion])));
    banner.appendChild(body);

    slot.appendChild(banner);
  }

  /* Role-aware contents. A user who cannot see the Admin tab is not offered its topics: an
   * instruction you are not allowed to follow reads as a broken product. */
  function applyRoleVisibility() {
    if (!host || !host.role) return;
    var role = String(host.role).toLowerCase();

    document.querySelectorAll('[data-roles]').forEach(function (node) {
      var roles = node.getAttribute('data-roles').split(' ').filter(Boolean);
      if (roles.length && roles.indexOf(role) === -1) {
        var item = node.closest('li') || node;
        item.hidden = true;
      }
    });
  }

  // -------------------------------------------------------------------- theme

  var STORAGE_THEME = 'raphael.help.theme';

  function readStoredTheme() {
    try { return window.localStorage.getItem(STORAGE_THEME); } catch (error) { return null; }
  }

  function storeTheme(value) {
    try { window.localStorage.setItem(STORAGE_THEME, value); } catch (error) { /* private window */ }
  }

  (function initTheme() {
    var stored = readStoredTheme();
    if (stored === 'dark' || stored === 'light') {
      document.documentElement.setAttribute('data-theme', stored);
    }

    var button = document.querySelector('[data-theme-toggle]');
    if (!button) return;

    button.addEventListener('click', function () {
      var current = document.documentElement.getAttribute('data-theme');
      var prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
      var next = current ? (current === 'dark' ? 'light' : 'dark') : (prefersDark ? 'light' : 'dark');
      document.documentElement.setAttribute('data-theme', next);
      storeTheme(next);
    });
  })();

  // -------------------------------------------------------------------- contents drawer

  (function initNav() {
    var toggle = document.querySelector('[data-nav-toggle]');
    var nav = document.getElementById('rf-nav');
    if (!toggle || !nav) return;

    toggle.addEventListener('click', function () {
      var open = nav.getAttribute('data-open') === 'true';
      nav.setAttribute('data-open', String(!open));
      toggle.setAttribute('aria-expanded', String(!open));
    });

    nav.addEventListener('click', function (event) {
      if (event.target.closest('a')) {
        nav.setAttribute('data-open', 'false');
        toggle.setAttribute('aria-expanded', 'false');
      }
    });
  })();

  // -------------------------------------------------------------------- on this page

  (function initScrollSpy() {
    var links = Array.prototype.slice.call(document.querySelectorAll('.rf-aside a[href^="#"]'));
    if (!links.length || !('IntersectionObserver' in window)) return;

    var byAnchor = {};
    links.forEach(function (link) { byAnchor[link.getAttribute('href').slice(1)] = link; });

    var visible = new Set();

    var observer = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (entry.isIntersecting) visible.add(entry.target.id);
        else visible.delete(entry.target.id);
      });

      links.forEach(function (link) { link.removeAttribute('aria-current'); });

      var first = Object.keys(byAnchor).filter(function (id) { return visible.has(id); })[0];
      if (first && byAnchor[first]) byAnchor[first].setAttribute('aria-current', 'true');
    }, { rootMargin: '-72px 0px -70% 0px' });

    Object.keys(byAnchor).forEach(function (id) {
      var heading = document.getElementById(id);
      if (heading) observer.observe(heading);
    });
  })();

  // -------------------------------------------------------------------- glossary popovers

  (function initGlossary() {
    var popover = null;

    function hide() {
      if (popover) { popover.remove(); popover = null; }
    }

    function show(term) {
      hide();
      var summary = term.getAttribute('data-term-summary');
      if (!summary) return;

      popover = el('div', 'rf-popover');
      popover.setAttribute('role', 'tooltip');
      popover.appendChild(el('strong', null, term.getAttribute('data-term-title') || term.textContent));
      popover.appendChild(el('span', null, summary));

      Object.assign(popover.style, {
        position: 'absolute', zIndex: '50', maxWidth: '320px',
        padding: '.6rem .75rem', borderRadius: '10px',
        background: 'var(--rf-bg-raised)', color: 'var(--rf-text)',
        border: '1px solid var(--rf-border)', boxShadow: 'var(--rf-shadow)',
        fontSize: '.85rem', lineHeight: '1.5', display: 'grid', gap: '.2rem'
      });

      document.body.appendChild(popover);

      var box = term.getBoundingClientRect();
      var top = box.bottom + window.scrollY + 6;
      var left = Math.min(box.left + window.scrollX, window.innerWidth - popover.offsetWidth - 16);
      popover.style.top = top + 'px';
      popover.style.left = Math.max(8, left) + 'px';
    }

    document.addEventListener('mouseover', function (event) {
      var term = event.target.closest ? event.target.closest('.help-term') : null;
      if (term) show(term);
    });
    document.addEventListener('mouseout', function (event) {
      if (event.target.closest && event.target.closest('.help-term')) hide();
    });
    document.addEventListener('focusin', function (event) {
      var term = event.target.closest ? event.target.closest('.help-term') : null;
      if (term) show(term);
    });
    document.addEventListener('focusout', hide);
    window.addEventListener('scroll', hide, { passive: true });
  })();

  // -------------------------------------------------------------------- "open it for me"

  /* The help stops describing the route and walks it. Outside the application the link says so
   * instead of failing silently, which is the difference between a dead link and an honest one. */
  (function initActions() {
    var available = inApp();

    document.querySelectorAll('.help-action').forEach(function (link) {
      if (!available) {
        link.setAttribute('data-unavailable', 'true');
        link.setAttribute('title', T.actionUnavailable || '');
      }
    });

    document.addEventListener('click', function (event) {
      var link = event.target.closest ? event.target.closest('.help-action') : null;
      if (!link) return;
      event.preventDefault();

      var action = link.getAttribute('data-action');
      if (!action) return;

      if (!postToHost({ type: 'help.action', action: action })) {
        link.setAttribute('data-unavailable', 'true');
        link.setAttribute('title', T.actionUnavailable || '');
      }
    });
  })();

  // -------------------------------------------------------------------- support report

  (function initDiagnostics() {
    var button = document.querySelector('[data-diagnostics]');
    if (!button) return;

    button.addEventListener('click', function () {
      var lines = [
        'Raphael — support report',
        '------------------------',
        'Application:   ' + (PAGE.app || '—') + ' ' + ((host && host.appVersion) || '—'),
        'Build:         ' + ((host && host.build) || '—'),
        'Help covers:   ' + (PAGE.app || '—') + ' ' + (PAGE.appVersion || '—'),
        'Help built:    ' + (PAGE.built || '—'),
        'Help source:   ' + (PAGE.sourceCommit || '—'),
        'Topic:         ' + (PAGE.id || '—') + ' (' + (PAGE.lang || '—') + ')',
        'Role:          ' + ((host && host.role) || '—'),
        'Channel:       ' + ((host && host.channel) || '—'),
        'Generated:     ' + new Date().toISOString()
      ];

      var report = lines.join('\n');
      var done = function (ok) {
        button.textContent = ok ? (T.copyDiagnosticsDone || 'OK') : (T.copyDiagnosticsFailed || '');
        window.setTimeout(function () { button.textContent = T.copyDiagnostics || ''; }, 4000);
      };

      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(report).then(function () { done(true); }, function () { done(false); });
      } else {
        done(false);
      }
    });
  })();

  // -------------------------------------------------------------------- print

  (function initPrint() {
    var button = document.querySelector('[data-print]');
    if (button) button.addEventListener('click', function () { window.print(); });
  })();

  // -------------------------------------------------------------------- search

  (function initSearch() {
    var input = document.getElementById('rf-search-input');
    var results = document.getElementById('rf-results');
    if (!input || !results) return;

    var WEIGHTS = { title: 9, keywords: 6, heading: 3, summary: 2, text: 1 };
    var active = -1;
    var current = [];

    function normalise(value) {
      return String(value || '')
        .toLowerCase()
        .normalize('NFD')
        .replace(/[̀-ͯ]/g, '');
    }

    /* The office does not search for "notification". It searches for "campanita", "aviso" and
     * "se cayó". The synonym table is built from those words, not from the schema. */
    function expand(tokens) {
      var out = [];
      tokens.forEach(function (token) {
        out.push(token);
        var extra = INDEX.synonyms && INDEX.synonyms[token];
        if (extra) out = out.concat(extra);
      });
      return out;
    }

    function score(doc, tokens) {
      var total = 0;

      for (var index = 0; index < tokens.length; index += 1) {
        var token = tokens[index];
        var hit = 0;

        if (doc.n_title.indexOf(token) !== -1) hit += WEIGHTS.title;
        if (doc.n_keywords.indexOf(token) !== -1) hit += WEIGHTS.keywords;
        if (doc.n_headings.indexOf(token) !== -1) hit += WEIGHTS.heading;
        if (doc.n_summary.indexOf(token) !== -1) hit += WEIGHTS.summary;
        if (doc.n_text.indexOf(token) !== -1) hit += WEIGHTS.text;

        // Every token has to land somewhere. Two words means both, or the result is noise.
        if (!hit) return 0;
        total += hit;
      }

      if (doc.n_title.indexOf(tokens.join(' ')) !== -1) total += 12;
      return total;
    }

    function excerpt(doc, tokens) {
      var text = doc.text;
      var lower = doc.n_text;
      var at = -1;

      for (var index = 0; index < tokens.length && at === -1; index += 1) {
        at = lower.indexOf(tokens[index]);
      }
      if (at === -1) return doc.summary;

      var start = Math.max(0, at - 60);
      var slice = text.slice(start, start + 170).trim();
      return (start > 0 ? '… ' : '') + slice + (start + 170 < text.length ? ' …' : '');
    }

    function highlight(text, tokens) {
      var node = document.createDocumentFragment();
      var lower = normalise(text);
      var marks = [];

      tokens.forEach(function (token) {
        var from = 0;
        while (true) {
          var at = lower.indexOf(token, from);
          if (at === -1) break;
          marks.push([at, at + token.length]);
          from = at + token.length;
        }
      });

      marks.sort(function (a, b) { return a[0] - b[0]; });

      var cursor = 0;
      marks.forEach(function (range) {
        if (range[0] < cursor) return;
        node.appendChild(document.createTextNode(text.slice(cursor, range[0])));
        var mark = document.createElement('mark');
        mark.textContent = text.slice(range[0], range[1]);
        node.appendChild(mark);
        cursor = range[1];
      });
      node.appendChild(document.createTextNode(text.slice(cursor)));

      return node;
    }

    function render(query) {
      results.innerHTML = '';
      active = -1;

      var tokens = expand(normalise(query).split(/\s+/).filter(Boolean));
      if (!tokens.length) { close(); return; }

      current = INDEX.docs
        .map(function (doc) { return { doc: doc, score: score(doc, tokens) }; })
        .filter(function (entry) { return entry.score > 0; })
        .sort(function (a, b) { return b.score - a.score; })
        .slice(0, 12);

      if (!current.length) {
        var empty = el('div', 'rf-results-empty');
        empty.appendChild(el('div', null, format(T.searchNoResults, [query])));
        empty.appendChild(el('div', null, T.searchHint || ''));
        results.appendChild(empty);
      } else {
        current.forEach(function (entry, position) {
          var link = el('a', 'rf-result');
          link.href = PAGE.root + PAGE.lang + '/' + entry.doc.id + '.html';
          link.setAttribute('role', 'option');
          link.id = 'rf-result-' + position;

          var title = el('strong');
          title.appendChild(highlight(entry.doc.title, tokens));
          link.appendChild(title);

          var body = el('span');
          body.appendChild(highlight(excerpt(entry.doc, tokens), tokens));
          link.appendChild(body);

          results.appendChild(link);
        });
      }

      results.hidden = false;
      input.setAttribute('aria-expanded', 'true');
    }

    function close() {
      results.hidden = true;
      results.innerHTML = '';
      input.setAttribute('aria-expanded', 'false');
      active = -1;
    }

    function move(step) {
      var links = results.querySelectorAll('.rf-result');
      if (!links.length) return;

      if (active >= 0) links[active].classList.remove('is-active');
      active = (active + step + links.length) % links.length;
      links[active].classList.add('is-active');
      links[active].scrollIntoView({ block: 'nearest' });
      input.setAttribute('aria-activedescendant', links[active].id);
    }

    var pending = null;
    input.addEventListener('input', function () {
      window.clearTimeout(pending);
      pending = window.setTimeout(function () { render(input.value.trim()); }, 90);
    });

    input.addEventListener('keydown', function (event) {
      if (event.key === 'ArrowDown') { event.preventDefault(); move(1); }
      else if (event.key === 'ArrowUp') { event.preventDefault(); move(-1); }
      else if (event.key === 'Enter') {
        var links = results.querySelectorAll('.rf-result');
        if (active >= 0 && links[active]) { event.preventDefault(); links[active].click(); }
      } else if (event.key === 'Escape') { close(); input.blur(); }
    });

    document.addEventListener('click', function (event) {
      if (!event.target.closest || !event.target.closest('.rf-search')) close();
    });

    document.addEventListener('keydown', function (event) {
      if (event.key === '/' && document.activeElement !== input &&
          !/^(INPUT|TEXTAREA)$/.test(document.activeElement.tagName)) {
        event.preventDefault();
        input.focus();
        input.select();
      }
    });
  })();

  // -------------------------------------------------------------------- boot

  if (inApp()) {
    postToHost({ type: 'help.ready', id: PAGE.id, lang: PAGE.lang });
  }
  applyHostState();
})();
