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

function createJunctionMarker(p, index) {
  return L.circle(p, { radius: 3, fillOpacity: 0.5 })
    .addEventListener('click', () => toggleJunction(index) )
    .addTo(map);
}

function updateJunctions() {
  return fetch('/junctionState')
  .then(resp => resp.json())
  .then(states =>
    states.forEach((state, index) =>
      junctionMarkers[index].setStyle({ color: state == 0 ? 'blue' : 'orange' })
    )
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
  const textRotation = (carData.rotation % 180) - 90;
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

function periodicUpdate() {
  updateCars();
  updateJunctions()
  .then(_ => window.setTimeout(periodicUpdate, 1000));
}

junctionsReady.then(_ => periodicUpdate())