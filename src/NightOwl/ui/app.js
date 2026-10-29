'use strict';
// nightOwl settings UI. The core (C#) owns the config; this page edits a copy, sends the whole
// config back on every change (live preview, no Apply button) and re-renders when the core pushes state.

const host = window.chrome && window.chrome.webview;
const $ = (sel, root = document) => root.querySelector(sel);
const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

let state = null;          // last state from the core: { config, monitors, status }
let lastConfigJson = '';
let page = sessionStorageGet('page') || 'home';
let selectedMonitor = null;
const thumbs = {};         // id → true (has thumbnail) | false (none) | 'pending'

function sessionStorageGet(k) { try { return sessionStorage.getItem(k); } catch { return null; } }
function sessionStorageSet(k, v) { try { sessionStorage.setItem(k, v); } catch { } }

function send(msg, files) {
  if (!host) return mockHost(msg);
  if (files) host.postMessageWithAdditionalObjects(msg, files);
  else host.postMessage(msg);
}

const cfg = () => state.config;
const prof = () => state.config.profiles[state.config.activeProfile];

let sendTimer = 0;
/** Apply a local edit, send it to the core, and optionally re-render the page. */
function edit(fn, rerender = true) {
  fn(state.config);
  lastConfigJson = JSON.stringify(state.config);
  clearTimeout(sendTimer);
  sendTimer = setTimeout(() => send({ type: 'setConfig', config: state.config }), 120);
  if (rerender) renderPage();
}

function toast(text) {
  const t = $('#toast');
  t.textContent = text;
  t.hidden = false;
  clearTimeout(toast.timer);
  toast.timer = setTimeout(() => (t.hidden = true), 2600);
}

// ---------- messages from the core ----------

function onMessage(m) {
  if (m.type === 'state') {
    const json = JSON.stringify(m.config);
    const configChanged = json !== lastConfigJson;
    state = m;
    lastConfigJson = json;
    if (configChanged || !$('#main').firstChild) renderPage(); else renderStatus();
  } else if (m.type === 'thumb') {
    thumbs[m.id] = m.ok;
    document.querySelectorAll(`[data-thumb="${m.id}"]`).forEach(el => applyThumb(el, m.id));
    renderMonitorsOnly();
  } else if (m.type === 'toast') {
    toast(m.message);
  } else if (m.type === 'error') {
    toast(m.message);
  }
}
if (host) host.addEventListener('message', e => onMessage(e.data));

// ---------- navigation ----------

$('#nav').addEventListener('click', e => {
  const b = e.target.closest('button[data-page]');
  if (!b) return;
  page = b.dataset.page;
  sessionStorageSet('page', page);
  renderPage();
  $('#main').scrollTop = 0;
});

// Dropping files anywhere must never navigate the WebView.
['dragover', 'drop'].forEach(t => document.addEventListener(t, e => e.preventDefault()));

// ---------- rendering helpers ----------

const toggle = (key, checked, opts = {}) =>
  `<label class="switch"><span class="lbl">${checked ? 'On' : 'Off'}</span><input type="checkbox" data-key="${key}" ${checked ? 'checked' : ''} ${opts.disabled ? 'disabled' : ''}></label>`;

const row = (title, desc, control, icon) => `
  <div class="row">${icon ? `<i class="lead">${icon}</i>` : ''}
    <div class="grow"><div class="title">${title}</div>${desc ? `<div class="desc">${desc}</div>` : ''}</div>
    ${control}
  </div>`;

const seg = (key, value, options) =>
  `<div class="seg" data-seg="${key}">${options.map(([v, l]) => `<button data-v="${v}" class="${v === value ? 'on' : ''}">${l}</button>`).join('')}</div>`;

function featureHead(title, desc, key, on, statusText) {
  return `<div class="card"><div class="feature-head">
      <div class="grow"><div class="title">${title}</div><div class="desc muted">${desc}</div>
      ${statusText ? `<div class="small" data-status-text="${key}" style="margin-top:4px"><span class="status-dot ${on ? 'on' : ''}"></span>${esc(statusText)}</div>` : ''}</div>
      ${toggle(key, on)}
    </div></div>`;
}

function soonCard(icon, title, desc, phase) {
  return `<div class="card soon">${row(`${title}<span class="badge">Phase ${phase}</span>`, desc, '', icon)}</div>`;
}

function warningItems() {
  return (state.status.warnings || []).map(t => `<div class="warn"><i>&#xE7BA;</i><div>${esc(t)}</div></div>`).join('');
}
function warnings() { return `<div class="warnings">${warningItems()}</div>`; }

function itemById(id) { return cfg().library.find(i => i.id === id); }
function typeIcon(t) { return t === 'url' ? '&#xE774;' : t === 'html' ? '&#xE943;' : t === 'image' ? '&#xE91B;' : '&#xE768;'; }
function typeLabel(t) { return t === 'url' ? 'Web' : t === 'html' ? 'HTML' : t === 'image' ? 'Image' : 'Video'; }

function applyThumb(el, id) {
  if (thumbs[id] === true) {
    el.style.backgroundImage = `url("https://thumbs.nightowl/${id}.png")`;
    const ic = el.querySelector('i'); if (ic) ic.style.display = 'none';
  }
}

function requestThumbs() {
  for (const it of cfg().library) {
    if (thumbs[it.id] === undefined && it.type !== 'url') {
      thumbs[it.id] = 'pending';
      send({ type: 'thumb', id: it.id });
    }
  }
}

// ---------- pages ----------

const pages = {
  home() {
    const s = state.status, c = cfg(), p = prof();
    return `
      <div class="hero"><img src="owl.png" alt="">
        <div><h1>nightOwl</h1><p class="muted">Profile <b>${esc(c.activeProfile)}</b> · Windows ${esc(s.displayVersion)} (build ${s.build})</p></div>
      </div>
      ${warnings()}
      ${c.pausedAll ? `<div class="warn"><i>&#xE769;</i><div>All effects are paused. Windows looks stock until you resume.</div></div>` : ''}
      <div class="grid2">
        <div class="card tile">
          <div class="tile-head"><i>&#xE8A9;</i><span class="title">Auto-hide on full screen</span>${toggle('autoHide.enabled', p.autoHide.enabled)}</div>
          <div class="small" data-status="autoHide"><span class="status-dot ${s.autoHideOn ? 'on' : ''}"></span>${esc(s.autoHide)}</div>
        </div>
        <div class="card tile">
          <div class="tile-head"><i>&#xE91B;</i><span class="title">Live wallpaper</span>${toggle('wallpaper.enabled', p.wallpaper.enabled)}</div>
          <div class="small" data-status="wallpaper"><span class="status-dot ${s.wallpaperOn ? 'on' : ''}"></span>${esc(s.wallpaper)}</div>
        </div>
      </div>
      <h2>Quick actions</h2>
      <div class="card"><div class="btns">
        <button class="btn" data-act="pauseAll"><i>${c.pausedAll ? '&#xE768;' : '&#xE769;'}</i>${c.pausedAll ? 'Resume all effects' : 'Pause all effects'}</button>
        <button class="btn" data-act="next" ${c.library.length ? '' : 'disabled'}><i>&#xE893;</i>Next wallpaper</button>
        <button class="btn danger" data-act="reset"><i>&#xE72C;</i>Reset to stock</button>
      </div>
      <p class="small" style="margin-top:10px">Reset works even when this window can't open: press <b>Ctrl+Alt+Shift+R</b>.</p></div>
      <h2>Taskbar and Explorer styling</h2>
      <div class="card tile">
        <div class="tile-head"><i>&#xE790;</i><span class="title">Windhawk mods</span>
          <button class="btn" data-shell="openWindhawk" ${s.windhawkInstalled ? '' : 'disabled'}><i>&#xE8A7;</i>Open Windhawk</button></div>
        <div class="small" data-status="shell"><span class="status-dot ${s.shellOn ? 'on' : ''}"></span>${esc(s.shell)}</div>
        ${shellGate(true)}
      </div>
      <h2>Startup</h2>
      <div class="card">${row('Start with Windows', 'Start nightOwl in the tray when you sign in.', toggle('startWithWindows', c.startWithWindows), '&#xE7E8;')}</div>`;
  },

  taskbar() {
    const a = prof().autoHide, s = state.status;
    return `
      <div class="page-head"><div><h1>Taskbar</h1><p class="muted">Profile ${esc(cfg().activeProfile)}</p></div></div>
      ${featureHead('Auto-hide when an app is full screen',
        'Hides a monitor\'s taskbar while the app in front fills that monitor, including borderless games and full-screen video.',
        'autoHide.enabled', a.enabled, s.autoHide)}
      <div class="card">
        ${row('Also hide when a window is maximised', 'Switches on Windows’ own auto-hide while a maximised window is in front, so the window gets the full height. The taskbar slides up at the bottom edge and comes back on the desktop.', toggle('autoHide.whenMaximized', a.whenMaximized), '&#xE740;')}
        ${row('Reveal at the screen edge', 'While hidden, move the mouse to the bottom edge to bring the taskbar back.', toggle('autoHide.edgeReveal', a.edgeReveal), '&#xE7C2;')}
        ${row('Always hide for these apps', 'Hide the taskbar whenever one of these is in front, even when not full screen.', '', '&#xE8F4;')}
        ${chipList('alwaysHide', a.alwaysHide)}
        ${row('Never hide for these apps', 'Keep the taskbar visible for these, even in full screen.', '', '&#xE8F4;')}
        ${chipList('neverHide', a.neverHide)}
      </div>
      ${shellGate()}
      ${shellPages.taskbarShell()}`;
  },

  notifications: () => shellPages.notifications(),
  startMenu: () => shellPages.startMenu(),
  explorer: () => shellPages.explorer(),
  styler: () => shellPages.styler(),

  wallpaper() {
    const w = prof().wallpaper, s = state.status, c = cfg();
    if (!selectedMonitor || !state.monitors.some(m => m.id === selectedMonitor))
      selectedMonitor = (state.monitors.find(m => m.primary) || state.monitors[0] || {}).id;
    const pl = w.playlist;
    const items = c.library;
    const opt = (id) => items.map(i => `<option value="${i.id}" ${i.id === id ? 'selected' : ''}>${esc(i.name)}</option>`).join('');
    return `
      <div class="page-head"><div><h1>Wallpaper</h1><p class="muted">Profile ${esc(c.activeProfile)}</p></div>
        <button class="btn" data-act="next" ${items.length ? '' : 'disabled'}><i>&#xE893;</i>Next wallpaper</button></div>
      ${featureHead('Live wallpaper', 'Video, animated images and web pages behind your desktop icons.', 'wallpaper.enabled', w.enabled, s.wallpaper)}
      ${warnings()}

      <h2>Displays</h2>
      <div class="card">
        ${row('Layout', '', seg('wallpaper.layout', w.layout, [['perMonitor', 'Each display'], ['span', 'Span all displays']]), '&#xE7F4;')}
        <div class="monitors" id="monitors"></div>
        <p class="small">${w.layout === 'span' ? 'Click a wallpaper below to span it across every display.' : 'Select a display, then click a wallpaper below to put it there.'}</p>
      </div>

      <h2>Library</h2>
      <div class="drop" id="drop"><i>&#xE896;</i> Drop videos, GIFs, images or .html files here
        <div class="btns" style="justify-content:center;margin-top:10px">
          <button class="btn" data-act="addFiles"><i>&#xE710;</i>Add files</button>
          <input type="url" id="urlInput" placeholder="https://… web page" style="width:240px">
          <button class="btn" data-act="addUrl"><i>&#xE774;</i>Add web page</button>
        </div>
      </div>
      <div class="lib" id="lib">${items.map(libItem).join('') || '<p class="muted">No wallpapers yet.</p>'}</div>

      <h2>Playback</h2>
      <div class="card">
        ${row('Fit', '', `<select data-key="wallpaper.fit">${[['fill', 'Fill'], ['fit', 'Fit'], ['stretch', 'Stretch'], ['centre', 'Centre']]
          .map(([v, l]) => `<option value="${v}" ${w.fit === v ? 'selected' : ''}>${l}</option>`).join('')}</select>`, '&#xE740;')}
        ${row('Sound', w.muted ? 'Muted' : `Volume ${w.volume}%`,
          `<input type="range" min="0" max="100" value="${w.volume}" data-key="wallpaper.volume" ${w.muted ? 'disabled' : ''}>
           <label class="switch"><span class="lbl">Mute</span><input type="checkbox" data-key="wallpaper.muted" ${w.muted ? 'checked' : ''}></label>`, '&#xE767;')}
        ${row('Interactive web wallpapers', 'Pass mouse movement and clicks on the empty desktop to HTML and web wallpapers.', toggle('wallpaper.interactive', w.interactive), '&#xE962;')}
      </div>

      <h2>Pause automatically</h2>
      <div class="card">
        ${row('When an app is full screen', '', toggle('wallpaper.pauseWhenFullscreen', w.pauseWhenFullscreen), '&#xE740;')}
        ${row('When a window is maximised', '', toggle('wallpaper.pauseWhenMaximized', w.pauseWhenMaximized), '&#xE922;')}
        ${row('On battery', s.hasBattery ? (s.onBattery ? 'Running on battery now' : 'Plugged in now') : 'No battery detected', toggle('wallpaper.pauseOnBattery', w.pauseOnBattery), '&#xE83F;')}
        ${row('When battery saver is on', s.batterySaver ? 'Battery saver is on now' : '', toggle('wallpaper.pauseOnBatterySaver', w.pauseOnBatterySaver), '&#xEBC0;')}
      </div>

      <h2>Playlist</h2>
      <div class="card">
        ${row('Rotate wallpapers', 'Uses starred wallpapers, or the whole library when none are starred.', toggle('wallpaper.playlist.enabled', pl.enabled), '&#xE8B1;')}
        ${pl.enabled ? `
          ${row('Change', '', seg('wallpaper.playlist.mode', pl.mode, [['interval', 'On a timer'], ['timeOfDay', 'By time of day']]), '&#xE823;')}
          ${pl.mode === 'interval' ? `
            ${row('Every', 'minutes', `<input type="number" min="1" max="1440" value="${pl.intervalMinutes}" data-key="wallpaper.playlist.intervalMinutes" style="width:90px">`, '&#xE916;')}
            ${row('Shuffle', '', toggle('wallpaper.playlist.shuffle', pl.shuffle), '&#xE8B1;')}`
          : `<div class="row"><i class="lead">&#xE787;</i><div class="grow">
              <div class="title">Schedule</div><div class="desc">From each time until the next, this wallpaper shows on every display.</div>
              ${pl.schedule.map((sl, i) => `<div class="slot"><input type="time" value="${esc(sl.time)}" data-slot-time="${i}">
                 <select data-slot-item="${i}"><option value="">Choose…</option>${opt(sl.itemId)}</select>
                 <button class="btn icon" data-slot-del="${i}" title="Remove"><i>&#xE711;</i></button></div>`).join('')}
              <button class="btn" data-act="addSlot" style="margin-top:8px" ${items.length ? '' : 'disabled'}><i>&#xE710;</i>Add time</button>
            </div></div>`}` : ''}
      </div>`;
  },

  profiles() {
    const c = cfg();
    return `
      <div class="page-head"><div><h1>Profiles</h1><p class="muted">A profile is a full set of settings. Switch from here or from the tray menu.</p></div></div>
      <div class="card">
        ${Object.keys(c.profiles).map(n => `
          <div class="row profile-row"><i class="lead">&#xE716;</i>
            <div class="grow"><span class="name">${esc(n)}</span> ${n === c.activeProfile ? '<span class="badge accent">Active</span>' : ''}</div>
            <div class="btns">
              ${n === c.activeProfile ? '' : `<button class="btn primary" data-prof="switch" data-name="${esc(n)}">Switch</button>`}
              <button class="btn" data-prof="duplicate" data-name="${esc(n)}">Duplicate</button>
              <button class="btn" data-prof="rename" data-name="${esc(n)}">Rename</button>
              <button class="btn danger" data-prof="delete" data-name="${esc(n)}" ${Object.keys(c.profiles).length < 2 ? 'disabled' : ''}>Delete</button>
            </div>
          </div>`).join('')}
      </div>
      <div class="card" id="profileForm" hidden>
        <div class="row"><div class="grow"><div class="title" id="profileFormTitle"></div></div>
          <input type="text" id="profileName" maxlength="40" style="width:220px">
          <button class="btn primary" id="profileOk">Save</button><button class="btn" id="profileCancel">Cancel</button></div>
      </div>`;
  },

  about() {
    const s = state.status, c = cfg();
    return `
      <div class="hero"><img src="owl.png" alt=""><div><h1>nightOwl</h1><p class="muted">Version ${esc(s.version)} · Phase 0 and 1</p></div></div>
      <div class="card">
        ${row('Windows', `${esc(s.displayVersion)} · build ${s.build}`, '', '&#xE7F8;')}
        ${row('Reset hotkey', 'Turns every effect off and restores stock Windows.', '<span class="badge">Ctrl+Alt+Shift+R</span>', '&#xE765;')}
        ${row('Start with Windows', s.startupSet ? 'Registered for this user' : 'Not registered', toggle('startWithWindows', c.startWithWindows), '&#xE7E8;')}
        ${row('Logs', 'The last 7 days, for diagnosing problems after Windows updates.', '<button class="btn" data-act="openLogs"><i>&#xE8B7;</i>Open folder</button>', '&#xE9F9;')}
        ${row('Settings file', esc(s.configPath), '<button class="btn" data-act="openConfig"><i>&#xE8B7;</i>Open folder</button>', '&#xE713;')}
      </div>`;
  },
};

function chipList(list, values) {
  return `<div class="chips" style="margin:6px 0 4px 40px">
    ${values.map((v, i) => `<span class="chip">${esc(v)}<button data-chip-del="${list}" data-i="${i}" title="Remove"><i>&#xE711;</i></button></span>`).join('')}
    <input type="text" placeholder="app.exe, then Enter" data-chip-add="${list}" style="width:170px">
  </div>`;
}

function libItem(it) {
  const w = prof().wallpaper;
  const onMon = w.layout === 'span'
    ? (w.spanItem === it.id ? ['All'] : [])
    : state.monitors.filter(m => w.monitors[m.id] === it.id).map(m => String(m.index));
  const isCurrent = w.layout === 'span' ? w.spanItem === it.id : w.monitors[selectedMonitor] === it.id;
  const starred = w.playlist.items.includes(it.id);
  return `<div class="item ${isCurrent ? 'current' : ''}" data-item="${it.id}" title="${esc(it.path)}">
      <div class="thumb" data-thumb="${it.id}"><i>${typeIcon(it.type)}</i></div>
      ${starred ? '<span class="star"><i>&#xE735;</i></span>' : ''}
      ${onMon.length ? `<div class="on-mon">${onMon.map(n => `<span>${n}</span>`).join('')}</div>` : ''}
      <div class="meta"><span class="name">${esc(it.name)}</span>${it.codec === 'hevc'
        ? '<span class="badge warnb" title="HEVC (H.265) needs the HEVC Video Extensions from the Microsoft Store to play.">HEVC</span>'
        : `<span class="badge">${typeLabel(it.type)}</span>`}</div>
      <div class="tools">
        <button data-star="${it.id}" title="${starred ? 'Remove from playlist' : 'Add to playlist'}"><i>${starred ? '&#xE735;' : '&#xE734;'}</i></button>
        <button data-remove="${it.id}" title="Remove from library"><i>&#xE74D;</i></button>
      </div>
    </div>`;
}

function renderMonitorsOnly() {
  const box = $('#monitors');
  if (!box || !state) return;
  const ms = state.monitors;
  if (!ms.length) { box.innerHTML = ''; return; }
  const w = prof().wallpaper;
  const minX = Math.min(...ms.map(m => m.x)), minY = Math.min(...ms.map(m => m.y));
  const maxX = Math.max(...ms.map(m => m.x + m.w)), maxY = Math.max(...ms.map(m => m.y + m.h));
  const bw = box.clientWidth || 600, bh = box.clientHeight || 150;
  const scale = Math.min(bw / (maxX - minX), bh / (maxY - minY)) * 0.96;
  const offX = (bw - (maxX - minX) * scale) / 2;
  box.innerHTML = ms.map(m => {
    const id = w.layout === 'span' ? w.spanItem : w.monitors[m.id];
    const sel = w.layout === 'span' || m.id === selectedMonitor;
    const bg = id && thumbs[id] === true ? `background-image:url('https://thumbs.nightowl/${id}.png')` : '';
    return `<div class="mon ${sel ? 'sel' : ''}" data-mon="${esc(m.id)}" title="${esc(m.id)} · ${m.w}×${m.h}"
      style="left:${offX + (m.x - minX) * scale + 2}px;top:${(m.y - minY) * scale + 2}px;width:${m.w * scale - 4}px;height:${m.h * scale - 4}px;${bg}">
      <span class="num">${m.index}</span></div>`;
  }).join('');
}

function renderPage() {
  if (!state) return;
  document.querySelectorAll('#nav button').forEach(b => b.classList.toggle('active', b.dataset.page === page));
  const focused = document.activeElement;
  const focusKey = focused && (focused.dataset.chipAdd || focused.id);
  const main = $('#main');
  main.innerHTML = `<div class="page">${(pages[page] || pages.home)()}</div>`;
  if (page === 'wallpaper') {
    requestThumbs();
    document.querySelectorAll('[data-thumb]').forEach(el => applyThumb(el, el.dataset.thumb));
    renderMonitorsOnly();
    wireDrop();
  }
  if (focusKey) {
    const el = main.querySelector(`[data-chip-add="${focusKey}"]`) || document.getElementById(focusKey);
    if (el) el.focus();
  }
}

/** Status-only update (no config change): refresh the live status lines without rebuilding inputs. */
function renderStatus() {
  const s = state.status;
  const set = (key, text, on) => document.querySelectorAll(`[data-status="${key}"], [data-status-text="${key}.enabled"]`)
    .forEach(el => (el.innerHTML = `<span class="status-dot ${on ? 'on' : ''}"></span>${esc(text)}`));
  set('autoHide', s.autoHide, s.autoHideOn);
  set('wallpaper', s.wallpaper, s.wallpaperOn);
  set('shell', s.shell, s.shellOn);
  const w = warningItems();
  document.querySelectorAll('.warnings').forEach(el => { if (el.innerHTML !== w) el.innerHTML = w; });
}

// ---------- events ----------

function setPath(obj, path, value) {
  const keys = path.split('.');
  let o = obj;
  for (const k of keys.slice(0, -1)) o = o[k];
  o[keys[keys.length - 1]] = value;
}

/** data-key paths are relative to the active profile, except top-level config keys. */
function setKey(key, value) {
  const topLevel = ['startWithWindows', 'pausedAll'];
  const live = key === 'wallpaper.volume' || !!document.querySelector(`input[type=range][data-key="${key}"][data-live]`);
  edit(c => setPath(topLevel.includes(key.split('.')[0]) ? c : c.profiles[c.activeProfile], key, value), !live);
}

const main = $('#main');

main.addEventListener('change', e => {
  const t = e.target;
  if (t.dataset.key) {
    if (t.type === 'checkbox') setKey(t.dataset.key, t.checked);
    else if (t.type === 'range' || t.type === 'number') setKey(t.dataset.key, Math.max(+t.min || 0, Math.min(+t.max || 1e9, parseInt(t.value, 10) || 0)));
    else if (t.dataset.num !== undefined) setKey(t.dataset.key, parseInt(t.value, 10) || 0);
    else setKey(t.dataset.key, t.value);
  } else if (t.dataset.list === 'constants') {
    edit(c => (c.profiles[c.activeProfile].styler.constants = lines(t.value)), false);
  } else if (t.dataset.ruleTarget !== undefined) {
    edit(c => (c.profiles[c.activeProfile].styler.rules[+t.dataset.ruleTarget].target = t.value.trim()), false);
  } else if (t.dataset.ruleStyles !== undefined) {
    edit(c => (c.profiles[c.activeProfile].styler.rules[+t.dataset.ruleStyles].styles = lines(t.value)), false);
  } else if (t.dataset.slotTime !== undefined) {
    edit(c => (c.profiles[c.activeProfile].wallpaper.playlist.schedule[+t.dataset.slotTime].time = t.value), false);
  } else if (t.dataset.slotItem !== undefined) {
    edit(c => (c.profiles[c.activeProfile].wallpaper.playlist.schedule[+t.dataset.slotItem].itemId = t.value), false);
  }
});

main.addEventListener('input', e => {
  const t = e.target;
  if (t.type === 'range' && t.dataset.key === 'wallpaper.volume') {
    const desc = t.closest('.row').querySelector('.desc');
    if (desc) desc.textContent = `Volume ${t.value}%`;
    setKey('wallpaper.volume', parseInt(t.value, 10));
  } else if (t.type === 'range' && t.dataset.live !== undefined) {
    const desc = t.closest('.row').querySelector('.desc');
    if (desc) desc.textContent = t.dataset.scale !== undefined ? `${(t.value / 100).toFixed(2)}×` : `${t.value}${t.dataset.unit || ''}`;
    clearTimeout(t._timer);
    t._timer = setTimeout(() => setKey(t.dataset.key, parseInt(t.value, 10)), 150);
  }
});

const lines = v => v.split(/\r?\n/).map(x => x.trim()).filter(Boolean);

main.addEventListener('keydown', e => {
  const t = e.target;
  if (e.key === 'Enter' && t.dataset.chipAdd) {
    const v = t.value.trim();
    if (!v) return;
    const name = /\.exe$/i.test(v) ? v : v + '.exe';
    edit(c => {
      const list = c.profiles[c.activeProfile].autoHide[t.dataset.chipAdd];
      if (!list.some(x => x.toLowerCase() === name.toLowerCase())) list.push(name);
    });
  } else if (e.key === 'Enter' && t.id === 'urlInput') {
    addUrl();
  } else if (e.key === 'Enter' && t.id === 'profileName') {
    $('#profileOk').click();
  }
});

function addUrl() {
  const v = $('#urlInput').value.trim();
  if (!/^https?:\/\/\S+/i.test(v)) { toast('Enter a web address starting with https://'); return; }
  send({ type: 'addUrl', url: v });
}

main.addEventListener('click', e => {
  const t = e.target;
  const b = t.closest('button, [data-item], [data-mon]');
  if (!b) return;

  if (b.dataset.seg === undefined && b.parentElement && b.parentElement.dataset.seg) {
    const key = b.parentElement.dataset.seg;
    setKey(key, b.dataset.v);
    return;
  }
  if (b.dataset.act) return action(b.dataset.act);
  if (b.dataset.shell) return send({ type: 'shell', action: b.dataset.shell });
  if (b.dataset.preset) {
    const h = +b.dataset.preset;
    const icon = { 40: 20, 48: 24, 64: 32 }[h] || 24;
    return edit(c => Object.assign(c.profiles[c.activeProfile].resize, { height: h, iconSize: icon, buttonWidth: Math.round(h * 0.92), enabled: true }));
  }
  if (b.dataset.example !== undefined) {
    const x = STYLER_EXAMPLES[+b.dataset.example];
    return edit(c => { const st = c.profiles[c.activeProfile].styler; st.rules.push({ target: x.target, styles: [...x.styles] }); st.enabled = true; });
  }
  if (b.dataset.ruleDel !== undefined) {
    return edit(c => c.profiles[c.activeProfile].styler.rules.splice(+b.dataset.ruleDel, 1));
  }
  if (b.dataset.chipDel) {
    return edit(c => c.profiles[c.activeProfile].autoHide[b.dataset.chipDel].splice(+b.dataset.i, 1));
  }
  if (b.dataset.star) {
    const id = b.dataset.star;
    return edit(c => {
      const items = c.profiles[c.activeProfile].wallpaper.playlist.items;
      const i = items.indexOf(id);
      if (i >= 0) items.splice(i, 1); else items.push(id);
    });
  }
  if (b.dataset.remove) {
    const it = itemById(b.dataset.remove);
    if (it && confirm(`Remove "${it.name}" from the library? The file itself is not deleted.`)) send({ type: 'removeItem', id: it.id });
    return;
  }
  if (b.dataset.slotDel !== undefined) {
    return edit(c => c.profiles[c.activeProfile].wallpaper.playlist.schedule.splice(+b.dataset.slotDel, 1));
  }
  if (b.dataset.prof) return profileAction(b.dataset.prof, b.dataset.name);
  if (b.dataset.mon) {
    selectedMonitor = b.dataset.mon;
    return renderPage();
  }
  if (b.dataset.item) {
    const id = b.dataset.item;
    return edit(c => {
      const w = c.profiles[c.activeProfile].wallpaper;
      if (w.layout === 'span') w.spanItem = id;
      else if (selectedMonitor) w.monitors[selectedMonitor] = id;
      w.enabled = true;
    });
  }
});

function action(a) {
  switch (a) {
    case 'pauseAll': send({ type: 'pauseAll', value: !cfg().pausedAll }); break;
    case 'next': send({ type: 'nextWallpaper' }); break;
    case 'reset': send({ type: 'resetAll' }); toast('Everything is back to stock Windows.'); break;
    case 'addFiles': send({ type: 'addFiles' }); break;
    case 'addUrl': addUrl(); break;
    case 'openLogs': send({ type: 'openFolder', which: 'logs' }); break;
    case 'openConfig': send({ type: 'openFolder', which: 'config' }); break;
    case 'addRule': edit(c => c.profiles[c.activeProfile].styler.rules.push({ target: '', styles: [] })); break;
    case 'importTheme': send({ type: 'importTheme' }); break;
    case 'exportTheme': send({ type: 'exportTheme' }); break;
    case 'addSlot': edit(c => {
      const pl = c.profiles[c.activeProfile].wallpaper.playlist;
      const last = pl.schedule.length ? pl.schedule[pl.schedule.length - 1].time : '06:00';
      const [h, m] = last.split(':').map(Number);
      pl.schedule.push({ time: `${String((h + 6) % 24).padStart(2, '0')}:${String(m || 0).padStart(2, '0')}`, itemId: c.library[0]?.id || '' });
    }); break;
  }
}

function profileAction(act, name) {
  if (act === 'switch' || act === 'delete') {
    if (act === 'delete' && !confirm(`Delete the profile "${name}"?`)) return;
    send({ type: 'profile', action: act, name });
    return;
  }
  const form = $('#profileForm'), input = $('#profileName');
  form.hidden = false;
  $('#profileFormTitle').textContent = act === 'rename' ? `Rename "${name}"` : `New profile from "${name}"`;
  input.value = act === 'rename' ? name : `${name} copy`;
  input.focus(); input.select();
  $('#profileOk').onclick = () => {
    const newName = input.value.trim();
    if (!newName) return;
    if (cfg().profiles[newName]) { toast('A profile with that name already exists.'); return; }
    send({ type: 'profile', action: act, name, newName });
    form.hidden = true;
  };
  $('#profileCancel').onclick = () => (form.hidden = true);
}

function wireDrop() {
  const d = $('#drop');
  if (!d) return;
  d.addEventListener('dragenter', e => { e.preventDefault(); d.classList.add('over'); });
  d.addEventListener('dragover', e => { e.preventDefault(); d.classList.add('over'); });
  d.addEventListener('dragleave', () => d.classList.remove('over'));
  d.addEventListener('drop', e => {
    e.preventDefault();
    d.classList.remove('over');
    if (e.dataTransfer.files.length) send({ type: 'dropFiles' }, e.dataTransfer.files);
  });
}

window.addEventListener('resize', () => renderMonitorsOnly());

// ---------- start ----------

if (host) send({ type: 'ready' });
else startMock();

// ---------- browser-only mock (lets the UI be previewed outside the app) ----------

function startMock() {
  document.body.classList.add('no-host');
  const profile = () => ({
    autoHide: { enabled: true, edgeReveal: true, whenMaximized: false, alwaysHide: [], neverHide: ['obs64.exe'] },
    wallpaper: { enabled: true, layout: 'perMonitor', monitors: { '\\\\.\\DISPLAY1': 'a1' }, spanItem: null, fit: 'fill',
      muted: true, volume: 30, interactive: true, pauseWhenFullscreen: true, pauseWhenMaximized: true, pauseOnBattery: true,
      pauseOnBatterySaver: true, playlist: { enabled: false, mode: 'interval', intervalMinutes: 30, shuffle: false, items: [], schedule: [] } },
    taskbarLook: { enabled: true, style: 'color', color: '#202040', opacity: 50, accentColor: false, onlyWhenMaximized: false },
    notificationCenter: { enabled: false, theme: 'TranslucentShell' },
    startMenu: { enabled: false, theme: 'WindowGlass', layout: '' },
    resize: { enabled: false, height: 48, iconSize: 24, buttonWidth: 44 },
    hover: { enabled: true, maxScale: 125, radius: 100, curve: 0, spacing: 50 },
    styler: { enabled: false, theme: '', rules: [{ target: 'Taskbar.TaskbarFrame > Grid#RootGrid', styles: ['Margin=8,0,8,6', 'CornerRadius=14'] }], constants: [] },
    explorer: { enabled: false, explorerTheme: 'Translucent Explorer11', backdrop: 'mica', settingsApp: true, settingsTheme: 'Translucent_Settings11' },
  });
  onMessage({
    type: 'state',
    config: { version: 1, activeProfile: 'Default', startWithWindows: true, firstRunDone: true, pausedAll: false,
      library: [
        { id: 'a1', name: 'Rainy window', path: 'C:\\Videos\\rain.mp4', type: 'video' },
        { id: 'a2', name: 'Pixel city', path: 'C:\\Videos\\city.gif', type: 'image' },
        { id: 'a3', name: 'Particles', path: 'C:\\Walls\\particles.html', type: 'html' },
      ],
      profiles: { Default: profile(), Work: profile(), Gaming: profile(), Battery: profile() } },
    monitors: [{ id: '\\\\.\\DISPLAY1', index: 1, primary: true, x: 0, y: 0, w: 1920, h: 1080 },
               { id: '\\\\.\\DISPLAY2', index: 2, primary: false, x: 1920, y: -200, w: 2560, h: 1440 }],
    status: { version: '0.1.0', build: 26300, displayVersion: '26H2', autoHide: 'Watching for full-screen apps', autoHideOn: true,
      wallpaper: 'Playing on 1 surface(s)', wallpaperOn: true, onBattery: false, batterySaver: false, hasBattery: true,
      startupSet: true, shell: '3 mod(s) active through Windhawk', shellOn: true, windhawkInstalled: true, windhawkRunning: true,
      buildUnconfirmed: true, mods: [{ id: 'taskbar-icon-size', name: 'Taskbar height and icon size', feature: 'Resize the taskbar', installed: false }],
      warnings: ['Windows build 26300 has not been verified with nightOwl yet.'], logs: '', configPath: 'C:\\Users\\you\\AppData\\Roaming\\nightOwl\\config.json' },
  });
}

function mockHost(msg) {
  if (msg.type === 'setConfig') return;
  if (msg.type === 'thumb') setTimeout(() => onMessage({ type: 'thumb', id: msg.id, ok: false }), 50);
  else toast(`(preview) ${msg.type}`);
}
