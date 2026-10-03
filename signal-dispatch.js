const dispatchSignals = new Map();
const dispatchSignalMarkers = new Map();
const dispatchLocks = new Map();
const routePreviewLayer = L.layerGroup().addTo(map);
const activeRouteLayer = L.layerGroup().addTo(map);
const occupiedTrackLayer = L.layerGroup().addTo(map);
// Shared Canvas draw order changes whenever a route or preview is rebuilt. Keep
// occupancy above those paths regardless of when each overlay last changed.
const occupiedTrackPane = map.createPane('dispatchOccupancy');
occupiedTrackPane.style.zIndex = '410';
occupiedTrackPane.style.pointerEvents = 'none';
const occupiedTrackRenderer = L.canvas({ pane: 'dispatchOccupancy' });
let routeStart, routeEnd, routeCandidates = [], selectedCandidate;
let dispatchState = { available: false, authority: true, routes: [] };
let routeBusy = false, previewGeneration = 0;
const signalMinZoom = 16;
let lastRouteGeometry = '', lastRouteCards = '', highlightedSignals = new Set();
let lastOccupiedTracks = '', lastManualShunting = '';

function routeMessage(message, error = false) {
  const status = document.getElementById('routeStatus');
  status.textContent = message;
  status.classList.toggle('route-error', error);
  sidebar.open('routesTab');
}
function escapeSignalText(value) {
  const element = document.createElement('span');
  element.textContent = String(value);
  return element.innerHTML;
}
function signalIcon(signal) {
  return L.divIcon({
    className: 'dispatch-signal',
    iconSize: [30, 30], iconAnchor: [signal.shunting ? -7 : signal.normal ? 15 : 37, 15],
    html: `<svg viewBox="-15 -15 30 30" aria-hidden="true"><g transform="rotate(${Number(signal.rotation) || 0})"><path d="M0,-14 L-5,-7 L5,-7 Z" fill="var(--signal-colour, red)" stroke="#111"/><rect x="-5" y="-5" width="10" height="15" rx="3" fill="#111" stroke="#ddd"/><circle cy="0" r="3.5" fill="var(--signal-colour, red)"/><path d="M0,10 L0,14" stroke="#ddd" stroke-width="2"/></g></svg>`
  });
}
function refreshSignalMarker(signal) {
  let marker = dispatchSignalMarkers.get(signal.id);
  if (!marker) {
    marker = L.marker(signal.position, { icon: signalIcon(signal), title: signal.name, zIndexOffset: 2000, bubblingMouseEvents: false })
      .on('click', () => selectRouteSignal(signal.id)).addTo(map);
    marker.bindTooltip('', { direction: 'top' });
    marker.dispatchIconKey = `${signal.rotation}:${signal.normal}:${signal.shunting}`;
    marker.dispatchPosition = [...signal.position];
    dispatchSignalMarkers.set(signal.id, marker);
  }
  const iconKey = `${signal.rotation}:${signal.normal}:${signal.shunting}`;
  if (marker.dispatchIconKey !== iconKey) { marker.setIcon(signalIcon(signal)); marker.dispatchIconKey = iconKey; }
  if (marker.dispatchPosition[0] !== signal.position[0] || marker.dispatchPosition[1] !== signal.position[1]) {
    marker.setLatLng(signal.position); marker.dispatchPosition = [...signal.position];
  }
  const element = marker.getElement();
  const colour = /^#[\da-fA-F]{6}$/.test(signal.colour) || ['red', 'green', 'yellow', 'white'].includes(signal.colour) ? signal.colour : 'red';
  if (element && element.style.getPropertyValue('--signal-colour') !== colour) element.style.setProperty('--signal-colour', colour);
  element?.classList.toggle('selected', signal.id === routeStart || signal.id === routeEnd);
  const tooltipKey = JSON.stringify([signal.name, signal.aspect, signal.shunting, signal.shuntingAllowed]);
  if (marker.dispatchTooltipKey !== tooltipKey) {
    const tooltip = `${escapeSignalText(signal.name)} · ${escapeSignalText(signal.aspect)}${signal.shunting ? ' · Rangiersignal' : ''}${signal.shuntingAllowed ? ' · Rangierfahrt erlaubt' : ''}`;
    marker.setTooltipContent(tooltip); marker.dispatchTooltipKey = tooltipKey;
    element?.setAttribute('title', signal.name);
    element?.setAttribute('aria-label', `${signal.name} (${signal.aspect})`);
  }
}
function refreshVisibleDispatchSignals() {
  const bounds = map.getBounds().pad(0.15);
  const visible = new Set();
  if (map.getZoom() >= signalMinZoom) {
    for (const signal of dispatchSignals.values()) {
      if (!bounds.contains(signal.position)) continue;
      visible.add(signal.id);
      refreshSignalMarker(signal);
    }
  }
  for (const [id, marker] of dispatchSignalMarkers) {
    if (!visible.has(id)) { marker.remove(); dispatchSignalMarkers.delete(id); }
  }
}
map.on('moveend zoomend', refreshVisibleDispatchSignals);
function updateDispatchSignals(data) {
  const existing = new Set();
  for (const signal of data) {
    existing.add(signal.id);
    dispatchSignals.set(signal.id, signal);
  }
  for (const id of dispatchSignals.keys()) if (!existing.has(id)) dispatchSignals.delete(id);
  refreshVisibleDispatchSignals();
  if ((routeStart !== undefined && !existing.has(routeStart)) || (routeEnd !== undefined && !existing.has(routeEnd))) resetRouteSelection();
}
function updateDispatchSignalStates(data) {
  for (const state of data) {
    const signal = dispatchSignals.get(state.id);
    if (!signal || (signal.aspect === state.aspect && signal.colour === state.colour && signal.stop === state.stop && signal.route === state.route &&
        signal.shuntingAllowed === state.shuntingAllowed && signal.manualShunting === state.manualShunting)) continue;
    Object.assign(signal, state);
    if (dispatchSignalMarkers.has(state.id)) refreshSignalMarker(signal);
  }
}
async function routeRequest(path) {
  const response = await fetch(new URL(path, location), { method: 'POST' });
  if (response.status === 204) return;
  const text = await response.text();
  let result;
  try { result = JSON.parse(text); } catch { result = {}; }
  if (!response.ok) throw new Error(result.error || (response.status === 403 ? 'Permission required: enable Fahrstraßen or Hilfsauflösung for your dispatcher name in the game mod settings.' : `Request failed (${response.status}).`));
  return result;
}
function updateEndpointText() {
  const name = id => dispatchSignals.get(id)?.name || `S-${id}`;
  document.getElementById('routeEndpoints').textContent = routeStart === undefined ? 'No signals selected.' :
    routeEnd === undefined ? `${name(routeStart)} → select destination` : `${name(routeStart)} → ${name(routeEnd)}`;
  const selected = new Set([routeStart, routeEnd].filter(id => id !== undefined));
  for (const id of new Set([...highlightedSignals, ...selected])) {
    dispatchSignalMarkers.get(id)?.getElement()?.classList.toggle('selected', selected.has(id));
  }
  highlightedSignals = selected;
}
function resetRouteSelection() {
  previewGeneration++;
  routeStart = routeEnd = selectedCandidate = undefined;
  routeCandidates = [];
  routePreviewLayer.clearLayers();
  document.getElementById('routeCandidates').replaceChildren();
  updateEndpointText();
}
document.getElementById('routeReset').addEventListener('click', resetRouteSelection);
document.getElementById('routeMode').addEventListener('change', resetRouteSelection);
async function selectRouteSignal(id) {
  sidebar.open('routesTab');
  if (routeBusy) return;
  if (!dispatchState.available) { routeMessage('DV Signals is not ready.', true); return; }
  if (!dispatchState.authority) { routeMessage("Connect to the multiplayer host's Remote Dispatch to set routes.", true); return; }
  const mode = document.getElementById('routeMode').value;
  const signal = dispatchSignals.get(id);
  if (mode === 'manualShunting') {
    if (!signal.canShunt) { routeMessage('Select a main signal or shunting signal.', true); return; }
    await setManualShunting(id, !signal.manualShunting);
    return;
  }
  if (!(mode === 'shunting' ? signal.canShunt : signal.normal)) {
    routeMessage(mode === 'shunting' ? 'Select a main or shunting signal for a Rangierfahrstraße.' : 'Select a main signal for a Fahrstraße.', true);
    return;
  }
  if (routeStart === undefined || routeEnd !== undefined) {
    resetRouteSelection(); routeStart = id; updateEndpointText(); routeMessage('Select the destination signal.'); return;
  }
  if (routeStart === id) { resetRouteSelection(); return; }
  routeEnd = id; updateEndpointText();
  const generation = ++previewGeneration;
  routeBusy = true;
  routeMessage('Finding available paths…');
  try {
    const result = await routeRequest(`/route/preview?start=${routeStart}&end=${routeEnd}&mode=${mode}`);
    if (generation !== previewGeneration) return;
    routeCandidates = result.candidates;
    renderCandidates();
    routeMessage(routeCandidates.length ? `Choose a path to preview, then set the route.${result.truncated ? ' Search limit reached; choose closer endpoints to see more paths.' : ''}` : 'No forward path exists between these signal heads. Check the direction arrows.', !routeCandidates.length);
  } catch (error) { if (generation === previewGeneration) routeMessage(error.message, true); }
  finally { routeBusy = false; }
}
async function setManualShunting(id, allowed) {
  if (routeBusy || !dispatchState.authority) return;
  routeBusy = true;
  try {
    const state = await routeRequest(`/route/${id}/shunting?allowed=${allowed}`);
    if (state) updateDispatchSignalStates([state]);
    routeMessage(allowed ? 'Rangierfahrt erlaubt. No Fahrstraße is set; switches are not checked or locked. Click again or choose Rangierhalt to stop.' : 'Rangierhalt set.');
  } catch (error) { routeMessage(error.message, true); }
  finally { routeBusy = false; }
}
function drawPath(layer, candidate, colour, dashed = false, occupied = false) {
  for (const id of candidate.tracks) {
    const track = trackPolyLines.get(String(id));
    if (track) L.polyline(track.getLatLngs(), {
      renderer: occupied ? occupiedTrackRenderer : canvasRenderer,
      pane: occupied ? 'dispatchOccupancy' : 'overlayPane',
      color: colour, weight: 6, opacity: occupied ? 1 : 0.65, dashArray: dashed ? '8 6' : null, interactive: false
    }).addTo(layer);
  }
}
function renderCandidates() {
  const container = document.getElementById('routeCandidates');
  container.replaceChildren();
  routeCandidates.forEach((candidate, index) => {
    const box = document.createElement('div'); box.className = 'route-card';
    const preview = document.createElement('button'); preview.type = 'button'; preview.className = 'route-preview-button';
    preview.textContent = `Path ${index + 1} · ${candidate.length} m · ${Object.keys(candidate.switches).length} switches`;
    preview.setAttribute('aria-pressed', String(selectedCandidate === candidate.id));
    preview.addEventListener('click', () => {
      selectedCandidate = candidate.id; routePreviewLayer.clearLayers(); drawPath(routePreviewLayer, candidate, '#ffd166', true); renderCandidates();
    });
    box.append(preview);
    const details = document.createElement('div'); details.className = 'route-path-details';
    details.textContent = Object.entries(candidate.switches).map(([id, branch]) => `J-${id}: ${branch}`).join(' → ') || 'Direct track';
    box.append(details);
    if (candidate.unavailable) {
      const reason = document.createElement('p'); reason.className = 'route-error'; reason.textContent = candidate.unavailable; box.append(reason);
    }
    if (selectedCandidate === candidate.id) {
      const set = document.createElement('button'); set.type = 'button'; set.textContent = 'Set Fahrstraße';
      set.disabled = !!candidate.unavailable || routeBusy;
      set.addEventListener('click', async () => {
        if (routeBusy) return;
        routeBusy = true; set.disabled = true;
        try { await routeRequest(`/route/${candidate.id}/set`); resetRouteSelection(); routeMessage('Fahrstraße set. Switches are locked.'); }
        catch (error) { routeMessage(error.message, true); }
        finally { routeBusy = false; set.disabled = !!candidate.unavailable; }
      });
      box.append(set);
    }
    container.append(box);
  });
}
function updateDispatchRoutes(state) {
  const first = !dispatchState.available && state.available;
  const previousError = dispatchState.error;
  dispatchState = state;
  const locks = new Map(Object.entries(state.locks || {}).map(([id, branch]) => [Number(id), branch]));
  for (const id of new Set([...dispatchLocks.keys(), ...locks.keys()])) {
    if (dispatchLocks.has(id) === locks.has(id)) continue;
    const element = junctions[id]?.marker.getElement();
    element?.classList.toggle('junction-route-locked', locks.has(id));
    if (element) element.setAttribute('title', locks.has(id) ? `J-${id}: route locked` : `J-${id}`);
  }
  dispatchLocks.clear(); locks.forEach((branch, id) => dispatchLocks.set(id, branch));
  const geometry = JSON.stringify((state.routes || []).map(route => [route.id, route.tracks, route.shunting]));
  if (geometry !== lastRouteGeometry) {
    lastRouteGeometry = geometry; activeRouteLayer.clearLayers();
    for (const route of state.routes || []) drawPath(activeRouteLayer, route, route.shunting ? '#bda7ff' : '#52d6a4');
  }
  const cards = JSON.stringify([state.authority, state.routes]);
  if (cards !== lastRouteCards) { lastRouteCards = cards; renderActiveRoutes(); }
  const occupied = JSON.stringify(state.occupiedTracks || []);
  if (occupied !== lastOccupiedTracks) {
    lastOccupiedTracks = occupied; occupiedTrackLayer.clearLayers();
    drawPath(occupiedTrackLayer, { tracks: state.occupiedTracks || [] }, '#ef5350', false, true);
    document.getElementById('trackOccupancy').textContent = state.occupiedTracks?.length ?
      `Gleisfreimeldung: ${state.occupiedTracks.length} occupied tracks (red). Normal Fahrstraßen into them are blocked.` : 'Gleisfreimeldung: tracks clear.';
  }
  const manual = JSON.stringify([state.authority, state.manualShunting || []]);
  if (manual !== lastManualShunting) {
    lastManualShunting = manual;
    const container = document.getElementById('manualShuntingList'); container.replaceChildren();
    if (!state.manualShunting?.length) container.textContent = 'No manual shunting permissions.';
    for (const id of state.manualShunting || []) {
      const card = document.createElement('div'); card.className = 'route-card';
      card.textContent = `${dispatchSignals.get(id)?.name || `S-${id}`} · Rangierfahrt erlaubt `;
      if (state.authority) {
        const stop = document.createElement('button'); stop.type = 'button'; stop.textContent = 'Rangierhalt';
        stop.addEventListener('click', () => setManualShunting(id, false)); card.append(stop);
      }
      container.append(card);
    }
  }
  if (state.error && state.error !== previousError) routeMessage(state.error, true);
  else if (first) document.getElementById('routeStatus').textContent = state.authority ? 'Ready. Signals are at stop until a route is set.' : "Viewing the host's routes. Use the host's browser console to dispatch.";
  else if (!state.available) document.getElementById('routeStatus').textContent = 'DV Signals is not ready.';
}
function renderActiveRoutes() {
  const container = document.getElementById('routeList'); container.replaceChildren();
  if (!dispatchState.routes?.length) { container.textContent = 'No active routes.'; return; }
  for (const route of dispatchState.routes) {
    const card = document.createElement('div'); card.className = 'route-card';
    const title = document.createElement('strong'); title.textContent = `${route.startName} → ${route.endName}`; card.append(title);
    const status = document.createElement('p');
    const labels = { set: 'Set · switches locked', occupied: 'Train in route · switches locked', fault: 'Fault · switches held locked', releasing: 'Hilfsauflösung · signals at stop' };
    status.textContent = `${route.shunting ? 'Rangierfahrstraße' : 'Fahrstraße'} · ${labels[route.state] || route.state}`; card.append(status);
    if (route.releaseAt) {
      const countdown = document.createElement('p'); countdown.className = 'route-countdown'; countdown.dataset.releaseAt = route.releaseAt; card.append(countdown);
    } else if (dispatchState.authority) {
      const cancel = document.createElement('button'); cancel.textContent = 'Cancel'; cancel.type = 'button'; cancel.disabled = route.entered;
      cancel.addEventListener('click', () => releaseRoute(route, false)); card.append(cancel);
      const auxiliary = document.createElement('button'); auxiliary.textContent = 'Hilfsauflösung'; auxiliary.type = 'button'; auxiliary.className = 'route-auxiliary';
      auxiliary.addEventListener('click', () => releaseRoute(route, true)); card.append(auxiliary);
    }
    container.append(card);
  }
  updateReleaseCountdowns();
}
function updateReleaseCountdowns() {
  for (const element of document.querySelectorAll('.route-countdown')) {
    const seconds = Math.max(0, Math.ceil((Date.parse(element.dataset.releaseAt) - Date.now()) / 1000));
    element.textContent = seconds > 0 ? `Switches unlock in ${seconds} s` : 'Waiting for host release…';
  }
}
setInterval(updateReleaseCountdowns, 500);
async function releaseRoute(route, auxiliary) {
  if (routeBusy) return;
  if (auxiliary && !window.confirm(`Hilfsauflösung for ${route.startName} → ${route.endName}?\nSignals go to stop now. Switches unlock after ${dispatchState.auxiliaryReleaseSeconds} seconds, even if wagons remain in the route.`)) return;
  routeBusy = true;
  try {
    await routeRequest(`/route/${route.id}/${auxiliary ? 'auxiliaryRelease' : 'cancel'}`);
    routeMessage(auxiliary ? 'Hilfsauflösung started. Signals are at stop; switches remain locked during the countdown.' : 'Route cancelled.');
  } catch (error) { routeMessage(error.message, true); }
  finally { routeBusy = false; }
}
// The initial /updates session includes signals and routes. Reuse it rather than loading the whole map twice.
