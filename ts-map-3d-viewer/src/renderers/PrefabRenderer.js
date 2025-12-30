import * as THREE from 'three';

export class PrefabRenderer {
    constructor(scene) {
        this.scene = scene;
        
        // Prefab materials (roads, intersections, etc.)
        this.materials = {
            road: new THREE.MeshStandardMaterial({
                color: 0x3a3a3a,
                roughness: 0.9,
                metalness: 0.1,
                side: THREE.DoubleSide
            }),
            light: new THREE.MeshStandardMaterial({
                color: 0xcccccc,
                roughness: 0.7,
                side: THREE.DoubleSide
            }),
            dark: new THREE.MeshStandardMaterial({
                color: 0x666666,
                roughness: 0.8,
                side: THREE.DoubleSide
            }),
            green: new THREE.MeshStandardMaterial({
                color: 0x7ec850,
                roughness: 0.9,
                side: THREE.DoubleSide
            })
        };
    }
    
    async render(prefabs, container, converter) {
        if (!prefabs || prefabs.length === 0) {
            console.warn('No prefab data to render');
            return;
        }
        
        let prefabCount = 0;
        
        // Render in batches to avoid freezing
        const batchSize = 50;
        for (let i = 0; i < prefabs.length; i += batchSize) {
            const batch = prefabs.slice(i, i + batchSize);
            
            for (const prefab of batch) {
                try {
                    const prefabMesh = this.createPrefab(prefab, converter);
                    if (prefabMesh) {
                        container.add(prefabMesh);
                        prefabCount++;
                    }
                } catch (error) {
                    console.error('Error rendering prefab:', error);
                }
            }
            
            // Yield to browser every batch
            if (i + batchSize < prefabs.length) {
                await new Promise(resolve => setTimeout(resolve, 0));
            }
        }
        
        console.log(`? Rendered ${prefabCount} prefabs`);
    }
    
    createPrefab(prefab, converter) {
        // Prefabs are complex intersections, roundabouts, etc.
        // Render as filled polygons that blend with roads
        
        if (!prefab.nodes || prefab.nodes.length < 3) {
            return null;
        }

        const group = new THREE.Group();
        
        // Convert all node points
        const points2D = [];
        const points3D = [];
        
        prefab.nodes.forEach(node => {
            const pos = converter.convert(node.X || node.x, node.Z || node.z || node.Y || node.y);
            points2D.push(new THREE.Vector2(pos.x, pos.z));
            points3D.push(new THREE.Vector3(pos.x, 0.5, pos.z)); // Slightly lower than roads
        });
        
        // Create filled polygon shape
        const shape = new THREE.Shape(points2D);
        const geometry = new THREE.ShapeGeometry(shape);
        
        // Rotate to lay flat on XZ plane
        geometry.rotateX(-Math.PI / 2);
        geometry.translate(0, 0.5, 0); // Slightly above ground
        
        // Determine material based on prefab type/category
        let material = this.materials.road;
        if (prefab.category) {
            if (prefab.category.includes('green')) {
                material = this.materials.green;
            } else if (prefab.category.includes('light')) {
                material = this.materials.light;
            } else if (prefab.category.includes('dark')) {
                material = this.materials.dark;
            }
        }
        
        const mesh = new THREE.Mesh(geometry, material);
        mesh.receiveShadow = true;
        mesh.castShadow = false; // Prefabs don't cast shadows (flat)
        
        // Add subtle outline for better visibility
        const edges = new THREE.EdgesGeometry(geometry);
        const line = new THREE.LineSegments(
            edges,
            new THREE.LineBasicMaterial({ color: 0x2a2a2a, linewidth: 1 })
        );
        
        group.add(mesh);
        group.add(line);
        
        // Store prefab data
        group.userData = {
            type: 'prefab',
            category: prefab.category,
            nodeCount: prefab.nodes.length
        };
        
        return group;
    }
}
