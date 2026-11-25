"use strict";

const webview = window.chrome.webview;
const map = L.map("map", { worldCopyJump: true, doubleClickZoom: false }).setView([20, 0], 2);
map.attributionControl.setPrefix(false);
const tileError = document.getElementById("tile-error");
const labelsPane = map.createPane("map-labels");
labelsPane.style.zIndex = 300;
labelsPane.style.pointerEvents = "none";
const streets = L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
    maxZoom: 19,
    attribution: '<a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener">© OpenStreetMap contributors</a>'
});
const imagery = L.tileLayer("https://services.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}", {
    maxNativeZoom: 18, maxZoom: 19,
    attribution: '<a href="https://www.esri.com/en-us/legal/terms/full-master-agreement" target="_blank" rel="noopener">© Esri</a>, Vantor, Earthstar Geographics, and the GIS User Community'
});
const labels = L.tileLayer("https://services.arcgisonline.com/ArcGIS/rest/services/Reference/World_Boundaries_and_Places/MapServer/tile/{z}/{y}/{x}", {
    maxNativeZoom: 19, maxZoom: 19, pane: "map-labels",
    attribution: 'Labels: Esri, HERE, Garmin, <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener">© OpenStreetMap contributors</a>, and the GIS User Community'
});
let activeBasemap;
for (const layer of [streets, imagery]) {
    layer.on("tileerror", () => { if (activeBasemap === layer) tileError.hidden = false; })
         .on("tileload", () => { if (activeBasemap === layer) tileError.hidden = true; });
}
function setStyle(style) {
    map.removeLayer(streets).removeLayer(imagery).removeLayer(labels);
    tileError.hidden = true;
    activeBasemap = style === "normal" ? streets : imagery;
    activeBasemap.addTo(map);
    if (style === "hybrid") labels.addTo(map);
}
setStyle("normal");
const files = L.layerGroup().addTo(map);
const photoElements = new Map();
const photoCache = new Map();
const waiting = new Set();
const inFlight = new Set();

function requestPhotos() {
    for (const id of waiting) {
        if (inFlight.size >= 8) break;
        waiting.delete(id);
        inFlight.add(id);
        webview.postMessage({ kind: "request-thumbnail", id });
    }
}

const visiblePhotos = new IntersectionObserver(entries => {
    for (const entry of entries) {
        const id = entry.target.dataset.fileId;
        if (entry.isIntersecting) {
            if (!inFlight.has(id)) waiting.add(id);
        } else {
            waiting.delete(id);
        }
    }
    requestPhotos();
}, { root: document.getElementById("map") });

function showPhoto(tile, image) {
    if (!image) return;
    const photo = document.createElement("img");
    photo.alt = "";
    photo.draggable = false;
    photo.src = "data:image/png;base64," + image;
    tile.replaceChildren(photo);
}

function addPhotoPin(file) {
    const tile = document.createElement("div");
    tile.className = "photo-pin-tile";
    tile.dataset.fileId = file.id;
    tile.textContent = "▧";

    const size = file.selected ? 48 : 44;
    const style = "photo-pin" + (file.pending ? " photo-pin-pending" : "");
    const icon = L.divIcon({ html: tile, className: style, iconSize: [size, size], iconAnchor: [size / 2, size / 2] });
    L.marker([file.latitude, file.longitude], {
        icon, title: file.name + (file.pending ? " · Pending location" : ""),
        zIndexOffset: file.selected ? 1000 : 0, bubblingMouseEvents: false
    }).on("click", () => webview.postMessage({ kind: "file", id: file.id })).addTo(files);
    photoElements.set(file.id, tile);
    if (photoCache.has(file.id)) showPhoto(tile, photoCache.get(file.id));
    else visiblePhotos.observe(tile);
}

function fitFiles() {
    const points = files.getLayers().map(layer => layer.getLatLng());
    if (points.length > 0) map.fitBounds(points, { maxZoom: 15, padding: [48, 48], animate: false });
}

function pick(point) {
    const location = point.wrap();
    webview.postMessage({ kind: "pick", latitude: location.lat, longitude: location.lng });
}

map.on("click", event => pick(event.latlng));
webview.addEventListener("message", ({ data }) => {
    if (data.kind === "files") {
        visiblePhotos.disconnect();
        waiting.clear();
        photoElements.clear();
        files.clearLayers();
        for (const file of data.locations) addPhotoPin(file);
        if (data.fit) fitFiles();
    } else if (data.kind === "thumbnail") {
        inFlight.delete(data.id);
        photoCache.set(data.id, data.image);
        if (photoCache.size > 256) photoCache.delete(photoCache.keys().next().value);
        const tile = photoElements.get(data.id);
        if (tile) {
            visiblePhotos.unobserve(tile);
            showPhoto(tile, data.image);
        }
        requestPhotos();
    } else if (data.kind === "fit") {
        fitFiles();
    } else if (data.kind === "style") {
        if (["normal", "hybrid", "satellite"].includes(data.value)) setStyle(data.value);
    } else if (data.kind === "focus") {
        map.setView([data.latitude, data.longitude], 15);
    } else if (data.kind === "pick-center") {
        pick(map.getCenter());
    }
});
webview.postMessage({ kind: "ready" });
