const earthCircumference = 40e6;
const metersToDegrees = 360 / earthCircumference;

/////////////////////
// map

const mapBounds = [[0, 0], [0.15, 0.15]];
const maxBounds = [[-0.02, -0.02], [0.17, 0.17]];
const map = L.map('map', { minZoom: 13, maxBounds: maxBounds, tap: false })
.fitBounds(mapBounds);
L.control.scale().addTo(map);

let markerToFollow;
map.addEventListener('mousedown', _ => markerToFollow = null);

/////////////////////
// track

let trackPolyLines = {};

fetch('/track')
.then(resp => resp.json())
.then(tracks => {
  Object.entries(tracks).forEach(([trackId, coords]) =>
    trackPolyLines[trackId] = L.polyline(coords, { color: 'steelblue', interactive: false }).addTo(map)
  );
});

/////////////////////
// junctions

let junctions = [];
const junctionsReady = fetch('/junction')
.then(resp => resp.json())
.then(allJunctionData =>
  junctions = allJunctionData.map((data, index) => ({
    marker: createJunctionMarker(data.position, index),
    branches: data.branches,
  }))
);

function toggleJunction(index) {
  fetch(`/junction/${index}/toggle`, { method: 'POST' })
}

const junctionCanvasSize = 30;

function createJunctionShape(selectedBranch) {
  return `<g opacity="50%"><rect x="${-junctionCanvasSize/2}" y="${-junctionCanvasSize}" width="${junctionCanvasSize}" height="${junctionCanvasSize*2}" fill="red"/>` +
    (
      selectedBranch == 0 ? `<line x1="${junctionCanvasSize/2}" y1="${junctionCanvasSize}" x2="${-junctionCanvasSize/2}" y2="${-junctionCanvasSize}" stroke="white" stroke-width="10"/>` :
      selectedBranch == 1 ? `<line x1="${-junctionCanvasSize/2}" y1="${junctionCanvasSize}" x2="${junctionCanvasSize/2}" y2="${-junctionCanvasSize}" stroke="white" stroke-width="10"/>`
      : ''
    ) +
    `<rect x="${-junctionCanvasSize/2}" y="${-junctionCanvasSize}" width="${junctionCanvasSize}" height="${junctionCanvasSize*2}" fill="none" stroke="black" stroke-width="2%"/></g>`;
}

function createJunctionLabel(junctionId) {
  return `<text x="${-junctionCanvasSize/2+5}" y="${junctionCanvasSize-5}">${junctionId}</text>`
}

function createJunctionOverlay(junctionId) {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('id', junctionId)
  svg.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
  svg.setAttribute('viewBox', `${-junctionCanvasSize/2} ${-junctionCanvasSize} ${junctionCanvasSize} ${junctionCanvasSize*2}`);
  svg.innerHTML = createJunctionShape(null) + createJunctionLabel(junctionId);
  return svg;
}

function updateJunctionOverlay(junctionId, selectedBranch) {
  const junction = junctions[junctionId]
  junction.marker.getElement().innerHTML = createJunctionShape(selectedBranch) + createJunctionLabel(junctionId);
  var selectedTrackId = junction.branches[selectedBranch]
  trackPolyLines[selectedTrackId].setStyle({ color: 'steelblue', dashArray: null });
  var unselectedTrackPolyLine = trackPolyLines[junction.branches[1-selectedBranch]]
  unselectedTrackPolyLine
    .setStyle({ color: 'lightsteelblue', dashArray: "4" })
    .bringToBack();
}

function getJunctionOverlayBounds(position) {
  const size = metersToDegrees * 5;
  return [ [ position[0] - size, position[1] - size/2], [position[0] + size, position[1] + size/2] ];
}

function createJunctionMarker(p, junctionId) {
  return L.svgOverlay(createJunctionOverlay(junctionId), getJunctionOverlayBounds(p), { interactive: true })
    .addEventListener('click', () => toggleJunction(junctionId) )
    .addTo(map);
}

function updateAllJunctions() {
  return fetch('/junctionState')
  .then(resp => resp.json())
  .then(states =>
    states.forEach((state, index) => updateJunctionOverlay(index, state))
  );
}

/////////////////////
// cars

const carCanvasSize = 150;
const carHeight = 10;

// https://www.npmjs.com/package/string-hash
function stringHash(str) {
  let hash = 5381, i = str.length;
  while(i) {
    hash = (hash * 33) ^ str.charCodeAt(--i);
  }
  return hash >>> 0;
}

function createCarShape(carData) {
  const color = carData.jobId ? `hsl(${stringHash(carData.jobId) % 36 * 10} 40% 50%)` : 'gray';
  const lengthPx = carData.length * 6;
  const transform = `rotate(${carData.rotation - 90},0,0)`;
  const svg = carData.isLoco
  ? `<polygon points="${-lengthPx/2},-10 ${-lengthPx/2},10 ${lengthPx/2-10},10 ${lengthPx/2},0 ${lengthPx/2-10},-10" transform="${transform}" fill="goldenrod" fill-opacity="50%" stroke="goldenrod" stroke-width="1%"/>`
  : `<rect x="${-lengthPx/2}" y="-10" width="${lengthPx}" height="20" transform="${transform}" fill="${color}" fill-opacity="50%" stroke="${color}" stroke-width="1%"/>`;
  return svg;
}

function createCarLabel(carId, carData) {
  const lengthPx = carData.length * 6;
  const transform = `rotate(${carData.rotation % 180 - 90},0,0)`;
  return `<text x="${-lengthPx/2 + 7}" y="5" transform="${transform}" font-weight="bold">${carId}</text>`;
}

function createCarOverlay(carId, carData) {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('id', carId)
  svg.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
  svg.setAttribute('viewBox', `${-carCanvasSize/2} ${-carCanvasSize/2} ${carCanvasSize} ${carCanvasSize}`);
  return svg
}

function updateCarOverlay(carId, carData) {
  const svg = carMarkers[carId].getElement();
  svg.innerHTML = createCarShape(carData) + createCarLabel(carId, carData);
}

function getCarOverlayBounds(position) {
  const size = metersToDegrees * 12;
  return [ [ position[0] - size, position[1] - size], [position[0] + size, position[1] + size] ];
}

const carMarkers = new Map();

function createNewCar(carId, carData) {
  // new car
  carMarkers[carId] = L.svgOverlay(
    createCarOverlay(carId, carData),
    getCarOverlayBounds(carData.position),
    { interactive: true, bubblingMouseEvents: false })
    .addEventListener('click', e => markerToFollow = e.target)
    .addTo(map);
  updateCarOverlay(carId, carData);
}

function updateCar(carId, carData) {
  const marker = carMarkers[carId]
  if (!marker)
    return;
  updateCarOverlay(carId, carData);
  marker.setBounds(getCarOverlayBounds(carData.position));
}

function removeCar(carId) {
  const car = carMarkers[carId];
  if (car) {
    car.remove();
    delete carMarkers[carId];
  }
}

function updateAllCars() {
  return fetch('/car')
  .then(resp => resp.json())
  .then(cars => {
    Object.entries(cars).forEach(([carId, carData]) => {
      createNewCar(carId, carData);
    });
  });
}

function uuidv4() {
  return ([1e7]+-1e3+-4e3+-8e3+-1e11).replace(/[018]/g, c =>
    (c ^ crypto.getRandomValues(new Uint8Array(1))[0] & 15 >> c / 4).toString(16)
  );
}

function subscribeForEvents() {
  const events = new EventSource(`/eventSource?${uuidv4()}`)
  events.onmessage = e => {
    const msg = JSON.parse(e.data);
    switch (msg.type)
    {
    case "carDeleted":
      removeCar(msg.carId);
      break;
    case "carSpawned":
      createNewCar(msg.carId, msg.carData);
      break;
    case "carsUpdate":
      Object.entries(msg.cars).forEach(([carId, carData]) => {
        updateCar(carId, carData);
      });
      break;
    case "junctionSwitched":
      updateJunctionOverlay(msg.junctionId, msg.selectedBranch);
      break;
    }
    if (markerToFollow)
      map.panTo(markerToFollow.getBounds().getCenter());
  };
}

junctionsReady.then(_ => {
  updateAllCars();
  updateAllJunctions();
  subscribeForEvents();
});