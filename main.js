var map = L.map('map', { maxBounds: [[-0.5, -0.5], [1.5, 1.5]], tap: false });

fetch('/track')
.then(resp => resp.json())
.then(coords => {
  var poly = L.polyline(coords, { interactive: false }).addTo(map);
  map.fitBounds(poly.getBounds());
});

var junctionMarkers
function toggleJunction(index) {
  fetch('/junction/'+index+'/toggle', { method: 'POST' })
  .then(_ => updateJunctions())
}

function createJunctionMarker(p, index) {
  return L.circle(p, { radius: 25, fillOpacity: 0.5 })
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

function periodicUpdateJunctions() {
  updateJunctions()
  .then(_ => window.setTimeout(periodicUpdateJunctions, 1000));
}

fetch('/junction')
.then(resp => resp.json())
.then(coords => {
  junctionMarkers = coords.map((p, index) => createJunctionMarker(p, index))
  periodicUpdateJunctions();
});
