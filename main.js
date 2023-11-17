const initialZoom = 20;
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
  zoomControl: false,
})
.fitBounds(mapBounds);
L.control.scale().addTo(map);
const zoomHome = new L.Control.ZoomHome({
  position: 'topleft',
  zoomInText: '<i class="fas fa-search-plus"></i>',
  zoomHomeText: '<i class="fas fa-user"></i>',
  zoomHomeTitle: 'Zoom to player(s)',
  zoomOutText: '<i class="fas fa-search-minus"></i>',
}).addTo(map);

let markerToFollow;
map.addEventListener('mousedown', stopFollowing);

function setMarkerToFollow(marker) {
  markerToFollow = marker;
  map.panTo(marker.getBounds().getCenter());
}

function stopFollowing() {
  markerToFollow = null;
}

function zoomToAllPlayers() {
  const bounds = new L.LatLngBounds();
  playerMarkers.forEach(marker => bounds.extend(marker.getBounds()));
  map.fitBounds(bounds, { maxZoom: initialZoom });
}

map.addEventListener('zoomhome', () => {
  stopFollowing();
  zoomToAllPlayers();
});

/////////////////////
// settings

document.getElementById('themeDropdown')
  .addEventListener('input', e => {
    if (e.target.value === 'dark') {
      document.getElementById('map').classList.add('dark');
    } else {
      document.getElementById('map').classList.remove('dark');
    }
  });

function getCarColorMode() {
  return document.getElementById('carColorDropdown').value;
}

document.getElementById('carColorDropdown')
  .addEventListener('input', () => {
    updateAllCarColors();
    updateJobListColors();
  });

/////////////////////
// sidebar

const sidebar = L.control.sidebar({ autopan: true, container: 'sidebar' }).addTo(map);

const tablesort = new Tablesort(document.getElementById('carList'));
const carListBody = document.getElementById('carListBody');

function createCarRow(carId) {
  const row = document.createElement('tr');
  row.setAttribute('id', `carList-${carId}`);
  row.classList.add('interactive');
  carListBody.append(row);
  updateCarRow(carId);
  row.addEventListener('click', _ => followCar(carId, false) );
}

function removeCarRow(carId) {
  const row = document.getElementById(`carList-${carId}`);
  if (row)
    row.remove();
}

function updateCarRow(carId) {
  const row = document.getElementById(`carList-${carId}`);
  if (!row)
    return;
  const jobId = carJobIds.has(carId) ? carJobIds.get(carId) : '';
  const destinationYardId = allJobData.has(jobId) ? allJobData.get(jobId).destinationYardId : '';
  row.innerHTML = `<td>${carId}</td><td>${jobId}</td><td>${destinationYardId}</td>`;
  tablesort.refresh();
}

/////////////////////
// jobs

const CarsPerRow = 3;
const allJobData = new Map();
const carJobIds = new Map();
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
const carColors = [
  '#52ef99', '#c95e9f', '#b1e632', '#7574f5', '#799d10', '#fd3fbe', '#2cf52b', '#d130ff', '#21a708', '#fd2b31',
  '#3eeaef', '#ffc4de', '#069668', '#f9793b', '#5884c9', '#e5d75e', '#96ccfe', '#bb8801', '#6a8b7b', '#a8777c',
];

function colorByHashing(str) {
  return carColors[stringHash(str) % carColors.length];
}

function colorForJobDestination(jobId) {
  const jobData = allJobData.get(jobId);
  if (!jobData)
    return 'gray';
  return colorForYardId(jobData.destinationYardId);
}

function colorForJobType(jobId) {
  const segments = jobId.split('-');
  if (segments.length == 2)
    return 'cornflowerblue';
  const jobType = segments[1];
  switch (jobType) {
  case 'FH': return 'lightgreen';
  case 'LH': return 'khaki';
  case 'PC':
  case 'PE': return 'cornflowerblue';
  case 'SL':
  case 'SU': return 'lightcoral';
  }
}

function colorForJobId(jobId) {
  switch (getCarColorMode()) {
    case 'jobId': return colorByHashing(jobId);
    case 'carType':
    case 'jobType': return colorForJobType(jobId);
    case 'destination': return colorForJobDestination(jobId);
  }
}

function yardIdForTrack(trackId) {
  return trackId.split('-')[0];
}

function jobMatchesFilter(jobId, jobData) {
    const testText = document.getElementById('jobSearchText').value.toUpperCase();
    const activeOnly = document.getElementById('jobActiveOnly').checked;
  function taskFields(task) { return [task.startTrack, task.destinationTrack].concat(task.cars); }
  const fields = [jobId].concat(jobData.tasks.flatMap(taskFields));
  return fields.some(field => field.includes(testText)) && (!activeOnly || jobData.isActive);
}

function jobElem(jobId, jobData) {
  function replaceHyphens(s) { return s.replaceAll('-', '\u2011'); }

  const tbody = document.createElement('tbody');
  tbody.setAttribute('id', `jobList-${jobId}`);

  let row = document.createElement('tr');
  const jobIdCell = document.createElement('th'); 
  jobIdCell.setAttribute('colspan', CarsPerRow);
  jobIdCell.classList.add("jobList-jobHeader");
  jobIdCell.style.background = colorForJobId(jobId);
  jobIdCell.textContent = jobId;

  jobLicensesDiv = document.createElement('div');
  jobLicensesDiv.classList.add('jobList-licenses');
  for (const license of jobData.requiredLicenses) {
      jobLicensesDiv.innerHTML += `<span class="jobList-license"><div class="jobList-licenseBackground"></div><img src="res/licenses.${license}.png" title="${license}"></span>`;
  }
  jobIdCell.appendChild(jobLicensesDiv);

  row.appendChild(jobIdCell);
  tbody.appendChild(row);

  row = document.createElement('tr');
  jobMassCell = document.createElement('th');
  jobMassCell.textContent = `${jobData.mass.toFixed(0)} t`;
  jobLengthCell = document.createElement('th');
  jobLengthCell.textContent = `${jobData.length.toFixed(0)} m`;
  jobPaymentCell = document.createElement('th');
  jobPaymentCell.textContent =
    new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD', maximumFractionDigits: 0 })
    .format(jobData.basePayment);
  row.append(jobMassCell, jobLengthCell, jobPaymentCell);
  tbody.appendChild(row);

  jobData.tasks.forEach(task => {
    row = document.createElement('tr');
    const startTrackCell = document.createElement('th');
    startTrackCell.classList.add('interactive');
    startTrackCell.textContent = replaceHyphens(task.startTrack);
    startTrackCell.style.background = colorForYardId(yardIdForTrack(task.startTrack));
    startTrackCell.addEventListener('click', () => scrollToTrack(task.startTrack));
    row.appendChild(startTrackCell);

    const arrowCell = document.createElement('th');
    arrowCell.textContent = "\u279C";
    arrowCell.classList.add('jobList-trackSeparator');
    row.appendChild(arrowCell);

    const destinationTrackCell = document.createElement('th');
    destinationTrackCell.classList.add('interactive');
    destinationTrackCell.textContent = replaceHyphens(task.destinationTrack);
    destinationTrackCell.style.background = colorForYardId(yardIdForTrack(task.destinationTrack));
    destinationTrackCell.addEventListener('click', () => scrollToTrack(task.destinationTrack));
    row.appendChild(destinationTrackCell);

    for (let carIndex = 0; carIndex < task.cars.length; carIndex++) {
      if (carIndex % CarsPerRow == 0) {
        tbody.appendChild(row);
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
    tbody.appendChild(row);
  });

  return tbody;
}

function updateCarJobs() {
  carJobIds.clear();
  allJobData.forEach((jobData, jobId) => {
    jobData.tasks.forEach(task => {
      task.cars.forEach(carId => {
        carJobIds.set(carId, jobId);
      });
    })
  });
  for ([carId, _] of allCarData) {
    updateCarRow(carId);
    updateCarMarker(carId);
  }
}

function updateJobListColors() {
  for (const elem of jobListBody.querySelectorAll('th.jobList-jobHeader')) {
    elem.style.background = colorForJobId(elem.textContent);
  }
}

function updateJobList() {
  for (const elem of Array.from(jobListBody.childNodes))
    elem.remove();
  const sortedJobs = Array.from(allJobData.entries()).sort((a, b) => a[0].localeCompare(b[0]));
  sortedJobs
    .filter(([jobId, jobData]) => jobMatchesFilter(jobId, jobData))
    .forEach(([jobId, jobData]) => jobListBody.appendChild(jobElem(jobId, jobData)));
}

function updateAllJobs(jobs) {
  allJobData.clear();
  Object.entries(jobs).forEach(([jobId, jobData]) => allJobData.set(jobId, jobData));
  updateJobList();
  updateCarJobs();
}

let jobSearchTimeoutId = null;
function queueJobUpdate() {
    if (jobSearchTimeoutId)
        clearTimeout(jobSearchTimeoutId);
    jobSearchTimeoutId = setTimeout(updateJobList, 100);
}
document.getElementById('jobSearchText').addEventListener('input', e => {
    queueJobUpdate();
});
document.getElementById('jobActiveOnly').addEventListener('change', e => {
    queueJobUpdate();
})

/////////////////////
// track

const trackPolyLines = new Map();

function colorForYardId(yardId) {
  switch (yardId) {
  case 'SM'  : return '#9899a3';
  case 'FM'  : return '#ffcb69';
  case 'FF'  : return '#9ac9f9';
  case 'GF'  : return '#f09ebb';
  case 'CSW' : return '#c7c1b7';
  case 'HB'  :
  case 'HMB' :
    return '#9b7fa0';
  case 'MF'  :
  case 'MFMB':
    return '#ffa96e';
  case 'CM'  : return '#807b73';
  case 'IME' : return '#d97f73';
  case 'IMW' : return '#b76e59';
  case 'FRC' : return '#afd57b';
  case 'FRS' : return '#7caa6f';
  case 'SW'  : return '#f6ce9f';
  case 'OWN' : return '#786f61';
  case 'OWC' : return '#6c6c6f';
  case 'MB'  : return '#b6a46f';
  }
}

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

const tracksReady = fetch(new URL('/track', location))
.then(resp => resp.json())
.then(tracks => {
  Object.entries(tracks).forEach(([trackId, coords]) => {
    const isSiding = !trackId.includes('#');
    const polyline = L.polyline(coords, {
      color: isSiding ? 'slategray' : 'lightsteelblue',
      interactive: false,
      renderer: canvasRenderer,
    }).addTo(map);
    trackPolyLines.set(trackId, polyline);
    if (isSiding)
      createTrackLabels(trackId, coords)
  });
});

/////////////////////
// junctions

let junctions = [];
const junctionsReady = tracksReady
.then(_ => fetch(new URL('/junction', location)))
.then(resp => resp.json())
.then(allJunctionData =>
  junctions = allJunctionData.map((data, index) => ({
    marker: createJunctionMarker(data.position, index),
    branches: data.branches,
  }))
);

function toggleJunction(junctionId) {
  fetch(new URL(`/junction/${junctionId}/toggle`, location), { method: 'POST' })
  .then(resp => resp.json())
  .then(selectedBranch => updateJunctionOverlay(junctionId, selectedBranch))
  .catch(err => {});
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
  const selectedTrackId = junction.branches[selectedBranch]
  trackPolyLines.get(selectedTrackId).setStyle({ color: 'steelblue', dashArray: null });
  const unselectedTrackPolyLine = trackPolyLines.get(junction.branches[1-selectedBranch]);
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

function updateAllJunctions(states) {
  states.forEach((state, index) => updateJunctionOverlay(index, state))
}

/////////////////////
// following

function followCar(carId, shouldScroll) {
  setMarkerToFollow(carMarkers.get(carId));

  for (const row of carListBody.querySelectorAll('.following'))
    row.classList.remove('following');
  const carListRow = document.getElementById(`carList-${carId}`)
  carListRow.classList.add('following');
  if (shouldScroll)
    carListRow.scrollIntoView({ block: 'center' });

  for (const elem of jobListBody.querySelectorAll('.following'))
    elem.classList.remove('following');
  const jobListElems = jobListBody.querySelectorAll(`.jobList-carCell-${carId}`);
  for (const elem of jobListElems) {
    elem.classList.add('following');
    elem.closest('tbody').classList.add('following');
  }
  if (shouldScroll && jobListElems.length > 0)
    jobListElems[0].scrollIntoView({ block: 'center' });
}

/////////////////////
// player

const playerMarkers = new Map();

function getPlayerOverlayBounds(position) {
  const size = metersToDegrees * 2;
  return [ [ position[0] - size, position[1] - size], [position[0] + size, position[1] + size] ];
}

function updatePlayerOverlays(data) {
  const existingPlayerIds = Array.from(playerMarkers.keys());
  // Remove markers from disconnected players
  existingPlayerIds
  .filter(id => !data.hasOwnProperty(id))
  .forEach(id => {
    removePlayerOverlay(id);
  });
  // Add markers for new players
  Object.entries(data)
  .filter(([id]) => !existingPlayerIds.includes(id))
  .forEach(([id, playerData]) => {
    createPlayerMarker(id, playerData);
  });
  Object.entries(data).forEach(([id, playerData]) => {
    const polygonElem = document.getElementById(`playerPolygon-${id}`);
    polygonElem.setAttribute('transform', `rotate(${playerData.rotation})`);
    playerMarkers.get(id).setBounds(getPlayerOverlayBounds(playerData.position));
  });
}

function removePlayerOverlay(id) {
  document.getElementById(`playerPolygon-${id}`)?.remove();
  playerMarkers.delete(id);
}

function createPlayerOverlay(id, playerData) {
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.setAttribute('viewBox', '-10 -10 20 20');
  const polygon = document.createElementNS(svg.namespaceURI, 'polygon');
  polygon.setAttribute('id', `playerPolygon-${id}`);
  polygon.setAttribute('fill', playerData.color);
  polygon.setAttribute('fill-opacity', '70%');
  polygon.setAttribute('stroke', 'black');
  polygon.setAttribute('stroke-width', '1%');
  polygon.setAttribute('points', '0,-10 10,10 0,5 -10,10');
  svg.appendChild(polygon);
  return svg;
}

function createPlayerMarker(id, playerData) {
  playerMarkers.set(id, L.svgOverlay(
    createPlayerOverlay(id, playerData),
    getPlayerOverlayBounds(playerData.position),
    { interactive: true, bubblingMouseEvents: false })
    .addEventListener('click', e => setMarkerToFollow(e.target))
    .addTo(map));
}

function scrollToTrack(trackId) {
  stopFollowing();
  const polyLine = trackPolyLines.get(trackId);
  if (polyLine)
    map.panTo(polyLine.getCenter());
}

fetch(new URL('/player', location))
.then(resp => resp.json())
.then(data => {
  updatePlayerOverlays(data);
  zoomToAllPlayers();
});

/////////////////////
// loco control

const locoIdSelect = document.getElementById('locoControlLocoId');
function updateLocoList() {
  for (const elem of Array.from(locoIdSelect.children))
    elem.remove();
  const locoIds = Array.from(allCarData.entries())
    .filter(([_, carData]) => carData.canBeControlled)
    .map(([id, _]) => id.slice(2));
  locoIds.sort();
  for (const id of locoIds) {
    const option = document.createElement('option');
    option.textContent = id;
    locoIdSelect.appendChild(option);
  }
}

function isReverserButtonActive(faButton) {
  return faButton.querySelector('svg').getAttribute('data-prefix') == 'fas';
}

function updateReverserButtons(reverser) {
  const reverseButton = document.querySelector('#locoControlReverserReverseButton svg');
  const newReverseStyle = reverser < 0.5 ? 'fas' : 'far';
  if (reverseButton.getAttribute('data-prefix') != newReverseStyle)
    reverseButton.setAttribute('data-prefix', newReverseStyle);

  const forwardButton = document.querySelector('#locoControlReverserForwardButton svg');
  const newForwardStyle = reverser > 0.5 ? 'fas' : 'far';
  if (forwardButton.getAttribute('data-prefix') != newForwardStyle)
    forwardButton.setAttribute('data-prefix', newForwardStyle);
}

const locoBrakePipeDisplay = document.getElementById('locoControlBrakePipe');
const locoSpeedDisplay = document.getElementById('locoControlForwardSpeed');
const locoTrainBrakeInput = document.getElementById('locoControlTrainBrakeInput');
const locoIndependentBrakeInput = document.getElementById('locoControlIndependentBrakeInput');
const locoReverserReverseButton = document.getElementById('locoControlReverserReverseButton');
const locoReverserForwardButton = document.getElementById('locoControlReverserForwardButton');
const locoThrottleInput = document.getElementById('locoControlThrottleInput');
const locoControlCoupleButton = document.getElementById('locoControlCoupleButton');
const locoControlUncoupleButton = document.getElementById('locoControlUncoupleButton');
const locoControlUncoupleSelect = document.getElementById('locoControlUncoupleSelect');

function updateCouplingControls(carData) {
  const canCouple = carData.canCouple;
  const carsInFront = carData.carsInFront;
  const carsInRear = carData.carsInRear;

  locoControlCoupleButton.disabled = !canCouple;
  locoControlUncoupleButton.disabled = carsInFront == 0 && carsInRear && 0;

  if (locoControlUncoupleSelect.childElementCount == carsInFront + carsInRear) {
    return;
  }

  const options = [];
  for (let i = carsInFront; i >= 1; i--)
    options.push(i);
  for (let i = 1; i <= carsInRear; i++)
    options.push(-i);
  locoControlUncoupleSelect.replaceChildren(...options.map(i => {
    const option = document.createElement('option');
    option.setAttribute('value', i);
    option.textContent = i >= 0 ? `\u002b${i}` : `\u2212${-i}`;
    return option;
  }));
}

function getControlledLocoId() {
  return `L-${locoIdSelect.value}`;
}

function getControlledLocoData() {
  return allCarData.get(getControlledLocoId());
}

let locoTrainBrakeEditing = false;
let locoIndependentBrakeEditing = false;
let locoThrottleEditing = false;

function updateLocoTrainBrakeInput(carData) {
  if (locoTrainBrakeEditing)
    return;
  if (!carData)
    carData = getControlledLocoData();
  locoTrainBrakeInput.value = carData.trainBrake * 100;
}

function updateLocoIndependentBrakeInput(carData) {
  if (locoIndependentBrakeEditing)
    return;
  if (!carData)
    carData = getControlledLocoData();
  locoIndependentBrakeInput.value = carData.independentBrake * 100;
}

function updateLocoThrottleInput(carData) {
  if (locoThrottleEditing)
    return;
  if (!carData)
    carData = getControlledLocoData();
  locoThrottleInput.value = carData.throttle * 100;
}

function updateLocoDisplay() {
  const carData = getControlledLocoData();
  if (carData) {
    locoBrakePipeDisplay.textContent = carData.brakePipe.toFixed(1);
    locoSpeedDisplay.textContent = carData.forwardSpeed.toFixed(0);
    updateLocoTrainBrakeInput(carData);
    updateLocoIndependentBrakeInput(carData);
    updateReverserButtons(carData.reverser);
    updateLocoThrottleInput(carData);
    updateCouplingControls(carData);
  }
}

locoIdSelect.addEventListener('change', updateLocoDisplay);
setInterval(updateLocoDisplay, 1000 / 9);

function sendLocoCommand(command) {
  const locoId = `L-${locoIdSelect.value}`;
  if (allCarData.has(locoId)) {
    fetch(new URL(`/car/${locoId}/control?${command}`, location), { method: 'POST' });
  }
}

function rangeCommandSender(parameter) {
  return e => sendLocoCommand(`${parameter}=${e.target.value / 100}`);
}

locoTrainBrakeInput.addEventListener('input', rangeCommandSender('trainBrake'));
locoIndependentBrakeInput.addEventListener('input', rangeCommandSender('independentBrake'));
locoReverserReverseButton.addEventListener('click', e =>
  sendLocoCommand(`reverser=${isReverserButtonActive(locoReverserReverseButton) ? 0.5 : 0}`));
locoReverserForwardButton.addEventListener('click', e =>
  sendLocoCommand(`reverser=${isReverserButtonActive(locoReverserForwardButton) ? 0.5 : 1}`));
locoThrottleInput.addEventListener('input', rangeCommandSender('throttle'));
locoControlCoupleButton.addEventListener('click', e =>
  sendLocoCommand('couple=0'));
locoControlUncoupleButton.addEventListener('click', e =>
  sendLocoCommand(`uncouple=${locoControlUncoupleSelect.value}`));

locoTrainBrakeInput.addEventListener("mousedown", () => locoTrainBrakeEditing = true);
locoTrainBrakeInput.addEventListener("mouseup", () => {
  locoTrainBrakeEditing = false;
  updateLocoTrainBrakeInput();
});
locoIndependentBrakeInput.addEventListener("mousedown", () => locoIndependentBrakeEditing = true);
locoIndependentBrakeInput.addEventListener("mouseup", () => {
  locoIndependentBrakeEditing = false;
  updateLocoIndependentBrakeInput();
});
locoThrottleInput.addEventListener("mousedown", () => locoThrottleEditing = true);
locoThrottleInput.addEventListener("mouseup", () => {
  locoThrottleEditing = false;
  updateLocoThrottleInput();
});


/////////////////////
// cars

const carWidthMeters = 3;
const carWidthPx = 20;
const svgPixelsPerMeter = carWidthPx / 3;

const allCarData = new Map();
const carMarkers = new Map();

function getCarColor(carId) {
  const jobId = carJobIds.get(carId);

  switch (getCarColorMode()) {
  case 'jobId':
    return jobId ? colorByHashing(jobId) : 'gray';
  case 'jobType':
    return jobId ? colorForJobType(jobId) : 'gray';
  case 'destination':
    return jobId ? colorForJobDestination(jobId) : 'gray';
  case 'carType':
    return colorByHashing(carId.slice(0,3));
  }
}

function updateCarColor(carId) {
  const carMarker = carMarkers.get(carId);
  const rect = carMarker.getElement().querySelector('rect');
  if (rect)
    rect.setAttribute('fill', getCarColor(carId));
}

function updateAllCarColors() {
  carMarkers.forEach((_, carId) => updateCarColor(carId));
}

const locoShapeNoseDepth = 10;

function createCarShape(carId, carData) {
  const isLoco = carId.slice(0,2) == 'L-';
  const lengthPx = carData.length * svgPixelsPerMeter;
  const svg = isLoco
    ? `<polygon points="${-lengthPx/2},-${carWidthPx/2} ${-lengthPx/2},${carWidthPx/2} ${lengthPx/2-locoShapeNoseDepth},${carWidthPx/2} ${lengthPx/2},0 ${lengthPx/2-locoShapeNoseDepth},-${carWidthPx/2}" fill="goldenrod" fill-opacity="70%" stroke="black" stroke-width="1%"/>`
    : `<rect x="${-lengthPx/2}" y="-10" width="${lengthPx}" height="20" fill-opacity="70%" stroke="black" stroke-width="1%"/>`;
  return svg;
}

function createCarLabel(carId, carData) {
  const isLoco = carId.slice(0,2) == 'L-';
  const jobId = carJobIds.get(carId);
  const lengthPx = carData.length * svgPixelsPerMeter;
  const rotation = carData.rotation >= 180 ? 'rotate(180)' : '';
  if (isLoco)
    return `<text transform="translate(-3 0) ${rotation}" text-anchor="middle" dominant-baseline="central" font-size="12" font-weight="bold">${carId}</text>`;
  const jobIdLabel =
    !jobId ? ""
    : jobId.split('-').length == 3 ? jobId.slice(-5,-3) + jobId.slice(-2)
    : jobId.split('-').join('');
  const jobIdText = `<text x="${-lengthPx/2 + 5}" transform="${rotation}" dominant-baseline="central" font-size="16">${jobIdLabel}</text>`
  const carIdText =
    `<text y="-0.5em" y="1" transform="${rotation} translate(${lengthPx/2 - 5})" dominant-baseline="central" text-anchor="end" font-size="8" font-family="monospace" font-weight="bold">` +
      `<tspan x="0">${carId.slice(0,-3).replaceAll('-', '')}</tspan>` +
      `<tspan x="0" dy="1em">${carId.slice(-3)}</tspan>` +
    '</text>';
  return jobIdText + carIdText;
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

function updateCarMarker(carId) {
  const marker = carMarkers.get(carId);
  if (!marker)
    return;
  const carData = allCarData.get(carId);
  marker.setBounds(getCarOverlayBounds(carData));
  marker.setRotationAngle(carData.rotation - 90);
  marker.getElement().innerHTML = createCarShape(carId, carData) + createCarLabel(carId, carData);
  updateCarColor(carId);
}

function getCarOverlayBounds(carData) {
  const position = carData.position;
  const length = metersToDegrees * carData.length;
  const width = metersToDegrees * carWidthMeters;
  return [ [ position[0] - width/2, position[1] - length/2], [position[0] + width/2, position[1] + length/2] ];
}

function createNewCar(carId, carData) {
  allCarData.set(carId, carData);
  createCarRow(carId);
  const overlay = L.svgOverlay(
    createCarOverlay(carId, carData),
    getCarOverlayBounds(carData),
    { interactive: true, bubblingMouseEvents: false })
    .addEventListener('mouseup', e => followCar(carId, true))
    .addTo(map);
  carMarkers.set(carId, overlay);
  updateCarMarker(carId);
}

function updateCar(carId, carData) {
  allCarData.set(carId, carData);
  updateCarRow(carId);
  updateCarMarker(carId);
}

function removeCar(carId) {
  removeCarRow(carId);
  const marker = carMarkers.get(carId);
  if (marker) {
    marker.remove();
    carMarkers.delete(carId);
  }
  allCarData.delete(carId);
}

function updateAllCars(updateCarData) {
  Object.entries(updateCarData).forEach(([carId, carData]) => {
    if (!carMarkers.has(carId))
      createNewCar(carId, carData);
    else
      updateCar(carId, carData);
  });
  for ([carId, _] of carMarkers)
    if (!updateCarData[carId])
      removeCar(carId);
  updateLocoList();
}

function updateCars(cars) {
  Object.entries(cars).forEach(([carId, carData]) =>
    updateCar(carId, carData));
}

/////////////////////
// events

function uuidv4() {
  return ([1e7]+-1e3+-4e3+-8e3+-1e11).replace(/[018]/g, c =>
    (c ^ crypto.getRandomValues(new Uint8Array(1))[0] & 15 >> c / 4).toString(16)
  );
}
const sessionId = uuidv4();
const updateInterval = 100;
let updateStart;

function updateOnce() {
  updateStart = performance.now();
  return fetch(new URL(`/updates/${sessionId}`, location))
  .then(resp => resp.json())
  .then(updateData => {
    Object.entries(updateData).forEach(([tag, data]) => {
      switch (tag) {
      case 'cars':
        updateAllCars(data);
        break;
      case 'jobs':
        updateAllJobs(data);
        break;
      case 'junctions':
        updateAllJunctions(data);
        break;
      case 'player':
        updatePlayerOverlays(data);
        break;
      default:
        const segments = tag.split('-');
        switch (segments[0]) {
        case 'trainset': updateCars(data); break;
        case 'carguid': updateCar(data.id, data); break;
        }
      }
    });
  })
  .then(_ => {
    if (markerToFollow)
      map.panTo(markerToFollow.getBounds().getCenter());
  });
}

function updateLoop() {
  updateOnce()
  .then(_ => {
    const timeToNextUpdate = (updateStart + updateInterval) - performance.now();
    setTimeout(updateLoop, timeToNextUpdate);
  });
}

junctionsReady.then(_ => {
  updateLoop();
});
