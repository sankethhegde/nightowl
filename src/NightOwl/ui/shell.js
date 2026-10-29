'use strict';
// Phases 2–4 pages: taskbar look, resize, hover, notification center, styler, Explorer and Settings.
// They render through Windhawk mods; app.js merges these into its page table and owns the events.

const TASKBAR_THEMES = ['', 'TranslucentTaskbar', 'DockLike', 'SimplyTransparent', 'Squircle', 'Matter', 'WinXP', 'Bubbles',
  'RosePine', 'WinVista', 'CleanSlate', 'Lucent', 'SunValley', 'BottomDensy', 'TaskbarXII', 'xdark', 'Windows7', 'Aeris',
  'Plasma', 'WindowGlass', 'Surface', 'Luminosity_variant_Dock', 'LayerMicaUI', 'Fluid', 'TintedGlass', 'UltraWideFriendly', 'LiquidGlass2'];
const NOTIFY_THEMES = ['', 'TranslucentShell', 'Matter', 'Unified', 'WindowGlass', 'TintedGlass', 'Fluid', 'LiquidGlass',
  'BetterControl11', 'LayerMicaUI', 'Borderless', 'Densy', 'FrostyGlass'];
// Start Menu Styler 1.7's themes. [value, label] where the mod's label differs; "new" marks themes made for
// the redesigned Start menu (the one on this PC's build).
const START_THEMES = ['', 'WindowGlass', 'Fluid', ['LiquidGlass2', 'LiquidGlass'], ['LiquidGlass', 'LiquidGlass (Legacy)'], 'LayerMicaUI',
  'Command Center', 'FrostyGlass', 'TranslucentStartMenu', 'NoRecommendedSection', 'SideBySide', 'SideBySide2',
  'SideBySideMinimal', 'Down Aero', 'Windows10', 'Windows10_variant_Minimal', 'Windows11_Metro10', 'Windows11_Metro10Minimal',
  'Fluent2Inspired', 'RosePine', 'Everblush', 'SunValley', ['21996', 'SunValley (Legacy)'], 'UniMenu', 'LegacyFluent',
  'OnlySearch', 'OnlySearch_variant_Minimal', 'Oversimplified&Accentuated', 'Windows10X', 'TintedGlass', 'Borderless', 'FullScreen'];
const START_REDESIGNED = ['WindowGlass', 'Fluid', 'LiquidGlass2', 'LiquidGlass', 'LayerMicaUI', 'Command Center', 'FrostyGlass'];
const START_LAYOUTS = [['', 'Default for the theme'], ['default', 'Windows default'], ['forceNewLayout', 'New layout'],
  ['newLayoutSideBySide', 'New layout, side by side']];
const EXPLORER_THEMES = ['', 'Translucent Explorer11', 'MicaBar', 'NoCommandBar', 'Minimal Explorer11', 'Tabless', 'Matter',
  'WindowGlass', 'TintedGlass', 'LiquidGlass', 'MicaTabless', 'Compact Explorer11', 'Float'];
const SETTINGS_THEMES = ['', 'Translucent_Settings11', 'Translucent_Seamless', 'Densy', 'WindowGlass', 'ClassicSearchBar'];

const STYLER_EXAMPLES = [
  { name: 'Floating pill', target: 'Taskbar.TaskbarFrame > Grid#RootGrid', styles: ['Margin=8,0,8,6', 'CornerRadius=14'] },
  { name: 'Hide the Start button', target: 'Taskbar.ExperienceToggleButton#LaunchListButton[AutomationProperties.AutomationId=StartButton]', styles: ['Visibility=Collapsed'] },
  { name: 'Rounded hover highlight', target: 'Taskbar.TaskListButton > Taskbar.TaskListLabeledButtonPanel@CommonStates > Border#BackgroundElement', styles: ['CornerRadius=8'] },
  { name: 'Bold clock', target: 'SystemTray.TextIconContent > Grid#ContainerGrid > SystemTray.AdaptiveTextBlock#Base > TextBlock#InnerTextBlock', styles: ['FontWeight=SemiBold'] },
];

function themeLabel(t) { return t === '' ? 'None' : t.replace(/_variant_/, ' (').replace(/^(.*\()(.*)$/, '$1$2)').replace(/_/g, ' '); }

/** list items are a theme value, or [value, label]; mark(value) can append a note to the label. */
function themeSelect(key, value, list, mark) {
  const items = list.map(t => Array.isArray(t) ? t : [t, themeLabel(t)]);
  if (!items.some(([v]) => v === value)) items.push([value, themeLabel(value)]);
  return `<select data-key="${key}">${items.map(([v, label]) =>
    `<option value="${esc(v)}" ${v === value ? 'selected' : ''}>${esc(label + (mark ? mark(v) : ''))}</option>`).join('')}</select>`;
}

/** Windhawk / safety status shown at the top of every shell page (and on Home). */
function shellGate(compact) {
  const s = state.status, c = cfg();
  const parts = [];
  if (!s.windhawkInstalled) {
    parts.push(`<div class="warn"><i>&#xE7BA;</i><div>These features run through <b>Windhawk</b>, which isn't installed in nightOwl's folder yet.</div></div>`);
  } else {
    if (c.shellSafeMode) {
      parts.push(`<div class="warn"><i>&#xE7BA;</i><div class="grow">Safe mode: ${esc(c.shellSafeModeReason || 'Explorer kept restarting')}. All mods are off.
        Update the mods in Windhawk, then try again.</div><button class="btn" data-shell="leaveSafeMode">Try again</button></div>`);
    } else if (s.buildUnconfirmed && !c.pausedAll) {
      parts.push(`<div class="warn"><i>&#xE7BA;</i><div class="grow">${c.shellConfirmedBuild
          ? `Windows changed from build ${c.shellConfirmedBuild} to ${s.build}. Taskbar and Explorer mods are held off until you turn them back on (update mods in Windhawk first if any broke).`
          : `Taskbar and Explorer mods are held off until you confirm them on Windows build ${s.build}. You can undo everything with Ctrl+Alt+Shift+R.`}</div>
        <button class="btn primary" data-shell="confirmBuild">Turn on for build ${s.build}</button></div>`);
    }
    const missing = (s.mods || []).filter(m => !m.installed && m.id !== 'translucent-windows');
    if (missing.length && !compact) {
      parts.push(`<div class="card"><div class="row"><i class="lead">&#xE896;</i><div class="grow"><div class="title">Mods to install in Windhawk</div>
        <div class="desc">${missing.map(m => `${esc(m.name)} <span class="small">(${esc(m.feature)})</span>`).join(' · ')}</div></div>
        <button class="btn" data-shell="openWindhawk"><i>&#xE8A7;</i>Open Windhawk</button>
        <button class="btn icon" data-shell="refresh" title="Check again"><i>&#xE72C;</i></button></div></div>`);
    } else if (missing.length) {
      parts.push(`<p class="small" style="margin-top:6px">${missing.length} mod(s) still to install in Windhawk.</p>`);
    }
  }
  return parts.join('');
}

window.shellPages = {
  taskbarShell() {
    const t = prof().taskbarLook, r = prof().resize, h = prof().hover;
    const styles = [['clear', 'Clear'], ['blur', 'Blur'], ['acrylic', 'Acrylic'], ['color', 'Tinted colour']];
    return `
      <h2>Translucent taskbar</h2>
      ${featureHead('Translucent taskbar', 'A clear, blurred, acrylic or tinted taskbar background.', 'taskbarLook.enabled', t.enabled)}
      <div class="card">
        ${row('Look', '', seg('taskbarLook.style', t.style, styles), '&#xE790;')}
        ${t.style !== 'clear' ? `
          ${row('Tint colour', t.accentColor ? 'Using your Windows accent colour' : '',
            `<input type="color" value="${esc(t.color)}" data-key="taskbarLook.color" ${t.accentColor ? 'disabled' : ''}>
             <label class="switch"><span class="lbl">Accent</span><input type="checkbox" data-key="taskbarLook.accentColor" ${t.accentColor ? 'checked' : ''}></label>`, '&#xE2B1;')}
          ${row('Opacity', `${t.opacity}%`, `<input type="range" min="0" max="100" value="${t.opacity}" data-key="taskbarLook.opacity" data-live data-unit="%">`, '&#xE81C;')}` : ''}
        ${row('Clear until a window is maximised', 'Fully see-through on the desktop; the look above appears when a window is maximised.', toggle('taskbarLook.onlyWhenMaximized', t.onlyWhenMaximized), '&#xE922;')}
        <p class="small" style="margin-top:8px">Applies to the taskbar on every monitor. Notification center and Quick Settings are styled on their own page.</p>
      </div>

      <h2>Resize the taskbar</h2>
      ${featureHead('Taskbar size', 'Height and icon size. Maximised windows follow the new height.', 'resize.enabled', r.enabled)}
      <div class="card">
        ${row('Preset', '', `<div class="seg">${[[40, 'Small'], [48, 'Stock'], [64, 'Large']].map(([v, l]) =>
          `<button data-preset="${v}" class="${r.height === v ? 'on' : ''}">${l}</button>`).join('')}</div>`, '&#xE740;')}
        ${row('Height', `${r.height} px`, `<input type="range" min="32" max="80" value="${r.height}" data-key="resize.height" data-live data-unit=" px">`, '&#xE8A9;')}
        ${row('Icon size', `${r.iconSize} px`, `<input type="range" min="16" max="48" value="${r.iconSize}" data-key="resize.iconSize" data-live data-unit=" px">`, '&#xE91B;')}
        ${row('Button width', `${r.buttonWidth} px`, `<input type="range" min="24" max="80" value="${r.buttonWidth}" data-key="resize.buttonWidth" data-live data-unit=" px">`, '&#xE7EA;')}
        <p class="small" style="margin-top:8px">Stock Windows 11: height 48, icons 24, buttons 44.</p>
      </div>

      <h2>Hover animation</h2>
      ${featureHead('Magnify icons on hover', 'Dock-style: the icon under the mouse grows and its neighbours grow a little.', 'hover.enabled', h.enabled)}
      <div class="card">
        ${row('Size', `${(h.maxScale / 100).toFixed(2)}×`, `<input type="range" min="100" max="160" step="5" value="${h.maxScale}" data-key="hover.maxScale" data-live data-scale>`, '&#xE8A3;')}
        ${row('Reach', `${h.radius} px`, `<input type="range" min="20" max="400" step="10" value="${h.radius}" data-key="hover.radius" data-live data-unit=" px">`, '&#xE7C9;')}
        ${row('Feel', '', `<select data-key="hover.curve" data-num>${[[0, 'Smooth'], [1, 'Linear'], [2, 'Snappy']].map(([v, l]) =>
          `<option value="${v}" ${h.curve === v ? 'selected' : ''}>${l}</option>`).join('')}</select>`, '&#xE916;')}
        <p class="small" style="margin-top:8px">With a small taskbar keep the size at 1.30× or less so icons aren't clipped.</p>
      </div>`;
  },

  notifications() {
    const n = prof().notificationCenter;
    return `<div class="page-head"><div><h1>Notification center</h1><p class="muted">Profile ${esc(cfg().activeProfile)}</p></div></div>
      ${shellGate()}
      ${featureHead('Restyle the notification center and Quick Settings', 'A theme for the notification center, calendar and Quick Settings flyouts.', 'notificationCenter.enabled', n.enabled)}
      <div class="card">${row('Theme', '', themeSelect('notificationCenter.theme', n.theme, NOTIFY_THEMES), '&#xE790;')}</div>`;
  },

  startMenu() {
    const m = prof().startMenu;
    return `<div class="page-head"><div><h1>Start menu</h1><p class="muted">Profile ${esc(cfg().activeProfile)}</p></div></div>
      ${shellGate()}
      ${featureHead('Restyle the Start menu', 'A theme for the Start menu and its search box.', 'startMenu.enabled', m.enabled)}
      <div class="card">
        ${row('Theme', 'Themes marked “new Start” are made for the redesigned Start menu this PC has; older ones may only partly apply.',
          themeSelect('startMenu.theme', m.theme, START_THEMES, v => START_REDESIGNED.includes(v) ? ' · new Start' : ''), '&#xE790;')}
        ${row('Layout', 'Switch between the Start menu layouts this Windows build supports.', themeSelect('startMenu.layout', m.layout, START_LAYOUTS), '&#xF0E2;')}
      </div>
      <p class="small" style="margin-top:8px">Changes show the next time you open Start.</p>`;
  },

  explorer() {
    const e = prof().explorer;
    const backdrops = [['mica', 'Mica'], ['micaAlt', 'Mica Alt'], ['acrylic', 'Acrylic'], ['acrylicblur', 'Blur'], ['', 'Theme default'], ['none', 'None']];
    return `<div class="page-head"><div><h1>Explorer and Settings</h1><p class="muted">Profile ${esc(cfg().activeProfile)}</p></div></div>
      ${shellGate()}
      ${featureHead('Translucent File Explorer and Settings', 'Lets the wallpaper show softly through File Explorer and the Settings app.', 'explorer.enabled', e.enabled)}
      <div class="card">
        ${row('Backdrop', '', `<select data-key="explorer.backdrop">${backdrops.map(([v, l]) => `<option value="${v}" ${e.backdrop === v ? 'selected' : ''}>${l}</option>`).join('')}</select>`, '&#xE7F4;')}
        ${row('File Explorer theme', 'Translucent themes clear the panes too, not just the title bar.', themeSelect('explorer.explorerTheme', e.explorerTheme, EXPLORER_THEMES), '&#xE8B7;')}
        ${row('Also restyle the Settings app', '', toggle('explorer.settingsApp', e.settingsApp), '&#xE713;')}
        ${e.settingsApp ? row('Settings app theme', '', themeSelect('explorer.settingsTheme', e.settingsTheme, SETTINGS_THEMES), '&#xE790;') : ''}
        <div class="warn" style="margin-top:10px"><i>&#xE946;</i><div>Changes here apply to File Explorer windows opened <b>afterwards</b>. Close any open Explorer windows (and the Settings app) and open them again to see the new look.</div></div>
        <p class="small" style="margin-top:4px">Readability: Mica keeps text contrast closest to stock. Acrylic or Blur over a bright wallpaper can make text hard to read; pick Mica or use dark mode if it does.</p>
      </div>`;
  },

  styler() {
    const st = prof().styler;
    return `<div class="page-head"><div><h1>Styler</h1><p class="muted">Profile ${esc(cfg().activeProfile)}</p></div>
        <div class="btns"><button class="btn" data-act="importTheme"><i>&#xE8B5;</i>Import</button><button class="btn" data-act="exportTheme"><i>&#xEDE1;</i>Export</button></div></div>
      ${shellGate()}
      ${featureHead('Taskbar styler', 'Pick a theme, then fine-tune any element of the taskbar with your own rules.', 'styler.enabled', st.enabled)}
      <div class="card">
        ${row('Theme', 'Built-in themes from the Windows 11 Taskbar Styler mod.', themeSelect('styler.theme', st.theme, TASKBAR_THEMES), '&#xE790;')}
        ${row('Style constants', 'One per line, for themes that support them (name=value).', '', '&#xE943;')}
        <textarea class="code" data-list="constants" rows="3" placeholder="name=value">${esc(st.constants.join('\n'))}</textarea>
      </div>
      <h2>Custom rules</h2>
      <p class="small" style="margin:-4px 0 8px">Target an element, e.g. <code>Taskbar.TaskbarFrame &gt; Grid#RootGrid</code> (add <code>@PointerOver</code> for hover),
        then one <code>Property=Value</code> per line. A rule that matches nothing is skipped; it never breaks the taskbar.</p>
      ${st.rules.map((r, i) => `<div class="card rule">
          <div class="row"><input type="text" class="grow" value="${esc(r.target)}" data-rule-target="${i}" placeholder="Target element">
            <button class="btn icon" data-rule-del="${i}" title="Remove rule"><i>&#xE74D;</i></button></div>
          <textarea class="code" rows="3" data-rule-styles="${i}" placeholder="Property=Value">${esc(r.styles.join('\n'))}</textarea>
        </div>`).join('')}
      <div class="btns" style="margin-top:6px"><button class="btn" data-act="addRule"><i>&#xE710;</i>Add rule</button>
        ${STYLER_EXAMPLES.map((x, i) => `<button class="btn" data-example="${i}" title="Add as a rule">+ ${esc(x.name)}</button>`).join('')}</div>`;
  },
};
