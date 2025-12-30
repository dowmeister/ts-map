import * as THREE from 'three';

/**
 * RouteRenderer - Renders navigation routes in 3D Google Maps style
 * 
 * Features:
 * - Animated route path with arrow indicators
 * - Distance markers along route
 * - Turn-by-turn waypoints
 * - Pulsing start/end markers
 * - Route elevation profile
 */
export class RouteRenderer {
    constructor(scene) {
        this.scene = scene;
        this.activeRoute = null;
        
        // Route material - bright blue like Google Maps
        this.routeMaterial = new THREE.MeshStandardMaterial({
            color: 0x4285f4,
            emissive: 0x4285f4,
            emissiveIntensity: 0.3,
            roughness: 0.5,
            metalness: 0.2
        });
        
        // Route outline material
        this.outlineMaterial = new THREE.MeshBasicMaterial({
            color: 0x1a73e8,
            transparent: true,
            opacity: 0.8,
            side: THREE.BackSide
        });
        
        // Waypoint materials
        this.startMaterial = new THREE.MeshStandardMaterial({
            color: 0x00ff00,
            emissive: 0x00ff00,
            emissiveIntensity: 0.5
        });
        
        this.endMaterial = new THREE.MeshStandardMaterial({
            color: 0xff0000,
            emissive: 0xff0000,
            emissiveIntensity: 0.5
        });
    }
    
    /**
     * Render a route path
     * @param {Object} route - Route data with waypoints
     * @param {THREE.Group} container - Parent container
     * @param {CoordinateConverter} converter - Coordinate converter
     */
    async render(route, container, converter) {
        this.clear();
        
        if (!route || !route.waypoints || route.waypoints.length < 2) {
            console.warn('Invalid route data');
            return;
        }
        
        const routeGroup = new THREE.Group();
        routeGroup.name = 'route';
        
        // Convert waypoints to 3D coordinates
        const points = route.waypoints.map(wp => {
            const pos = converter.convert(wp.X || wp.x, wp.Z || wp.z || wp.Y || wp.y);
            return new THREE.Vector3(pos.x, 10, pos.z); // Elevated for visibility
        });
        
        // Create route path
        const routePath = this.createRoutePath(points);
        routeGroup.add(routePath);
        
        // Add navigation arrows
        const arrows = this.createNavigationArrows(points);
        routeGroup.add(arrows);
        
        // Add start/end markers
        const startMarker = this.createWaypoint(points[0], 'start');
        const endMarker = this.createWaypoint(points[points.length - 1], 'end');
        routeGroup.add(startMarker);
        routeGroup.add(endMarker);
        
        // Add distance markers
        const distanceMarkers = this.createDistanceMarkers(points, route.totalDistance);
        routeGroup.add(distanceMarkers);
        
        container.add(routeGroup);
        this.activeRoute = routeGroup;
        
        console.log(`? Rendered route with ${route.waypoints.length} waypoints`);
    }
    
    createRoutePath(points) {
        const group = new THREE.Group();
        
        // Create smooth curve through points
        const curve = new THREE.CatmullRomCurve3(points);
        const tubePath = new THREE.TubeGeometry(curve, points.length * 5, 8, 8, false);
        
        // Main route tube
        const routeMesh = new THREE.Mesh(tubePath, this.routeMaterial);
        routeMesh.castShadow = true;
        group.add(routeMesh);
        
        // Outline (slightly larger)
        const outlinePath = new THREE.TubeGeometry(curve, points.length * 5, 10, 8, false);
        const outline = new THREE.Mesh(outlinePath, this.outlineMaterial);
        group.add(outline);
        
        return group;
    }
    
    createNavigationArrows(points) {
        const group = new THREE.Group();
        const arrowSpacing = 500; // Place arrow every 500 units
        
        let distance = 0;
        for (let i = 0; i < points.length - 1; i++) {
            const segmentLength = points[i].distanceTo(points[i + 1]);
            distance += segmentLength;
            
            if (distance >= arrowSpacing) {
                const arrow = this.createArrow(points[i], points[i + 1]);
                group.add(arrow);
                distance = 0;
            }
        }
        
        return group;
    }
    
    createArrow(start, end) {
        const direction = new THREE.Vector3().subVectors(end, start).normalize();
        const position = new THREE.Vector3().lerpVectors(start, end, 0.5);
        
        const arrowGeometry = new THREE.ConeGeometry(5, 15, 8);
        const arrowMaterial = new THREE.MeshBasicMaterial({ 
            color: 0xffffff,
            transparent: true,
            opacity: 0.9
        });
        const arrow = new THREE.Mesh(arrowGeometry, arrowMaterial);
        
        arrow.position.copy(position);
        arrow.quaternion.setFromUnitVectors(
            new THREE.Vector3(0, 1, 0),
            direction
        );
        
        return arrow;
    }
    
    createWaypoint(position, type) {
        const group = new THREE.Group();
        
        // Pole
        const poleGeometry = new THREE.CylinderGeometry(2, 2, 50);
        const poleMaterial = new THREE.MeshStandardMaterial({ color: 0x333333 });
        const pole = new THREE.Mesh(poleGeometry, poleMaterial);
        pole.position.y = -25;
        group.add(pole);
        
        // Marker sphere
        const markerGeometry = new THREE.SphereGeometry(12, 16, 16);
        const material = type === 'start' ? this.startMaterial : this.endMaterial;
        const marker = new THREE.Mesh(markerGeometry, material);
        marker.castShadow = true;
        
        // Pulsing animation
        marker.userData.animate = (time) => {
            marker.scale.setScalar(1 + Math.sin(time * 3) * 0.2);
        };
        
        group.add(marker);
        group.position.copy(position);
        
        return group;
    }
    
    createDistanceMarkers(points, totalDistance) {
        const group = new THREE.Group();
        
        // Create markers every ~10km (adjust based on scale)
        const markerInterval = 1000;
        let currentDistance = 0;
        
        for (let i = 0; i < points.length - 1; i++) {
            currentDistance += points[i].distanceTo(points[i + 1]);
            
            if (currentDistance >= markerInterval) {
                const marker = this.createDistanceLabel(points[i], currentDistance);
                group.add(marker);
                currentDistance = 0;
            }
        }
        
        return group;
    }
    
    createDistanceLabel(position, distance) {
        // Create canvas for distance text
        const canvas = document.createElement('canvas');
        const context = canvas.getContext('2d');
        canvas.width = 256;
        canvas.height = 64;
        
        context.font = 'Bold 32px Arial';
        context.fillStyle = 'rgba(0, 0, 0, 0.8)';
        context.fillRect(0, 0, canvas.width, canvas.height);
        context.fillStyle = 'white';
        context.fillText(`${(distance / 1000).toFixed(1)} km`, 10, 42);
        
        const texture = new THREE.CanvasTexture(canvas);
        const spriteMaterial = new THREE.SpriteMaterial({ 
            map: texture,
            transparent: true
        });
        const sprite = new THREE.Sprite(spriteMaterial);
        sprite.scale.set(50, 12, 1);
        sprite.position.copy(position);
        sprite.position.y += 20;
        
        return sprite;
    }
    
    clear() {
        if (this.activeRoute) {
            this.activeRoute.parent.remove(this.activeRoute);
            this.activeRoute = null;
        }
    }
    
    /**
     * Animate route (for demonstration or following)
     * @param {number} progress - Animation progress 0-1
     */
    animateRoute(progress) {
        if (!this.activeRoute) return;
        
        // TODO: Implement camera follow-along route animation
    }
}
