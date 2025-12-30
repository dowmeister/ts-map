import * as THREE from 'three';

export class POIRenderer {
    constructor(scene) {
        this.scene = scene;
        
        // Service type colors (matching Google Maps style)
        this.colors = {
            'Fuel Station': 0xff6600,
            'Service Station': 0x0066ff,
            'Garage': 0x00cc66,
            'Speed Camera': 0xff0000,
            'Toll Gate': 0xffcc00,
            'Weight Station': 0x9933ff,
            'Parking': 0x3399ff
        };
    }
    
    async render(services, container, converter) {
        if (!services || services.length === 0) {
            console.warn('No service data to render');
            return;
        }
        
        let poiCount = 0;
        
        for (const service of services) {
            try {
                const poi = this.createPOI(service, converter);
                if (poi) {
                    container.add(poi);
                    poiCount++;
                }
            } catch (error) {
                console.error('Error rendering POI:', error);
            }
        }
        
        console.log(`? Rendered ${poiCount} POIs`);
    }
    
    createPOI(service, converter) {
        const group = new THREE.Group();
        const pos = converter.convert(service.X, service.Y);
        
        // Get color for service type
        const color = this.colors[service.Type] || 0xffffff;
        
        // Create pole (vertical stick)
        const poleGeometry = new THREE.CylinderGeometry(1, 1, 30);
        const poleMaterial = new THREE.MeshStandardMaterial({ 
            color: 0x333333 
        });
        const pole = new THREE.Mesh(poleGeometry, poleMaterial);
        pole.position.y = 15;
        pole.castShadow = true;
        group.add(pole);
        
        // Create icon marker (glowing sphere)
        const iconGeometry = new THREE.SphereGeometry(8, 16, 16);
        const iconMaterial = new THREE.MeshStandardMaterial({ 
            color: color,
            emissive: color,
            emissiveIntensity: 0.5,
            roughness: 0.4,
            metalness: 0.2
        });
        const icon = new THREE.Mesh(iconGeometry, iconMaterial);
        icon.position.y = 40;
        icon.castShadow = true;
        group.add(icon);
        
        // Add pulsing animation
        icon.userData.animate = (time) => {
            icon.scale.setScalar(1 + Math.sin(time * 2) * 0.1);
        };
        
        // Position in world
        group.position.set(pos.x, 0, pos.z);
        
        // Store service data for interaction
        group.userData = {
            type: 'poi',
            data: service
        };
        
        return group;
    }
}
