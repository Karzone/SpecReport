/* SpecReport page script.
   The report is already rendered as HTML - this only adds interactivity, so the page stays
   readable with scripting disabled. No network calls of any kind (SPEC 8.5). */
(function () {
  'use strict';

  var scenarios = Array.prototype.slice.call(document.querySelectorAll('.scenario[data-status]'));
  var sections = Array.prototype.slice.call(document.querySelectorAll('.feature'));
  var rules = Array.prototype.slice.call(document.querySelectorAll('.rule'));
  var empty = document.getElementById('no-matches');
  var search = document.getElementById('q');
  var tagSelect = document.getElementById('tag');
  var chips = Array.prototype.slice.call(document.querySelectorAll('.chip[data-status]'));

  var filters = { status: 'all', tag: '', text: '' };

  function visible(scenario) {
    if (filters.status !== 'all' && scenario.getAttribute('data-status') !== filters.status) return false;

    if (filters.tag) {
      var tags = (scenario.getAttribute('data-tags') || '').split(' ');
      if (tags.indexOf(filters.tag) === -1) return false;
    }

    if (filters.text) {
      // Built lazily from the DOM on first search, then cached on the element. Shipping this
      // as an attribute duplicated every step's text into the document - megabytes, on exactly
      // the large runs where page weight matters most.
      var hay = scenario._hay;
      if (hay === undefined) {
        hay = (scenario.textContent || '').toLowerCase();
        scenario._hay = hay;
      }
      if (hay.indexOf(filters.text) === -1) return false;
    }

    return true;
  }

  function apply() {
    var shown = 0;

    scenarios.forEach(function (scenario) {
      var show = visible(scenario);
      scenario.hidden = !show;
      if (show) shown++;
    });

    // Hide containers that ended up with nothing in them, so the page doesn't show a Rule
    // heading above an empty space.
    rules.concat(sections).forEach(function (container) {
      var any = container.querySelector('.scenario[data-status]:not([hidden])');
      container.hidden = !any;
    });

    if (empty) empty.hidden = shown !== 0;
  }

  if (search) {
    search.addEventListener('input', function () {
      filters.text = search.value.toLowerCase();
      apply();
    });
  }

  if (tagSelect) {
    tagSelect.addEventListener('change', function () {
      filters.tag = tagSelect.value;
      apply();
    });
  }

  chips.forEach(function (chip) {
    chip.addEventListener('click', function () {
      filters.status = chip.getAttribute('data-status');
      chips.forEach(function (c) {
        c.setAttribute('aria-pressed', c === chip ? 'true' : 'false');
      });
      apply();
    });
  });

  /* ---- expand / collapse all ------------------------------------------------------- */

  var toggleAll = document.getElementById('toggle-all');

  if (toggleAll) {
    toggleAll.addEventListener('click', function () {
      // With two levels of disclosure, opening everything by hand is tedious - but so is
      // closing it again, so this is a toggle rather than two buttons.
      var expanding = toggleAll.getAttribute('aria-pressed') !== 'true';

      Array.prototype.forEach.call(
        document.querySelectorAll('details.scenario, details.step-block'),
        function (d) { d.open = expanding; });

      toggleAll.setAttribute('aria-pressed', expanding);
      toggleAll.textContent = expanding ? 'collapse all' : 'expand all';
    });
  }

  /* ---- lightbox -------------------------------------------------------------------- */

  // The dialog is in the markup, not built here: a native <dialog> opened with showModal()
  // traps focus and closes on Escape with no script at all (SPEC 8.8).
  var box = document.getElementById('lightbox');
  var boxImg = document.getElementById('lightbox-img');
  var boxCap = document.getElementById('lightbox-cap');
  var items = [], at = 0, opener = null;

  function move(delta) {
    if (!items.length) return;
    at = (at + delta + items.length) % items.length;
    show();
  }

  function show() {
    var img = items[at];
    boxImg.src = img.src;
    boxImg.alt = img.alt || 'Screenshot';
    boxCap.textContent = (img.alt || 'screenshot') + '  (' + (at + 1) + '/' + items.length + ')';
  }

  if (box) {
    document.getElementById('lightbox-prev').addEventListener('click', function () { move(-1); });
    document.getElementById('lightbox-next').addEventListener('click', function () { move(1); });
    document.getElementById('lightbox-close').addEventListener('click', function () { box.close(); });

    box.addEventListener('keydown', function (e) {
      if (e.key === 'ArrowLeft') { move(-1); e.preventDefault(); }
      if (e.key === 'ArrowRight') { move(1); e.preventDefault(); }
    });

    // Return focus where it came from, or a keyboard user is dumped at the top of the page.
    box.addEventListener('close', function () {
      if (opener && document.contains(opener)) opener.focus();
      opener = null;
    });

    document.addEventListener('click', function (e) {
      var button = e.target.closest ? e.target.closest('button.shot') : null;
      if (!button) return;

      var strip = button.closest('.shots');
      items = Array.prototype.slice.call(strip.querySelectorAll('img'));
      at = items.indexOf(button.querySelector('img'));
      opener = button;
      show();
      box.showModal();
    });
  }

  apply();
})();
