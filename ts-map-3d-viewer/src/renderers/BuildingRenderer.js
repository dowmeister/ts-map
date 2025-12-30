import * as THREE from 'three';

export class BuildingRenderer {
    constructor(scene) {
        this.scene = scene;
        
        // Building materials with different colors for variety
        this.materials = [
            new THREE.MeshStandardMaterial({
                color: 0x8b7355, // Brown/tan
                roughness: 0.8,
                metalness: 0.2
            }),
            new THREE.MeshStandardMaterial({
                color: 0x6b5d52, // Dark brown
                roughness: 0.7,
                metalness: 0.3
            }),
            new THREE.MeshStandardMaterial({
                color: 0x9d8b7a, // Light brown
                roughness: 0.75,
                metalness: 0.25
            })
        ];
    }
    
    async render(buildings, container, converter) {
        if (!buildings || buildings.length === 0) {
            console.warn('No building data to render');
            return;
        }
        
        let buildingCount = 0;
        
        // Render in batches
        const batchSize = 100;
        for (let i = 0; i < buildings.length; i += batchSize) {
            const batch = buildings.slice(i, i + batchSize);
            
            for (const building of batch) {
                try {
                    const buildingMesh = this.createBuilding(building, converter);
                    if (buildingMesh) {
                        container.add(buildingMesh);
                        buildingCount++;
                    }
                } catch (error) {
                    console.error('Error rendering building:', error);
                }
            }
            
            // Yield to browser
            if (i + batchSize < buildings.length) {
                await new Promise(resolve => setTimeout(resolve, 0));
            }
        }
        
        console.log(`? Rendered ${buildingCount} buildings`);
    }
    
    createBuilding(building, converter) {
        const pos = converter.convert(building.X, building.Z);
        
        // Random building dimensions for variety (like in the screenshot)
        const width = 30 + Math.random() * 40; // 30-70 units
        const depth = 30 + Math.random() * 40; // 30-70 units
        const height = 20 + Math.random() * 60; // 20-80 units tall
        
        const geometry = new THREE.BoxGeometry(width, height, depth);
        const material = this.materials[
            Math.floor(Math.random() * this.materials.length)
        ];
        
        const mesh = new THREE.Mesh(geometry, material);
        mesh.position.set(pos.x, height / 2, pos.z); // Position at ground level
        mesh.castShadow = true;
        mesh.receiveShadow = true;
        
        // Store building data
        mesh.userData = {
            type: 'building',
            name: building.name,
            buildingType: building.type
        };
        
        return mesh;
    }
}
