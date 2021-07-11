const earthCircumference = 40e6;
const metersToDegrees = 360 / earthCircumference;

const map = L.map('map', { maxBounds: [[-0.5, -0.5], [1.5, 1.5]], tap: false });

fetch('/track')
.then(resp => resp.json())
.then(coords => {
  const poly = L.polyline(coords, { color: 'lightsteelblue', interactive: false }).addTo(map);
  map.fitBounds(poly.getBounds());
});

let junctionMarkers = [];
const junctionsReady = fetch('/junction')
.then(resp => resp.json())
.then(coords => {
  junctionMarkers = coords.map((p, index) => createJunctionMarker(p, index))
});

function toggleJunction(index) {
  fetch(`/junction/${index}/toggle`, { method: 'POST' })
  .then(_ => updateJunctions())
}

const junctionCanvasSize = 30

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
  junctionMarkers[junctionId].getElement().innerHTML = createJunctionShape(selectedBranch) + createJunctionLabel(junctionId);
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

function updateJunctions() {
  return fetch('/junctionState')
  .then(resp => resp.json())
  .then(states =>
    states.forEach((state, index) => updateJunctionOverlay(index, state))
  );
}

const carCanvasSize = 150;
const carHeight = 10;

function createCarShape(carData) {
  const lengthPx = carData.length * 6;
  const transform = `rotate(${carData.rotation - 90},0,0)`;
  const svg = carData.isLoco
  ? `<polygon points="${-lengthPx/2},-10 ${-lengthPx/2},10 ${lengthPx/2-10},10 ${lengthPx/2},0 ${lengthPx/2-10},-10" transform="${transform}" fill="goldenrod" fill-opacity="50%" stroke="goldenrod" stroke-width="1%"/>`
  : `<rect x="${-lengthPx/2}" y="-10" width="${lengthPx}" height="20" transform="${transform}" fill="magenta" fill-opacity="50%" stroke="magenta" stroke-width="1%"/>`;
  return svg;
}

function createCarLabel(carId, carData) {
  const lengthPx = carData.length * 6;
  const transform = `rotate(${carData.rotation % 180 - 90},0,0)`;
  return `<text x="${-lengthPx/2 + 10}" y="5" transform="${transform}" font-weight="bold">${carId}</text>`;
}

function createCarOverlay(carId, carData) {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('id', carId)
  svg.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
  svg.setAttribute('viewBox', `${-carCanvasSize/2} ${-carCanvasSize/2} ${carCanvasSize} ${carCanvasSize}`);
  svg.innerHTML = createCarShape(carData) + createCarLabel(carId, carData);
  return svg
}

function updateCarOverlay(carId, carData) {
  const textRotation = (carData.rotation % 180) - 90;
  const svg = document.getElementById(carId);
  svg.innerHTML = createCarShape(carData) + createCarLabel(carId, carData);
}

function getCarOverlayBounds(position) {
  const size = metersToDegrees * 12;
  return [ [ position[0] - size, position[1] - size], [position[0] + size, position[1] + size] ];
}

const carMarkers = {};
function updateCars() {
  return fetch('/car')
  .then(resp => resp.json())
  .then(cars => {
    Object.entries(cars).forEach(([carId, carData]) => {
      if (carMarkers[carId]) {
        updateCarOverlay(carId, carData);
        carMarkers[carId].setBounds(getCarOverlayBounds(carData.position));
      } else {
        // new car
        carMarkers[carId] = L.svgOverlay(
          createCarOverlay(carId, carData),
          getCarOverlayBounds(carData.position))
          .addTo(map);
      }
    });

    Object.keys(carMarkers).forEach(carId => {
      if (!cars[carId]) {
        carMarkers[carId].remove();
      }
    })
  });
}

function periodic(f, interval) {
  f().then(_ => window.setTimeout(periodic(f, interval), interval))
}

function periodicUpdate() {
  periodic(updateCars, 500);
  updateJunctions(updateJunctions, 5000);
}

junctionsReady.then(_ => periodicUpdate())