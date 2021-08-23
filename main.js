const earthCircumference = 40e6;
const metersToDegrees = 360 / earthCircumference;

/////////////////////
// map

const canvasRenderer = L.canvas();
const mapBounds = [[0, 0], [0.15, 0.15]];
const maxBounds = [[-0.02, -0.02], [0.17, 0.17]];
const map = L.map('map', {
  minZoom: 13,
  maxBounds: maxBounds,
  tap: false,
})
.fitBounds(mapBounds);
L.control.scale().addTo(map);

let markerToFollow;
map.addEventListener('mousedown', stopFollowing);

function setMarkerToFollow(marker) {
  markerToFollow = marker;
  map.panTo(marker.getBounds().getCenter());
}

function stopFollowing() {
  markerToFollow = null;
}

/////////////////////
// sidebar

const sidebar = L.control.sidebar({ autopan: true, container: 'sidebar' }).addTo(map);

const tablesort = new Tablesort(document.getElementById('carList'));
const carListBody = document.getElementById('carListBody');

function createCarRow(carId, carData) {
  const row = document.createElement('tr');
  row.setAttribute('id', `carList-${carId}`);
  row.classList.add('interactive');
  carListBody.append(row);
  updateCarRow(carId, carData);
  row.addEventListener('click', _ => followCar(carId, false) );
}

function removeCarRow(carId) {
  var row = document.getElementById(`carList-${carId}`);
  if (row)
    row.remove();
}

function updateCarRow(carId, carData) {
  var row = document.getElementById(`carList-${carId}`);
  const jobId = carData.jobId ? carData.jobId.substring(3) : '';
  row.innerHTML = `<td>${carId}</td><td>${jobId}</td><td>${carData.destinationYardId || ''}</td>`;
  tablesort.refresh();
}

/////////////////////
// jobs

const CarsPerRow = 3
const jobListBody = document.getElementById('jobListBody');

// https://www.npmjs.com/package/string-hash
function stringHash(str) {
  let hash = 5381, i = str.length;
  while(i) {
    hash = (hash * 33) ^ str.charCodeAt(--i);
  }
  return hash >>> 0;
}

// http://vrl.cs.brown.edu/color
const carColors = ['#52ef99', '#c95e9f', '#b1e632', '#7574f5', '#799d10', '#fd3fbe', '#2cf52b', '#d130ff', '#21a708', '#fd2b31', '#3eeaef', '#ffc4de', '#069668', '#f9793b', '#5884c9', '#e5d75e', '#96ccfe', '#bb8801', '#6a8b7b', '#a8777c'];
function colorForJobId(jobId) {
  return carColors[stringHash(jobId) % carColors.length];
}

function jobElems(jobId, jobData) {
  const rows = [];

  let row = document.createElement('tr');
  const jobIdCell = document.createElement('th'); 
  jobIdCell.setAttribute('id', `jobList-${jobId}`);
  jobIdCell.setAttribute('colspan', CarsPerRow);
  jobIdCell.classList.add(`jobList-jobHeader`);
  jobIdCell.style.background = colorForJobId(jobId);
  jobIdCell.textContent = jobId;
  row.appendChild(jobIdCell);
  rows.push(row);

  jobData.forEach(task => {
    row = document.createElement('tr');
    const startTrackCell = document.createElement('th');
    startTrackCell.classList.add('interactive');
    startTrackCell.textContent = task.startTrack;
    startTrackCell.addEventListener('click', () => scrollToTrack(task.startTrack));
    row.appendChild(startTrackCell);

    const arrowCell = document.createElement('th');
    arrowCell.textContent = "\u279C";
    arrowCell.classList.add('jobList-trackSeparator');
    row.appendChild(arrowCell);

    const destinationTrackCell = document.createElement('th');
    destinationTrackCell.classList.add('interactive');
    destinationTrackCell.textContent = task.destinationTrack;
    destinationTrackCell.addEventListener('click', () => scrollToTrack(task.destinationTrack));
    row.appendChild(destinationTrackCell);

    for (let carIndex = 0; carIndex < task.cars.length; carIndex++) {
      if (carIndex % CarsPerRow == 0) {
        rows.push(row);
        row = document.createElement('tr');
      }
      const carId = task.cars[carIndex];
      const carCell = document.createElement('td');
      carCell.classList.add(`jobList-carCell-${carId}`);
      carCell.classList.add('interactive');
      carCell.textContent = carId;
      carCell.addEventListener('click', () => followCar(carId, false));
      row.appendChild(carCell);
    }
    if (row.children.length < CarsPerRow)
      // add filler cells
      for (let i = 0; i < CarsPerRow - (task.cars.length % CarsPerRow); i++)
        row.appendChild(document.createElement('td'));
    rows.push(row);
  });

  return rows;
}

fetch('/job')
.then(resp => resp.json())
.then(jobs => {
  for (const child of jobListBody.children)
    child.remove();
  for (const jobId in jobs)
    for (const elem of jobElems(jobId, jobs[jobId]))
      jobListBody.appendChild(elem);
});

/////////////////////
// track

let trackPolyLines = {};

function createTrackLabel(trackId, position, angle) {
  const size = 0.0002;
  const bounds = [[position[0] - size, position[1] - size], [position[0] + size, position[1] + size]];
  const rotation = `rotate(${-angle})`;

  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('id', trackId)
  svg.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
  svg.setAttribute('viewBox', '-50 -10 100 20');
  svg.innerHTML =
    `<text text-anchor="middle" dominant-baseline="central" transform="${rotation}" font-family="Arial" font-weight="bold" fill="steelblue" stroke="black" stroke-width="0.25px">${trackId.slice(trackId.indexOf('-') + 1)}</text>`;
  L.svgOverlay(svg, bounds, { renderer: canvasRenderer })
  .addTo(map)
  .setZIndex(1000);
}

function pointDistance(p1, p2) {
  const d0 = p1[0] - p2[0];
  const d1 = p1[1] - p2[1];
  return Math.sqrt(d0 * d0 + d1 * d1);
}

function pointLerp(p1, p2, a) {
  return [
    (p2[0] - p1[0]) * a + p1[0],
    (p2[1] - p1[1]) * a + p1[1]
  ];
}

function createLocation(start, end, mid, a) {
  return [
    (end[0] - start[0]) * a + mid[0],
    (end[1] - start[1]) * a + mid[1]
  ];
}

function createTrackLabels(trackId, coords) {
  const length = pointDistance(coords[0], coords[coords.length - 1]);
  const midIndex = Math.floor(coords.length / 2); 
  const beforeMid = (midIndex % 2 == 1) ? coords[midIndex] : coords[midIndex - 1];
  const mid = (midIndex % 2 == 1) ? coords[midIndex] : pointLerp(coords[midIndex - 1], coords[midIndex], 0.5);
  const afterMid = (midIndex % 2 == 1) ? coords[midIndex + 1] : coords[midIndex];
  const midGap = pointDistance(beforeMid, afterMid);

  const angle = ((Math.atan2(afterMid[0] - beforeMid[0], afterMid[1] - beforeMid[1]) * 180 / Math.PI) + 270) % 180 - 90;

  if (coords.length > 5) {
    createTrackLabel(trackId, createLocation(beforeMid, afterMid, mid, length / midGap *  0.3), angle);
    createTrackLabel(trackId, createLocation(beforeMid, afterMid, mid, length / midGap * -0.3), angle);
  } else {
    createTrackLabel(trackId, mid, angle);
  }
}

const tracksReady = fetch('/track')
.then(resp => resp.json())
.then(tracks => {
  Object.entries(tracks).forEach(([trackId, coords]) => {
    const isSiding = !trackId.includes('#');
    const polyline = L.polyline(coords, {
      color: isSiding ? 'slategray' : 'lightsteelblue',
      interactive: false,
      renderer: canvasRenderer,
    }).addTo(map);
    trackPolyLines[trackId] = polyline;
    if (isSiding)
      createTrackLabels(trackId, coords)
  });
});

/////////////////////
// junctions

let junctions = [];
const junctionsReady = tracksReady
.then(_ => fetch('/junction'))
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
  return `<g opacity="70%"><rect x="${-junctionCanvasSize/2}" y="${-junctionCanvasSize}" width="${junctionCanvasSize}" height="${junctionCanvasSize*2}" fill="red"/>` +
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
  svg.setAttribute('id', `J-${junctionId}`)
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
    .setStyle({ color: 'lightsteelblue', dashArray: "6 12" })
    .bringToBack();
}

function getJunctionOverlayBounds(position) {
  const size = metersToDegrees * 5;
  return [ [ position[0] - size, position[1] - size/2], [position[0] + size, position[1] + size/2] ];
}

function createJunctionMarker(p, junctionId) {
  return L.svgOverlay(
    createJunctionOverlay(junctionId),
    getJunctionOverlayBounds(p),
    { interactive: true, renderer: canvasRenderer })
    .addEventListener('click', () => toggleJunction(junctionId) )
    .addTo(map)
    .setZIndex(Math.floor(p[0] * 100000 + p[1] * 100000));
}

function updateAllJunctions() {
  return fetch('/junctionState')
  .then(resp => resp.json())
  .then(states =>
    states.forEach((state, index) => updateJunctionOverlay(index, state))
  );
}

/////////////////////
// following

function followCar(carId, shouldScroll) {
  setMarkerToFollow(carMarkers[carId]);

  for (const row of carListBody.querySelectorAll('.following'))
    row.classList.remove('following');
  const carListRow = document.getElementById(`carList-${carId}`)
  carListRow.classList.add('following');
  if (shouldScroll)
    carListRow.scrollIntoView({ block: 'center' });

  for (const elem of jobListBody.querySelectorAll('.following'))
    elem.classList.remove('following');
  const jobListElems = jobListBody.querySelectorAll(`.jobList-carCell-${carId}`);
  for (const elem of jobListElems)
    elem.classList.add('following');
  if (shouldScroll)
    jobListElems[0].scrollIntoView({ block: 'center' });
}

/////////////////////
// player

const playerCanvasSize = 20;
let playerMarker;

function getPlayerOverlayBounds(position) {
  const size = metersToDegrees * 2;
  return [ [ position[0] - size, position[1] - size], [position[0] + size, position[1] + size] ];
}

function updatePlayerOverlay(playerData) {
  const polygonElem = document.getElementById("playerPolygon");
  polygonElem.setAttribute('transform', `rotate(${playerData.rotation})`);
  playerMarker.setBounds(getPlayerOverlayBounds(playerData.position));
}

function createPlayerOverlay() {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('id', 'player');
  svg.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
  svg.setAttribute('viewBox', `${-playerCanvasSize/2*1.5} ${-playerCanvasSize/2*1.5} ${playerCanvasSize*1.5} ${playerCanvasSize*1.5}`);
  svg.innerHTML = '<polygon id="playerPolygon" fill="aqua" fill-opacity="70%" stroke="black" stroke-width="1%" points="0,-10 10,10 0,5 -10,10"/>';
  return svg;
}

function createPlayerMarker(playerData) {
  playerMarker = L.svgOverlay(
    createPlayerOverlay(),
    getPlayerOverlayBounds(playerData.position),
    { interactive: true, bubblingMouseEvents: false })
    .addEventListener('click', e => setMarkerToFollow(e.target))
    .addTo(map);
  updatePlayerOverlay(playerData);
}

function scrollToTrack(trackId) {
  stopFollowing();
  const polyLine = trackPolyLines[trackId];
  if (polyLine)
    map.panTo(polyLine.getCenter());
}

fetch('/player')
.then(resp => resp.json())
.then(playerData => {
  createPlayerMarker(playerData);
  map
  .setZoom(20)
  .panTo(playerData.position)
});

/////////////////////
// cars

const carWidthMeters = 3;
const carWidthPx = 20;
const svgPixelsPerMeter = carWidthPx / 3;

function createCarShape(carData) {
  const color = carData.jobId ? colorForJobId(carData.jobId) : 'gray';
  const lengthPx = carData.length * svgPixelsPerMeter;
  const svg = carData.isLoco
  ? `<polygon points="${-lengthPx/2},-${carWidthPx/2} ${-lengthPx/2},${carWidthPx/2} ${lengthPx/2-5},${carWidthPx/2} ${lengthPx/2},0 ${lengthPx/2-5},-${carWidthPx/2}" fill="goldenrod" fill-opacity="70%" stroke="black" stroke-width="1%"/>`
  : `<rect x="${-lengthPx/2}" y="-10" width="${lengthPx}" height="20" fill="${color}" fill-opacity="70%" stroke="black" stroke-width="1%"/>`;
  return svg;
}

function createCarLabel(carId, carData) {
  const lengthPx = carData.length * svgPixelsPerMeter;
  const rotation = carData.rotation >= 180 ? 'rotate(180)' : '';
  if (carData.isLoco)
    return `<text x="${-lengthPx/2 + 5}" transform="${rotation}" dominant-baseline="central" font-size="12" font-weight="bold">${carId}</text>`;
  const jobIdLabel = carData.jobId ? `<text x="${-lengthPx/2 + 5}" transform="${rotation}" dominant-baseline="central" font-size="16">${carData.jobId.slice(-5,-3)}${carData.jobId.slice(-2)}</text>` : "";
  const carIdLabel =
    `<text y="-0.5em" y="1" transform="${rotation} translate(${lengthPx/2 - 5})" dominant-baseline="central" text-anchor="end" font-size="8" font-family="monospace" font-weight="bold">` +
      `<tspan x="0">${carId.slice(0,3)}</tspan>` +
      `<tspan x="0" dy="1em">${carId.slice(3)}</tspan>` +
    '</text>';
  return jobIdLabel + carIdLabel;
}

function createCarOverlay(carId, carData) {
  const lengthPx = carData.length * svgPixelsPerMeter;
  const carCanvasMajor = Math.sqrt(lengthPx / 2 * lengthPx / 2 + carWidthPx / 2 * carWidthPx / 2);
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('id', carId);
  svg.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
  svg.setAttribute('viewBox', `${-carCanvasMajor} ${-carWidthPx/2} ${carCanvasMajor*2} ${carWidthPx}`);
  return svg
}

function updateCarOverlay(carId, carData) {
  const marker = carMarkers[carId];
  marker.setRotationAngle(carData.rotation - 90);
  marker.getElement().innerHTML = createCarShape(carData) + createCarLabel(carId, carData);
}

function getCarOverlayBounds(carData) {
  const position = carData.position;
  const length = metersToDegrees * carData.length;
  const width = metersToDegrees * carWidthMeters;
  return [ [ position[0] - width/2, position[1] - length/2], [position[0] + width/2, position[1] + length/2] ];
}

const carMarkers = new Map();
const carJobIds = new Map();

function createNewCar(carId, carData) {
  // new car
  createCarRow(carId, carData);
  carJobIds[carId] = carData.jobId;
  carMarkers[carId] = L.svgOverlay(
    createCarOverlay(carId, carData),
    getCarOverlayBounds(carData),
    { interactive: true, bubblingMouseEvents: false })
    .addEventListener('mouseup', e => followCar(carId, true))
    .addTo(map);
  updateCarOverlay(carId, carData);
}

function updateCar(carId, carData) {
  updateCarOverlay(carId, carData);
  if (carData.jobId !== carJobIds[carId])
    updateCarRow(carId, carData);
  carMarkers[carId].setBounds(getCarOverlayBounds(carData));
}

function removeCar(carId) {
  removeCarRow(carId);
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

/////////////////////
// events

function uuidv4() {
  return ([1e7]+-1e3+-4e3+-8e3+-1e11).replace(/[018]/g, c =>
    (c ^ crypto.getRandomValues(new Uint8Array(1))[0] & 15 >> c / 4).toString(16)
  );
}

function handleEvent(e) {
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
  case "playerUpdate":
    updatePlayerOverlay(msg);
    break;
  }
  if (markerToFollow)
    map.panTo(markerToFollow.getBounds().getCenter());
}

function subscribeForEvents() {
  const events = new EventSource(`/eventSource?${uuidv4()}`)
  let zoomActive = false;
  const queuedEvents = [];

  map.addEventListener('zoomstart', _ => zoomActive = true);
  map.addEventListener('zoomend', _ => {
    zoomActive = false;
    queuedEvents.forEach(handleEvent);
    queuedEvents.length = 0;
  });
  events.onerror = function(e) {
    console.error(e)
  }
  events.onmessage = function(e) {
    if (zoomActive)
      queuedEvents.push(e);
    else
      handleEvent(e);
  };
}

junctionsReady.then(_ => {
  updateAllCars();
  updateAllJunctions();
  subscribeForEvents();
});
