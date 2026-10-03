const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { JSDOM } = require('jsdom');
const html = fs.readFileSync(path.join(__dirname, '..', 'index.html'), 'utf8');
const dom = new JSDOM(html, { url: 'http://localhost:7245' });
const document = dom.window.document;
const requests = [];
const layers = [];
const operations = { icons: 0, positions: 0, tooltips: 0, layerClears: 0 };
let zoom = 20, viewport = [0, 2];
const mapEvents = {};
const panes = new Map();
let fixtureState = { available: true, authority: true, routes: [], locks: {}, auxiliaryReleaseSeconds: 90 };
let failure;
function marker(position, options) {
  const element = document.createElement('div');
  return {
    element, options, tooltip: '', handler: null,
    on(event, callback) { this.handler = callback; return this; },
    addTo() { return this; }, bindTooltip() { return this; },
    setTooltipContent(text) { operations.tooltips++; this.tooltip = text; return this; },
    getElement() { return element; }, setLatLng() { operations.positions++; return this; }, setIcon(icon) { operations.icons++; this.icon = icon; return this; }, remove() { this.removed = true; }
  };
}
const context = vm.createContext({
  document, URL, Map, Set, Date, Number, String, Object, Promise,
  location: new URL('http://localhost:7245'), canvasRenderer: {},
  map: {
    createPane(name) { const pane = { style: {} }; panes.set(name, pane); return pane; },
    on(events, callback) { for (const event of events.split(' ')) mapEvents[event] = callback; },
    getZoom: () => zoom,
    getBounds: () => ({ pad() { return this; }, contains: position => position[0] >= viewport[0] && position[0] <= viewport[1] })
  },
  setInterval: () => 0,
  sidebar: { open() {} },
  window: { confirm: () => true },
  junctions: [{ marker: marker() }, { marker: marker() }],
  trackPolyLines: new Map(['A', 'B', 'C', 'D'].map(id => [id, { getLatLngs: () => [[0, 0], [1, 1]] }])),
  junctionsReady: Promise.resolve(),
  L: {
    canvas: options => ({ options }),
    layerGroup() { const layer = { paths: [], addTo() { layers.push(this); return this; }, clearLayers() { operations.layerClears++; this.paths = []; } }; return layer; },
    divIcon: x => x,
    marker,
    polyline(coords, options) { return { addTo(layer) { layer.paths.push({ coords, options }); return this; } }; }
  },
  fetch: async (url, options) => {
    const pathname = new URL(url).pathname;
    requests.push({ pathname, query: new URL(url).searchParams.toString(), method: options?.method });
    let data, status = 200;
    if (failure) { data = { error: failure }; status = 409; failure = undefined; }
    else if (pathname === '/signal') data = [];
    else if (pathname === '/route') data = fixtureState;
    else if (pathname === '/route/preview') data = { candidates: [
      { id: 'one', length: 250, switches: { 0: 0, 1: 0 }, tracks: ['A', 'B', 'C'], unavailable: null },
      { id: 'two', length: 275, switches: { 0: 1, 1: 1 }, tracks: ['A', 'D', 'C'], unavailable: null }
    ], truncated: false };
    else if (pathname.endsWith('/set')) data = { id: 'two' };
    else if (pathname.endsWith('/shunting')) {
      const allowed = new URL(url).searchParams.get('allowed') === 'true';
      data = { id: Number(pathname.split('/')[2]), aspect: 'stop', colour: allowed ? 'white' : 'red', stop: !allowed, shuntingAllowed: allowed, manualShunting: allowed };
    }
    else status = 204;
    return { ok: status < 400, status, json: async () => data, text: async () => JSON.stringify(data) };
  }
});
vm.runInContext(fs.readFileSync(path.join(__dirname, '..', 'signal-dispatch.js'), 'utf8'), context);
const flush = () => new Promise(resolve => setImmediate(resolve));
let checks = 0;
function check(condition, message) { assert.ok(condition, message); checks++; }
(async () => {
  await flush(); await flush();
  context.updateDispatchRoutes(fixtureState);
  const signals = [
    { id: 1, name: '<Entrance>', position: [0, 0], rotation: 0, normal: true, shunting: false, aspect: 'stop', colour: 'red' },
    { id: 2, name: 'Destination', position: [1, 1], rotation: 0, normal: true, shunting: false, aspect: 'stop', colour: 'red' },
    { id: 3, name: 'Shunt start', position: [0, 0], rotation: 0, normal: false, shunting: true, aspect: 'stop', colour: 'red' },
    { id: 4, name: 'Shunt end', position: [1, 1], rotation: 0, normal: false, shunting: true, aspect: 'stop', colour: 'red' }
  ];
  signals.forEach(signal => { signal.canShunt = true; signal.manualShunting = false; signal.shuntingAllowed = false; });
  context.updateDispatchSignals(signals);
  check(vm.runInContext('dispatchSignalMarkers.size', context) === 4, 'Render all signals.');
  check(vm.runInContext('dispatchSignalMarkers.get(1).tooltip', context).includes('&lt;Entrance&gt;'), 'Escape signal names in tooltips.');
  Object.keys(operations).forEach(key => operations[key] = 0);
  context.updateDispatchSignals(JSON.parse(JSON.stringify(signals)));
  check(operations.icons === 0 && operations.positions === 0 && operations.tooltips === 0, 'Identical full signal updates do not rebuild or move markers.');
  context.updateDispatchSignalStates([{ id: 1, aspect: 'clear', colour: 'green', stop: false }]);
  check(operations.icons === 0 && operations.positions === 0 && operations.tooltips === 1, 'One aspect update changes only its tooltip and lamp color.');
  check(vm.runInContext('dispatchSignalMarkers.get(1).getElement().style.getPropertyValue("--signal-colour")', context) === 'green', 'Update the existing lamp without replacing its SVG.');
  context.updateDispatchSignalStates([{ id: 1, aspect: 'clear', colour: 'green', stop: false }]);
  check(operations.tooltips === 1, 'Repeated compact signal states do no DOM work.');
  await context.selectRouteSignal(1);
  check(document.getElementById('routeEndpoints').textContent.includes('<Entrance>'), 'Show the chosen entrance without HTML injection.');
  await context.selectRouteSignal(2);
  check(document.querySelectorAll('.route-preview-button').length === 2, 'Offer both manual path choices.');
  check(requests.filter(r => r.pathname.endsWith('/set')).length === 0, 'Do not auto-establish a route after two clicks.');
  document.querySelectorAll('.route-preview-button')[1].click();
  check(document.querySelectorAll('.route-preview-button')[1].getAttribute('aria-pressed') === 'true', 'Keep the selected alternative highlighted.');
  check(layers[0].paths.length === 3 && layers[0].paths[0].options.dashArray === '8 6', 'Draw a dashed path preview.');
  const set = Array.from(document.querySelectorAll('#routeCandidates button')).find(b => b.textContent === 'Set Fahrstraße');
  set.click(); await flush();
  check(requests.some(r => r.pathname === '/route/two/set' && r.method === 'POST'), 'Commit exactly the chosen candidate.');
  check(document.getElementById('routeEndpoints').textContent === 'No signals selected.', 'Clear the selection after establishment.');
  fixtureState = { ...fixtureState, routes: [{ id: 'active', startName: 'Start', endName: 'End', tracks: ['A', 'D', 'C'], state: 'occupied', entered: true, shunting: false }], locks: { 0: 1, 1: 1 } };
  context.updateDispatchRoutes(fixtureState);
  check(vm.runInContext('dispatchLocks.size', context) === 2, 'Track both locked switches.');
  check(context.junctions[0].marker.element.classList.contains('junction-route-locked'), 'Show switch lock styling.');
  check(Array.from(document.querySelectorAll('#routeList button')).find(b => b.textContent === 'Cancel').disabled, 'Disable normal cancellation after entry.');
  check(layers[1].paths.length === 3, 'Draw the active route separately from the preview.');
  const routeCard = document.querySelector('#routeList .route-card');
  const clearCount = operations.layerClears;
  context.updateDispatchRoutes(JSON.parse(JSON.stringify(fixtureState)));
  check(operations.layerClears === clearCount && document.querySelector('#routeList .route-card') === routeCard, 'Identical route snapshots preserve overlays and route cards.');
  document.querySelector('.route-auxiliary').click(); await flush();
  check(requests.some(r => r.pathname === '/route/active/auxiliaryRelease'), 'Request Hilfsauflösung explicitly.');
  context.updateDispatchRoutes({ ...fixtureState, routes: [{ ...fixtureState.routes[0], releaseAt: new Date(Date.now() + 45000).toISOString(), state: 'releasing' }] });
  check(document.querySelector('.route-countdown').textContent.includes('45'), 'Display the server release countdown.');
  document.getElementById('routeMode').value = 'shunting'; document.getElementById('routeMode').dispatchEvent(new dom.window.Event('change'));
  await context.selectRouteSignal(1); await context.selectRouteSignal(2);
  check(document.querySelectorAll('.route-preview-button').length === 2, 'Allow main signals as endpoints of a formal Rangierfahrstraße.');
  context.resetRouteSelection();
  await context.selectRouteSignal(3); await context.selectRouteSignal(4);
  check(document.querySelectorAll('.route-preview-button').length === 2, 'Preview a Rangierfahrstraße.');
  context.resetRouteSelection(); fixtureState.authority = false; context.updateDispatchRoutes(fixtureState);
  const requestCount = requests.length;
  await context.selectRouteSignal(3);
  check(requests.length === requestCount && document.getElementById('routeStatus').textContent.includes('host'), 'Client consoles do not submit route commands.');
  fixtureState.authority = true; context.updateDispatchRoutes(fixtureState);
  failure = 'Occupied track'; await context.selectRouteSignal(3); await context.selectRouteSignal(4);
  check(document.getElementById('routeStatus').textContent === 'Occupied track', 'Surface server route rejection.');
  context.resetRouteSelection();
  document.getElementById('routeMode').value = 'manualShunting';
  const previewsBefore = requests.filter(r => r.pathname === '/route/preview').length;
  await context.selectRouteSignal(1);
  check(requests.some(r => r.pathname === '/route/1/shunting' && r.query === 'allowed=true'), 'One click grants manual shunting on a main signal.');
  check(requests.filter(r => r.pathname === '/route/preview').length === previewsBefore, 'Manual shunting does not preview or establish a route.');
  check(document.getElementById('routeStatus').textContent.includes('not checked or locked'), 'Explain manual shunting behavior in the console.');
  await context.selectRouteSignal(1);
  check(requests.some(r => r.pathname === '/route/1/shunting' && r.query === 'allowed=false'), 'Second click returns the signal to Rangierhalt.');
  context.updateDispatchRoutes({ ...fixtureState, manualShunting: [1], occupiedTracks: ['C'] });
  check(layers[2].paths.length === 1 && document.getElementById('trackOccupancy').textContent.includes('1 occupied'), 'Display occupied target track after formal route release.');
  const occupiedPath = layers[2].paths[0];
  context.updateDispatchRoutes({ ...fixtureState, manualShunting: [1], occupiedTracks: ['C'], routes: [{ ...fixtureState.routes[0], tracks: ['A', 'B', 'C'] }] });
  check(layers[2].paths[0] === occupiedPath && layers[1].paths.length === 3, 'Changing a route preserves unchanged occupancy geometry.');
  check(occupiedPath.options.renderer !== layers[1].paths[0].options.renderer &&
    Number(panes.get(occupiedPath.options.pane)?.style.zIndex) > 400,
    'Occupied track uses a separate canvas above routes, including routes drawn after occupancy.');
  check(panes.get(occupiedPath.options.pane)?.style.pointerEvents === 'none', 'Occupancy overlay does not intercept train or track clicks.');
  const manualStop = document.querySelector('#manualShuntingList button');
  check(manualStop.textContent === 'Rangierhalt', 'Show an explicit stop action for each manual shunting permission.');
  manualStop.click(); await flush();
  check(requests.at(-1).pathname === '/route/1/shunting' && requests.at(-1).query === 'allowed=false', 'Manual permission list revokes the selected signal only.');
  context.resetRouteSelection();
  const crowdedMap = [...signals, ...Array.from({ length: 5000 }, (_, i) => ({ ...signals[0], id: i + 100, position: [i + 100, 100] }))];
  context.updateDispatchSignals(crowdedMap);
  check(vm.runInContext('dispatchSignals.size', context) === 5004 && vm.runInContext('dispatchSignalMarkers.size', context) === 4, 'Keep 5000 offscreen signals out of the DOM while retaining their data.');
  const offscreenUpdates = crowdedMap.map(signal => ({ id: signal.id, aspect: 'clear', colour: 'green', stop: false }));
  Object.keys(operations).forEach(key => operations[key] = 0);
  context.updateDispatchSignalStates(offscreenUpdates);
  check(operations.icons === 0 && operations.positions === 0 && operations.tooltips <= 4, 'A world-wide aspect update touches only visible markers.');
  viewport = [100, 105]; mapEvents.moveend();
  check(vm.runInContext('dispatchSignalMarkers.size', context) === 6, 'Create newly visible signals when panning and discard old viewport markers.');
  check(vm.runInContext('dispatchSignalMarkers.get(100).getElement().style.getPropertyValue("--signal-colour")', context) === 'green', 'Newly visible markers use their latest offscreen aspect.');
  zoom = 13; mapEvents.zoomend();
  check(vm.runInContext('dispatchSignalMarkers.size', context) === 0, 'World overview does not create thousands of overlapping markers.');
  zoom = 20; viewport = [0, 2]; mapEvents.zoomend();
  check(vm.runInContext('dispatchSignalMarkers.size', context) === 4, 'Restore signal markers after zooming in.');
  check(context.signalIcon({ ...signals[0], rotation: 90 }).html.includes('viewBox="-15 -15 30 30"'), 'Keep east/west arrows inside the SVG bounds.');
  context.updateDispatchSignals([]);
  check(vm.runInContext('dispatchSignalMarkers.size', context) === 0, 'Remove unloaded signals.');
  // Exercise the real junction update helper without loading the rest of the application.
  const junctionElement = document.createElement('div');
  const branchStyles = [];
  const junctionContext = vm.createContext({
    junctions: [{ marker: { getElement: () => junctionElement }, branches: ['B', 'D'] }],
    trackPolyLines: new Map(['B', 'D'].map(id => [id, { setStyle(style) { branchStyles.push({ id, style }); }, bringToBack() {} }])),
    createJunctionShape: branch => `<g data-branch="${branch}"></g>`, createJunctionLabel: () => '<text>J-0</text>'
  });
  const mainScript = fs.readFileSync(path.join(__dirname, '..', 'main.js'), 'utf8');
  vm.runInContext(mainScript.slice(mainScript.indexOf("map.createPane('dispatchVehicles')"), mainScript.indexOf('L.control.scale()')), context);
  const overlayOptions = [];
  const overlayContext = vm.createContext({
    map: context.map, canvasRenderer: context.canvasRenderer, metersToDegrees: 1,
    allCarData: new Map(), carMarkers: new Map(), playerMarkers: new Map(),
    createCarRow() {}, createCarOverlay() {}, getCarOverlayBounds() {}, updateCarMarker() {},
    createJunctionOverlay() {}, createPlayerOverlay() {}, getPlayerOverlayBounds() {},
    L: { svgOverlay(svg, bounds, options) {
      overlayOptions.push(options);
      return { addEventListener() { return this; }, addTo() { return this; }, setZIndex() { return this; } };
    } }
  });
  vm.runInContext(mainScript.slice(mainScript.indexOf('function createNewCar('), mainScript.indexOf('function updateCar(')), overlayContext);
  vm.runInContext(mainScript.slice(mainScript.indexOf('function getJunctionOverlayBounds('), mainScript.indexOf('function updateAllJunctions(')), overlayContext);
  vm.runInContext(mainScript.slice(mainScript.indexOf('function createPlayerMarker('), mainScript.indexOf('function scrollToTrack(')), overlayContext);
  overlayContext.createNewCar('wagon', {});
  overlayContext.createJunctionMarker([0, 0], 0);
  overlayContext.createPlayerMarker(1, { position: [0, 0] });
  check(overlayOptions.every(options => Number(panes.get(options.pane)?.style.zIndex) > Number(panes.get('dispatchOccupancy').style.zIndex)),
    'Vehicle, player and switch overlays remain visible above opaque occupancy lines.');
  vm.runInContext(mainScript.slice(mainScript.indexOf('function updateJunctionOverlay('), mainScript.indexOf('function getJunctionOverlayBounds(')), junctionContext);
  junctionContext.updateJunctionOverlay(0, 0);
  check(branchStyles.length === 2, 'Render both branch styles for the initial junction state.');
  const firstJunctionShape = junctionElement.firstChild;
  junctionContext.updateJunctionOverlay(0, 0);
  check(branchStyles.length === 2 && junctionElement.firstChild === firstJunctionShape, 'An unchanged junction state preserves its SVG and track styles.');
  junctionContext.updateJunctionOverlay(0, 1);
  check(branchStyles.length === 4 && junctionElement.firstChild !== firstJunctionShape, 'A changed junction branch still updates its SVG and track styles.');
  console.log(`PASS: ${checks} frontend assertions.`);
  dom.window.close();
})().catch(error => { console.error(error); process.exitCode = 1; });
