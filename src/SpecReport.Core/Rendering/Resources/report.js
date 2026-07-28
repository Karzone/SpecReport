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
      if ((scenario.getAttribute('data-text') || '').indexOf(filters.text) === -1) return false;
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

  /* ---- lightbox ------------------------------------------------------------------- */

  var box, boxImg, boxCap, items = [], at = 0;

  function ensure() {
    if (box) return;

    boxImg = document.createElement('img');
    boxImg.alt = '';
    boxCap = document.createElement('span');

    function button(label, onClick) {
      var b = document.createElement('button');
      b.type = 'button';
      b.textContent = label;
      b.addEventListener('click', onClick);
      return b;
    }

    var cap = document.createElement('div');
    cap.className = 'cap';
    cap.appendChild(button('‹ Prev', function () { move(-1); }));
    cap.appendChild(boxCap);
    cap.appendChild(button('Next ›', function () { move(1); }));
    cap.appendChild(button('Close', function () { box.close(); }));

    box = document.createElement('dialog');
    box.className = 'lightbox';
    box.appendChild(boxImg);
    box.appendChild(cap);

    // Arrow keys move; <dialog> gives Escape-to-close for free (SPEC 8.8).
    box.addEventListener('keydown', function (e) {
      if (e.key === 'ArrowLeft') { move(-1); e.preventDefault(); }
      if (e.key === 'ArrowRight') { move(1); e.preventDefault(); }
    });

    document.body.appendChild(box);
  }

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

  document.addEventListener('click', function (e) {
    var target = e.target;
    if (!target || target.tagName !== 'IMG' || !target.closest('.shots')) return;

    ensure();
    items = Array.prototype.slice.call(target.closest('.shots').querySelectorAll('img'));
    at = items.indexOf(target);
    show();
    box.showModal();
  });

  apply();
})();
