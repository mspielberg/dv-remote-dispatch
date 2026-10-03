// Local browser test fixture. This does not connect to or control the game.
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const tracks = {
  '#A': [[0.0440, 0.0440], [0.0444, 0.0440]],
  '#B': [[0.0444, 0.0440], [0.0448, 0.0443], [0.0452, 0.0440]],
  '#D': [[0.0444, 0.0440], [0.0448, 0.0437], [0.0452, 0.0440]],
  '#C': [[0.0452, 0.0440], [0.0456, 0.0440]]
};
const signals = [
  { id: 1, name: 'HB-01', position: [0.04415, 0.04403], rotation: 0, aspect: 'stop', colour: 'red', stop: true, normal: true, shunting: false },
  { id: 2, name: 'HB-02', position: [0.0455, 0.04403], rotation: 0, aspect: 'stop', colour: 'red', stop: true, normal: true, shunting: false },
  { id: 3, name: 'HB-R01', position: [0.04415, 0.04396], rotation: 0, aspect: 'stop', colour: 'red', stop: true, normal: false, shunting: true },
  { id: 4, name: 'HB-R02', position: [0.0455, 0.04396], rotation: 0, aspect: 'stop', colour: 'red', stop: true, normal: false, shunting: true }
];
let junctionState = [0, 0];
signals.forEach(signal => { signal.canShunt = true; signal.shuntingAllowed = signal.manualShunting = false; });
let state = { available: true, authority: true, routes: [], locks: {}, manualShunting: [], occupiedTracks: [], auxiliaryReleaseSeconds: 90, error: '' };
let candidates = [];
function response(res, data, status = 200) { res.writeHead(status, { 'Content-Type': 'application/json' }); res.end(JSON.stringify(data)); }
const server = http.createServer((req, res) => {
  const url = new URL(req.url, 'http://localhost:8724');
  if (url.pathname === '/track') return response(res, tracks);
  if (url.pathname === '/junction') return response(res, [{ position: tracks['#A'][1], branches: ['#B', '#D'] }, { position: tracks['#C'][0], branches: ['#B', '#D'] }]);
  if (url.pathname === '/signal') return response(res, signals);
  if (url.pathname === '/route') return response(res, state);
  if (url.pathname === '/player') return response(res, { player: { position: [0.0448, 0.044], rotation: 0, color: 'aqua' } });
  if (url.pathname.startsWith('/updates/')) return setTimeout(() => response(res, { signals, routes: state, junctions: junctionState, cars: {}, jobs: {} }), 250);
  const manual = /^\/route\/(\d+)\/shunting$/.exec(url.pathname);
  if (manual) {
    const signal = signals.find(s => s.id === Number(manual[1]));
    if (!signal) return response(res, { error: 'Signal not found.' }, 404);
    const allowed = url.searchParams.get('allowed') === 'true';
    signal.shuntingAllowed = signal.manualShunting = allowed;
    signal.stop = !allowed; signal.colour = allowed ? 'white' : 'red';
    signal.aspect = allowed && signal.shunting ? 'shunting' : 'stop';
    state.manualShunting = signals.filter(s => s.manualShunting).map(s => s.id);
    return response(res, signal);
  }
  if (url.pathname === '/route/preview') {
    const start = Number(url.searchParams.get('start')), end = Number(url.searchParams.get('end'));
    candidates = [0, 1].map(branch => ({ id: `preview-${branch}`, start, end,
      startName: signals.find(s => s.id === start).name, endName: signals.find(s => s.id === end).name,
      shunting: url.searchParams.get('mode') === 'shunting', length: branch ? 265 : 250,
      tracks: ['#A', branch ? '#D' : '#B', '#C'], switches: { 0: branch, 1: branch }, signals: [start], unavailable: null }));
    return response(res, { candidates, truncated: false });
  }
  const route = /^\/route\/([^/]+)\/(set|cancel|auxiliaryRelease)$/.exec(url.pathname);
  if (route) {
    const [, id, command] = route;
    if (command === 'set') {
      const selected = candidates.find(c => c.id === id);
      if (state.routes.length) return response(res, { error: 'Conflicts with the active route.' }, 409);
      state.routes = [{ ...selected, state: 'set', entered: false, dispatcher: 'preview' }];
      state.locks = selected.switches;
      junctionState = Object.values(selected.switches);
      const signal = signals.find(s => s.id === selected.start); signal.colour = selected.shunting ? 'white' : 'yellow'; signal.aspect = selected.shunting ? 'shunting' : 'proceed-slow'; signal.stop = false;
      return response(res, state.routes[0]);
    }
    if (command === 'cancel') { state.routes = []; state.locks = {}; signals.forEach(s => { s.colour = 'red'; s.aspect = 'stop'; s.stop = true; }); }
    else { state.routes[0].state = 'releasing'; state.routes[0].releaseAt = new Date(Date.now() + 90000).toISOString(); signals.forEach(s => { s.colour = 'red'; s.aspect = 'stop'; s.stop = true; }); }
    res.writeHead(204); return res.end();
  }
  if (/^\/junction\/\d+\/toggle$/.test(url.pathname)) return response(res, { error: 'This switch is locked by a Fahrstraße.' }, 409);
  const file = url.pathname === '/' ? 'index.html' : url.pathname.replace(/^\/res\//, '');
  if (!['index.html', 'main.js', 'style.css', 'signal-dispatch.js', 'leaflet.rotatedImageOverlay.js', 'icon.svg'].includes(file)) { res.writeHead(404); return res.end(); }
  res.setHeader('Content-Type', file.endsWith('.js') ? 'text/javascript' : file.endsWith('.css') ? 'text/css' : file.endsWith('.svg') ? 'image/svg+xml' : 'text/html');
  res.end(fs.readFileSync(path.join(root, file)));
});
server.listen(8724, '127.0.0.1', () => console.log('Local dispatch preview: http://127.0.0.1:8724'));
