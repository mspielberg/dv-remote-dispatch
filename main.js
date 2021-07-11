const earthCircumference = 40e6;
const metersToDegrees = 360 / earthCircumference;

const map = L.map('map', { maxBounds: [[-0.5, -0.5], [1.5, 1.5]], tap: false });

fetch('/track')
.then(resp => resp.json())
.then(coords => {
  const poly = L.polyline(coords, { interactive: false }).addTo(map);
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

function createCarOverlay(carId, carData) {
  const color = carData.isLoco ? 'goldenrod' : 'magenta';
  const textRotation = ((carData.rotation + 180) % 180) - 90;
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.setAttribute('id', carId)
  svg.setAttribute('xmlns', "http://www.w3.org/2000/svg");
  svg.setAttribute('viewBox', "0 0 200 200");
  svg.innerHTML =
    `<rect x="50" y="90" width="100" height="20" transform="rotate(${carData.rotation + 90},100,100)" fill-opacity="50%" fill="${color}" stroke="${color}" stroke-width="1%"/>` +
    `<text x="60" y="105" transform="rotate(${textRotation},100,100)" font-weight="bold">${carId}</text>`;
  return svg
}

function updateCarOverlay(carId, carData) {
  const color = carData.isLoco ? 'goldenrod' : 'magenta';
  const textRotation = ((carData.rotation + 180) % 180) - 90;
  const svg = document.getElementById(carId);
  svg.innerHTML =
    `<rect x="50" y="90" width="100" height="20" transform="rotate(${carData.rotation + 90},100,100)" fill-opacity="50%" fill="${color}" stroke="${color}" stroke-width="1%"/>` +
    `<text x="60" y="105" transform="rotate(${textRotation},100,100)" font-weight="bold">${carId}</text>`;
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