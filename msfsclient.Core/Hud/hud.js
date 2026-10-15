// The Airsports flight display: the client's dock, start timer, training feedback, photos and announcements, from the
// client's local server (HudServer). Shared by the page the client serves (hud.html) and the MSFS toolbar panel, which
// both only provide an element with id "airsports-hud". Plain JavaScript on purpose: the simulator's browser is not a
// modern one.
(function () {
  var MARKUP = [
    '<div id="offline">Connecting to the Airsports client…</div>',
    '<main id="app" class="hidden">',
    '  <h1 id="flight">Airsports</h1>',
    '  <div class="row">',
    '    <button id="track" class="track" aria-label="Start tracking"><span class="play"></span></button>',
    '    <div id="lines" class="lines" aria-live="polite"></div>',
    '  </div>',
    '  <div id="stopConfirm" class="confirm panel" role="alertdialog" aria-label="Stop tracking?">',
    '    <strong>Stop tracking?</strong>',
    '    <div class="Muted">Your position will no longer be shared and your flight will not be scored while tracking is off.</div>',
    '    <div class="buttons"><button class="danger" id="stopYes">Stop</button><button class="link" id="stopNo">Keep tracking</button></div>',
    '  </div>',
    '',
    '  <section id="training" class="panel hidden" aria-live="assertive"></section>',
    '',
    '  <section id="timer" class="timer panel hidden">',
    '    <div id="timerLabel" class="Muted"></div>',
    '    <div id="timerBig" class="big">0:00</div>',
    '    <div id="timerNote" class="Muted"></div>',
    '    <button id="timerStart" class="start">START</button>',
    '    <button id="timerReset" class="link hidden">Reset</button>',
    '    <div id="resetConfirm" class="confirm">',
    '      <div class="Muted">The running timer is cleared and you have to press Start again. Tracking is not affected.</div>',
    '      <div class="buttons"><button class="danger" id="resetYes">Reset</button><button class="link" id="resetNo">Keep timer</button></div>',
    '    </div>',
    '  </section>',
    '',
    '  <section id="photos" class="panel hidden">',
    '    <div class="row" style="justify-content: space-between"><strong>Photos</strong><span id="photoCounter" class="Muted"></span></div>',
    '    <div id="photoBlocker" class="note"></div>',
    '    <div id="photoList"></div>',
    '  </section>',
    '',
    '  <section id="announcements"></section>',
    '  <div id="version" class="Small" style="margin-top: 14px"></div>',
    '</main>',
    '<div id="toast" role="status"></div>',
    '',
  ].join('\n');

  var root = document.getElementById('airsports-hud');
  root.innerHTML = MARKUP;
  var host = location.protocol === 'http:' ? location.host : 'localhost:47911';
  var socket = null, state = null, clockOffset = 0, retry = 1000;
  function $(id) { return document.getElementById(id); }
  function text(el, value) { el.textContent = value == null ? '' : value; }
  function show(el, on) { el.classList.toggle('hidden', !on); }

  function connect() {
    socket = new WebSocket('ws://' + host + '/ws');
    socket.onopen = function () { retry = 1000; };
    socket.onmessage = function (e) {
      var msg = JSON.parse(e.data);
      if (msg.type === 'state') { state = msg.state; if (state.timer) clockOffset = state.timer.clockMillis - Date.now(); render(); }
      else if (msg.type === 'result' && msg.message) toast(msg.message, !msg.ok);
    };
    socket.onclose = function () {
      show($('app'), false); show($('offline'), true);
      text($('offline'), 'Not connected to the Airsports client. Start it on this PC; this page reconnects by itself.');
      setTimeout(connect, retry); retry = Math.min(retry * 2, 10000);
    };
  }

  function send(command) { if (socket && socket.readyState === 1) socket.send(JSON.stringify(command)); }

  var toastTimer = null;
  function toast(message, error) {
    var t = $('toast'); text(t, message); t.style.display = 'block'; t.style.background = error ? 'var(--red)' : 'var(--content)';
    clearTimeout(toastTimer); toastTimer = setTimeout(function () { t.style.display = 'none'; }, 4000);
  }

  function formatElapsed(ms) {
    var total = ms >= 0 ? Math.floor(ms / 1000) : -Math.floor((-ms + 999) / 1000);
    var sign = total < 0 ? '-' : '', abs = Math.abs(total);
    var h = Math.floor(abs / 3600), m = Math.floor((abs % 3600) / 60), s = abs % 60;
    function two(n) { return (n < 10 ? '0' : '') + n; }
    return h > 0 ? sign + h + ':' + two(m) + ':' + two(s) : sign + m + ':' + two(s);
  }
  function clockText(ms, offsetMinutes) {
    var d = new Date(ms + offsetMinutes * 60000);
    function two(n) { return (n < 10 ? '0' : '') + n; }
    return two(d.getUTCHours()) + ':' + two(d.getUTCMinutes()) + ':' + two(d.getUTCSeconds());
  }

  function render() {
    show($('offline'), false); show($('app'), true);
    text($('flight'), state.flight || 'Airsports');
    text($('version'), 'Airsports ' + state.version);

    var t = state.tracking, button = $('track');
    button.className = 'track' + (t.running ? ' on' : '');
    button.innerHTML = t.running ? '<span class="stop"></span>' : '<span class="play"></span>';
    button.setAttribute('aria-label', t.running ? 'Stop tracking' : 'Start tracking');
    button.disabled = !state.signedIn || (!t.running && !t.canStart);
    if (!t.running) $('stopConfirm').classList.remove('open');
    var lines = $('lines'); lines.innerHTML = '';
    t.lines.forEach(function (l) { var d = document.createElement('div'); d.className = l.tone; text(d, l.text); lines.appendChild(d); });

    var tr = state.training, training = $('training');
    show(training, !!tr && t.running);
    if (tr) training.innerHTML = tr.gate
      ? '<div class="gate"><span class="name"></span><span class="text ' + tr.timing + '"></span></div>'
      : '<strong>Training: waiting for your first gate</strong>';
    if (tr && tr.gate) { text(training.querySelector('.name'), tr.gate); text(training.querySelector('.text'), tr.text); }

    show($('timer'), !!state.timer);
    var p = state.photos;
    show($('photos'), !!p);
    if (p) {
      text($('photoCounter'), p.counter || '');
      text($('photoBlocker'), p.blocker || 'Position OK: marks are stamped with your position and time.');
      var list = $('photoList'); list.innerHTML = '';
      p.items.forEach(function (item) {
        var row = document.createElement('div'); row.className = 'photo';
        row.innerHTML = '<div class="name"><div></div><div class="state"></div></div><button class="mark"></button>';
        text(row.querySelector('.name div'), item.name); text(row.querySelector('.state'), item.state);
        var mark = row.querySelector('button'); text(mark, item.marked ? 'Mark again' : 'Mark'); mark.disabled = !item.canMark;
        mark.setAttribute('data-photo', item.id);
        list.appendChild(row);
      });
    }

    var ann = $('announcements'); ann.innerHTML = '';
    state.announcements.forEach(function (a) {
      var box = document.createElement('div'); box.className = 'note announcement';
      box.innerHTML = '<div class="source"></div><div class="message"></div><button class="link">OK</button>';
      text(box.querySelector('.source'), a.source); text(box.querySelector('.message'), a.message);
      box.querySelector('button').setAttribute('data-announcement', a.id);
      ann.appendChild(box);
    });
    tick();
  }

  // The timer counts on its own between updates, on the PC clock the client stamps positions with.
  function tick() {
    if (!state || !state.timer) return;
    var tm = state.timer, now = Date.now() + clockOffset;
    var running = tm.originMillis != null;
    show($('timerStart'), !running); show($('timerReset'), running);
    if (!running) {
      text($('timerLabel'), 'PC time (' + tm.zone + ')');
      text($('timerBig'), clockText(now, tm.utcOffsetMinutes));
      $('timerBig').classList.remove('down');
      text($('timerNote'), 'Press Start when you begin your start procedure.' + (tm.bufferMinutes > 0 ? ' Countdown buffer ' + tm.bufferMinutes + ' min.' : ''));
    } else {
      var elapsed = now - tm.originMillis;
      text($('timerLabel'), 'Start minute ' + tm.startMinute + ' (' + tm.zone + ')');
      text($('timerBig'), formatElapsed(elapsed));
      $('timerBig').classList.toggle('down', elapsed < 0);
      text($('timerNote'), elapsed < 0 ? 'Counting down to your start minute' : 'Time since your start minute');
    }
  }
  setInterval(tick, 100);

  // One click handler for the whole display: the simulator's panel frame may re-create its content, which would drop
  // handlers attached to single buttons.
  var actions = {
    track: function () { if (state.tracking.running) $('stopConfirm').classList.add('open'); else send({ action: 'startTracking' }); },
    stopYes: function () { $('stopConfirm').classList.remove('open'); send({ action: 'stopTracking', confirm: true }); },
    stopNo: function () { $('stopConfirm').classList.remove('open'); },
    timerStart: function () { send({ action: 'startTimer' }); },
    timerReset: function () { $('resetConfirm').classList.add('open'); },
    resetYes: function () { $('resetConfirm').classList.remove('open'); send({ action: 'resetTimer', confirm: true }); },
    resetNo: function () { $('resetConfirm').classList.remove('open'); }
  };
  document.addEventListener('click', function (e) {
    for (var el = e.target; el && el !== document; el = el.parentNode) {
      if (!el.getAttribute) continue;
      if (el.disabled) return;
      if (el.getAttribute('data-photo')) { send({ action: 'mark', photoId: +el.getAttribute('data-photo') }); return; }
      if (el.getAttribute('data-announcement')) { send({ action: 'dismissAnnouncement', id: +el.getAttribute('data-announcement') }); return; }
      if (el.id && actions[el.id] && state) { actions[el.id](); return; }
    }
  });
  connect();
})();
