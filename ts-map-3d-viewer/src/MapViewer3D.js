import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import { DataLoader } from './loaders/DataLoader.js';
import { RoadRenderer } from './renderers/RoadRenderer.js';
import { PrefabRenderer } from './renderers/PrefabRenderer.js';
import { POIRenderer } from './renderers/POIRenderer.js';
import { CityRenderer } from './renderers/CityRenderer.js';
import { BuildingRenderer } from './renderers/BuildingRenderer.js';
import { CoordinateConverter } from './utils/CoordinateConverter.js';

export class MapViewer3D {
    constructor(container) {
        this.container = container;
        this.clock = new THREE.Clock();
        this.frameCount = 0;
        this.fps = 60;
        
        // Layer groups
        this.layers = {
            roads: null,
            prefabs: null,
            pois: null,
            cities: null,
            buildings: null, // Add buildings layer
            routes: null // For future routing
        };
        
        // Setup Three.js scene
        this.setupScene();
        this.setupCamera();
        this.setupRenderer();
        this.setupLighting();
        this.setupControls();
        this.setupTerrain();
        this.setupRaycaster();
        
        // Renderers
        this.roadRenderer = new RoadRenderer(this.scene);
        this.prefabRenderer = new PrefabRenderer(this.scene);
        this.poiRenderer = new POIRenderer(this.scene);
        this.cityRenderer = new CityRenderer(this.scene);
        this.buildingRenderer = new BuildingRenderer(this.scene);
        
        // Handle window resize
        window.addEventListener('resize', () => this.onWindowResize());
    }
    
    setupScene() {
        this.scene = new THREE.Scene();
        // Dark theme background (like navigation apps)
        this.scene.background = new THREE.Color(0x1a1d28); // Dark blue-gray
        this.scene.fog = new THREE.Fog(0x1a1d28, 5000, 50000); // Matching fog
    }
    
    setupCamera() {
        const aspect = window.innerWidth / window.innerHeight;
        this.camera = new THREE.PerspectiveCamera(50, aspect, 1, 200000);
        
        // Google Maps Navigator style: tilted down view
        this.camera.position.set(0, 3000, 5000);
        this.camera.lookAt(0, 0, 0);
    }
    
    setupRenderer() {
        this.renderer = new THREE.WebGLRenderer({ 
            antialias: true,
            alpha: false
        });
        this.renderer.setSize(window.innerWidth, window.innerHeight);
        this.renderer.setPixelRatio(window.devicePixelRatio);
        this.renderer.shadowMap.enabled = true;
        this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
        this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
        this.renderer.toneMappingExposure = 1.0;
        
        this.container.appendChild(this.renderer.domElement);
    }
    
    setupLighting() {
        // Ambient light (soft overall illumination)
        const ambient = new THREE.AmbientLight(0xffffff, 0.5);
        this.scene.add(ambient);
        
        // Directional light (sun)
        const sun = new THREE.DirectionalLight(0xffffff, 0.8);
        sun.position.set(10000, 15000, 5000);
        sun.castShadow = true;
        
        // Shadow camera setup
        const shadowSize = 20000;
        sun.shadow.camera.left = -shadowSize;
        sun.shadow.camera.right = shadowSize;
        sun.shadow.camera.top = shadowSize;
        sun.shadow.camera.bottom = -shadowSize;
        sun.shadow.camera.near = 1000;
        sun.shadow.camera.far = 50000;
        sun.shadow.mapSize.width = 2048;
        sun.shadow.mapSize.height = 2048;
        sun.shadow.bias = -0.0001;
        
        this.scene.add(sun);
        
        // Hemisphere light for sky/ground gradient
        const hemiLight = new THREE.HemisphereLight(0x87ceeb, 0x7ec850, 0.3);
        this.scene.add(hemiLight);
    }
    
    setupControls() {
        this.controls = new OrbitControls(this.camera, this.renderer.domElement);
        this.controls.enableDamping = true;
        this.controls.dampingFactor = 0.05;
        this.controls.minDistance = 200;
        this.controls.maxDistance = 50000;
        this.controls.maxPolarAngle = Math.PI / 2.1; // Prevent going under ground
        this.controls.screenSpacePanning = false;
        this.controls.zoomSpeed = 1.2;
        this.controls.rotateSpeed = 0.5;
    }
    
    setupTerrain() {
        // Ground plane - dark for navigation theme
        const groundSize = 200000;
        const groundGeometry = new THREE.PlaneGeometry(groundSize, groundSize, 100, 100);
        
        // Dark ground material (not green grass)
        const groundMaterial = new THREE.MeshStandardMaterial({ 
            color: 0x0d0f14, // Very dark blue-gray
            roughness: 1.0,
            metalness: 0.0
        });
        
        const ground = new THREE.Mesh(groundGeometry, groundMaterial);
        ground.rotation.x = -Math.PI / 2;
        ground.receiveShadow = true;
        this.scene.add(ground);
    }
    
    setupRaycaster() {
        this.raycaster = new THREE.Raycaster();
        this.mouse = new THREE.Vector2();
        
        // Click handling for POIs
        this.renderer.domElement.addEventListener('click', (event) => {
            this.onMapClick(event);
        });
    }
    
    async loadMapData(paths) {
        const loader = new DataLoader();
        const data = await loader.loadAll(paths);
        
        // Store map bounds and scale
        this.mapInfo = data.mapInfo;
        this.coordinateConverter = new CoordinateConverter(this.mapInfo);
        this.dataLoader = loader; // Keep loader for dynamic chunk loading
        this.roadsIndex = data.roadsIndex;
        
        // Create layer groups
        this.layers.roads = new THREE.Group();
        this.layers.roads.name = 'roads';
        this.scene.add(this.layers.roads);
        
        this.layers.prefabs = new THREE.Group();
        this.layers.prefabs.name = 'prefabs';
        this.scene.add(this.layers.prefabs);
        
        this.layers.pois = new THREE.Group();
        this.layers.pois.name = 'pois';
        this.scene.add(this.layers.pois);
        
        this.layers.cities = new THREE.Group();
        this.layers.cities.name = 'cities';
        this.scene.add(this.layers.cities);
        
        this.layers.buildings = new THREE.Group();
        this.layers.buildings.name = 'buildings';
        this.scene.add(this.layers.buildings);

        // Initial road chunk load (around map center)
        console.log('???  Loading initial road chunks...');
        const centerX = (this.mapInfo.x.min + this.mapInfo.x.max) / 2;
        const centerZ = (this.mapInfo.z.min + this.mapInfo.z.max) / 2;
        await this.loadRoadChunksNear(centerX, centerZ, 10000);
        
        // Load prefabs if available
        if (data.prefabs && data.prefabs.length > 0) {
            console.log('???  Rendering prefabs...');
            await this.prefabRenderer.render(data.prefabs, this.layers.prefabs, this.coordinateConverter);
        }
        
        // Load buildings
        if (data.buildings && data.buildings.length > 0) {
            console.log('?? Rendering buildings...');
            await this.buildingRenderer.render(data.buildings, this.layers.buildings, this.coordinateConverter);
        }
        
        console.log('?? Rendering POIs...');
        await this.poiRenderer.render(data.services, this.layers.pois, this.coordinateConverter);
        
        console.log('???  Rendering cities...');
        await this.cityRenderer.render(data.cities, this.layers.cities, this.coordinateConverter);
        
        // Center camera on map
        this.centerCamera();
    }
    
    async loadRoadChunksNear(x, z, viewDistance) {
        if (!this.roadsIndex || this.roadsIndex.length === 0) {
            console.warn('No road index available');
            return;
        }
        
        const newRoads = await this.dataLoader.loadRoadChunksNear(
            x, z, viewDistance, this.roadsIndex
        );
        
        if (newRoads.length > 0) {
            console.log(`? Loaded ${newRoads.length} new roads`);
            await this.roadRenderer.render(newRoads, this.layers.roads, this.coordinateConverter);
        }
    }
    
    centerCamera() {
        if (!this.mapInfo) return;
        
        const centerX = (this.mapInfo.x.min + this.mapInfo.x.max) / 2;
        const centerZ = (this.mapInfo.z.min + this.mapInfo.z.max) / 2;
        
        const converted = this.coordinateConverter.convert(centerX, centerZ);
        
        this.camera.position.set(converted.x, 3000, converted.z + 5000);
        this.controls.target.set(converted.x, 0, converted.z);
        this.controls.update();
    }
    
    resetCamera() {
        this.centerCamera();
    }
    
    toggle2D3D() {
        const current = this.camera.position.y;
        const targetY = current < 10000 ? 15000 : 3000;
        const targetZ = current < 10000 ? this.camera.position.z + 5000 : this.camera.position.z - 5000;
        
        this.animateCamera(
            this.camera.position.x,
            targetY,
            targetZ
        );
    }
    
    setViewMode(mode) {
        if (mode === 'navigation') {
            this.controls.maxPolarAngle = Math.PI / 2.1;
            this.animateCamera(
                this.camera.position.x,
                3000,
                this.camera.position.z
            );
        } else if (mode === 'topdown') {
            this.controls.maxPolarAngle = Math.PI / 2;
            this.animateCamera(
                this.camera.position.x,
                15000,
                this.controls.target.z
            );
        }
    }
    
    animateCamera(x, y, z) {
        const start = this.camera.position.clone();
        const end = new THREE.Vector3(x, y, z);
        const duration = 1000; // ms
        const startTime = Date.now();
        
        const animate = () => {
            const elapsed = Date.now() - startTime;
            const t = Math.min(elapsed / duration, 1);
            const eased = this.easeInOutCubic(t);
            
            this.camera.position.lerpVectors(start, end, eased);
            
            if (t < 1) {
                requestAnimationFrame(animate);
            }
        };
        
        animate();
    }
    
    easeInOutCubic(t) {
        return t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;
    }
    
    toggleLayer(layerName, enabled) {
        if (this.layers[layerName]) {
            this.layers[layerName].visible = enabled;
        }
    }
    
    onMapClick(event) {
        // Calculate mouse position in normalized device coordinates
        const rect = this.renderer.domElement.getBoundingClientRect();
        this.mouse.x = ((event.clientX - rect.left) / rect.width) * 2 - 1;
        this.mouse.y = -((event.clientY - rect.top) / rect.height) * 2 + 1;
        
        // Update raycaster
        this.raycaster.setFromCamera(this.mouse, this.camera);
        
        // Check for intersections with POIs
        const intersects = this.raycaster.intersectObjects(this.layers.pois.children, true);
        
        if (intersects.length > 0) {
            const object = intersects[0].object;
            let userData = object.userData;
            
            // Traverse up to find POI data
            let parent = object.parent;
            while (parent && !userData.type && parent !== this.scene) {
                userData = parent.userData;
                parent = parent.parent;
            }
            
            if (userData.type === 'poi') {
                this.showPOIInfo(userData.data, event);
            }
        }
    }
    
    showPOIInfo(data, event) {
        const popup = document.getElementById('info-popup');
        const title = document.getElementById('popup-title');
        const content = document.getElementById('popup-content');
        
        title.textContent = data.Type;
        content.innerHTML = `
            <div class="info-row"><strong>City:</strong> ${data.City || 'Unknown'}</div>
            <div class="info-row"><strong>Position:</strong> ${data.X.toFixed(1)}, ${data.Y.toFixed(1)}</div>
            ${data.Properties && data.Properties.MinSpeed ? 
                `<div class="info-row"><strong>Speed Limit:</strong> ${data.Properties.MinSpeed} - ${data.Properties.MaxSpeed} km/h</div>` 
                : ''}
        `;
        
        popup.style.display = 'block';
        popup.style.left = event.clientX + 10 + 'px';
        popup.style.top = event.clientY + 10 + 'px';
    }
    
    getStats() {
        return {
            fps: this.fps,
            camera: this.camera.position,
            objects: this.scene.children.length
        };
    }
    
    onWindowResize() {
        this.camera.aspect = window.innerWidth / window.innerHeight;
        this.camera.updateProjectionMatrix();
        this.renderer.setSize(window.innerWidth, window.innerHeight);
    }
    
    animate() {
        requestAnimationFrame(() => this.animate());
        
        // Update controls
        this.controls.update();
        
        // Dynamic road chunk loading based on camera movement
        if (this.frameCount % 60 === 0 || !this.roadsIndex) { // Check every 60 frames (~1 second)
            const camPos = this.camera.position;
            const converted = this.coordinateConverter.convert(camPos.x, camPos.z);
            this.loadRoadChunksNear(converted.x, converted.z, 15000); // Load chunks within 15km
        }
        
        // Calculate FPS
        this.frameCount++;
        if (this.frameCount % 60 === 0) {
            this.fps = Math.round(1 / this.clock.getDelta());
        }
        
        // Render scene
        this.renderer.render(this.scene, this.camera);
    }
    
    start() {
        this.animate();
    }
}
