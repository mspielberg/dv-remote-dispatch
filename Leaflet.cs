namespace DvMod.RemoteDispatch
{
    public static class LeafletHost
    {
        public static string RenderMapPage()
        {
            return @"
<html>
<head>
<link rel=""stylesheet"" href=""https://unpkg.com/leaflet@1.7.1/dist/leaflet.css""
  integrity=""sha512-xodZBNTC5n17Xt2atTPuE1HxjVMSvLVW9ocqUKLsCC5CXdbqCmblAshOMAS6/keqq/sMZMZ19scR4PsZChSR7A==""
  crossorigin=""""/>
<script src=""https://unpkg.com/leaflet@1.7.1/dist/leaflet.js""
  integrity=""sha512-XQoYMqMTK8LvdxXYG3nZ448hOEQiglfqkJs1NOQV44cWnUrBc8PkAOcXy20w0vlaXaVUearIOBhiXZ5V3ynxwA==""
  crossorigin=""""></script>
</head>
<body>
<div id=""map"" style=""height:100%""/>
<script>
var map = L.map('map', { maxBounds: [[-0.5, -0.5], [1.5, 1.5]] });

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
</script>
</body>
</html>
";
        }
    }
}