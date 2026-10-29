// Test helper: inspects the wallpaper WebViews over the DevTools protocol.
// Needs nightOwl started with NIGHTOWL_DEBUGPORT=9333. Usage: node tools/probe-wallpaper.mjs [expression]
const port = process.env.NIGHTOWL_DEBUGPORT || 9333;
const expr = process.argv[2] || `(() => { const v = document.querySelector('video');
  return { url: location.href, hidden: document.hidden, size: [innerWidth, innerHeight],
    video: v && { src: v.currentSrc, ready: v.readyState, paused: v.paused, t: +v.currentTime.toFixed(2),
      err: v.error && v.error.code, w: v.videoWidth, h: v.videoHeight, muted: v.muted, volume: v.volume,
      cls: document.getElementById('stage') && document.getElementById('stage').className } }; })()`;

const targets = await (await fetch(`http://127.0.0.1:${port}/json`)).json();
for (const t of targets.filter(t => t.type === 'page')) {
  const ws = new WebSocket(t.webSocketDebuggerUrl);
  await new Promise(r => ws.addEventListener('open', r));
  const result = await new Promise(resolve => {
    ws.addEventListener('message', e => {
      const m = JSON.parse(e.data);
      if (m.id === 1) resolve(m.result?.result?.value ?? m.result);
    });
    ws.send(JSON.stringify({ id: 1, method: 'Runtime.evaluate', params: { expression: expr, returnByValue: true, awaitPromise: true } }));
  });
  console.log(JSON.stringify(result));
  ws.close();
}
