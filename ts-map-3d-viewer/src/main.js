import * as THREE from "three";
import { MapViewer3D } from "./MapViewer3D.js";

// Initialize the 3D map viewer
const container = document.getElementById("container");
const loading = document.getElementById("loading");

// Create viewer instance
const viewer = new MapViewer3D(container);

// Load map data
async function init() {
  try {
    // Show loading screen
    loading.classList.remove("hidden");

    // Load data exported from TsMap
    await viewer.loadMapData({
      services: "./data/Services.json",
      cities: "./data/Cities.json",
      mapInfo: "./data/TileMapInfo.json",
      roadsIndex: "./data/roads_index.json", // Load index instead of full roads.json
      prefabs: "./data/Prefabs.json", // Load prefabs
      buildings: "./data/Buildings.json", // Load buildings
    });

    // Hide loading screen
    loading.classList.add("hidden");

    // Start rendering
    viewer.start();

    console.log("? TS Map 3D Viewer initialized successfully!");
  } catch (error) {
    loading.innerHTML = `
            <div style="color: #ff4444;">
                <h3>Error Loading Map</h3>
                <p>${error.message}</p>
                <p style="font-size: 12px; color: #999; margin-top: 10px;">
                    Make sure you've exported the map data from TsMap first!
                </p>
            </div>
        `;
    console.error("Failed to load map:", error);
  }
}

// UI Controls
document.getElementById("reset-camera").addEventListener("click", () => {
  viewer.resetCamera();
});

document.getElementById("toggle-2d-3d").addEventListener("click", () => {
  viewer.toggle2D3D();
});

// Layer toggles
document.querySelectorAll("[data-layer]").forEach((btn) => {
  btn.addEventListener("click", () => {
    btn.classList.toggle("active");
    const layer = btn.dataset.layer;
    const enabled = btn.classList.contains("active");
    viewer.toggleLayer(layer, enabled);
  });
});

// View mode toggles
document.querySelectorAll("[data-view]").forEach((btn) => {
  btn.addEventListener("click", () => {
    document
      .querySelectorAll("[data-view]")
      .forEach((b) => b.classList.remove("active"));
    btn.classList.add("active");
    viewer.setViewMode(btn.dataset.view);
  });
});

// Stats update
setInterval(() => {
  const stats = viewer.getStats();
  document.getElementById("fps").textContent = stats.fps;
  document.getElementById("camera-pos").textContent = `${stats.camera.x.toFixed(
    0
  )}, ${stats.camera.y.toFixed(0)}, ${stats.camera.z.toFixed(0)}`;
  document.getElementById("object-count").textContent = stats.objects;
}, 100);

// Close popup
document
  .querySelector("#info-popup .close-btn")
  .addEventListener("click", () => {
    document.getElementById("info-popup").style.display = "none";
  });

// Initialize the viewer
init();
