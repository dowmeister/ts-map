import * as THREE from 'three';

export class RoadRenderer {
    constructor(scene) {
        this.scene = scene;
        
        // Road material - DARK gray (not bright) for non-route roads
        this.roadMaterial = new THREE.MeshStandardMaterial({
            color: 0x2a2d36, // Dark blue-gray (like screenshot)
            roughness: 0.9,
            metalness: 0.1,
            side: THREE.DoubleSide,
            flatShading: false,
            depthWrite: true,
            depthTest: true
        });
        
        // Bright cyan for active routes (will use RouteRenderer for this)
        this.routeMaterial = new THREE.MeshStandardMaterial({
            color: 0x00d9ff, // Bright cyan
            emissive: 0x00d9ff,
            emissiveIntensity: 0.5,
            roughness: 0.3,
            metalness: 0.4
        });
        
        // Road marking material - white/yellow lines
        this.markingMaterial = new THREE.LineBasicMaterial({
            color: 0xffffff,
            linewidth: 2,
            transparent: true,
            opacity: 0.8
        });
        
        // Cache for road meshes to avoid duplicates
        this.roadCache = new Map();
    }
    
    async render(roads, container, converter) {
        if (!roads || roads.length === 0) {
            console.warn('No road data to render');
            return;
        }
        
        let roadCount = 0;
        
        // Render in batches to avoid freezing
        const batchSize = 100;
        for (let i = 0; i < roads.length; i += batchSize) {
            const batch = roads.slice(i, i + batchSize);
            
            for (const road of batch) {
                try {
                    const roadMesh = this.createRoad(road, converter);
                    if (roadMesh) {
                        container.add(roadMesh);
                        roadCount++;
                    }
                } catch (error) {
                    console.error('Error rendering road:', error);
                }
            }
            
            // Yield to browser every batch
            if (i + batchSize < roads.length) {
                await new Promise(resolve => setTimeout(resolve, 0));
            }
        }
        
        console.log(`? Rendered ${roadCount} roads`);
    }
    
    createRoad(road, converter) {
        // Roads in TsMap have start/end nodes and bezier curves
        // We'll create a 3D ribbon along the path
        
        if (!road.startNode || !road.endNode) {
            return null;
        }

        const width = road.width || 10; // Default road width
        const points = this.getRoadPoints(road, converter);
        
        if (points.length < 2) {
            return null;
        }

        // Create road mesh as extruded ribbon with smooth connections
        const roadMesh = this.createRoadRibbon(points, width);
        roadMesh.castShadow = true;
        roadMesh.receiveShadow = true;
        
        // Store road data for potential node merging
        roadMesh.userData = {
            type: 'road',
            startNode: road.startNode,
            endNode: road.endNode
        };
        
        return roadMesh;
    }
    
    getRoadPoints(road, converter) {
        const points = [];
        
        // If road has explicit bezier curve points, use those
        if (road.points && road.points.length > 0) {
            road.points.forEach(p => {
                const converted = converter.convert(p.X || p.x, p.Z || p.z);
                points.push(new THREE.Vector3(converted.x, 1, converted.z)); // Slightly above ground
            });
        } else {
            // Create smooth curve between start and end nodes
            const start = converter.convert(road.startNode.X, road.startNode.Z);
            const end = converter.convert(road.endNode.X, road.endNode.Z);
            
            // Use rotation to create smooth bezier curve (like the game does)
            const startRot = road.startNode.Rotation || 0;
            const endRot = road.endNode.Rotation || 0;
            
            const distance = Math.sqrt(
                Math.pow(end.x - start.x, 2) + 
                Math.pow(end.z - start.z, 2)
            );
            
            // Control point distances based on road length
            const controlDist = distance * 0.5;
            
            // Calculate control points using node rotations
            const control1 = new THREE.Vector3(
                start.x + Math.cos(startRot) * controlDist,
                1,
                start.z + Math.sin(startRot) * controlDist
            );
            
            const control2 = new THREE.Vector3(
                end.x - Math.cos(endRot) * controlDist,
                1,
                end.z - Math.sin(endRot) * controlDist
            );
            
            // Create cubic bezier curve
            const curve = new THREE.CubicBezierCurve3(
                new THREE.Vector3(start.x, 1, start.z),
                control1,
                control2,
                new THREE.Vector3(end.x, 1, end.z)
            );
            
            // Sample points along curve (more points = smoother)
            const samples = Math.max(10, Math.floor(distance / 50));
            const curvePoints = curve.getPoints(samples);
            points.push(...curvePoints);
        }
        
        return points;
    }
    
    createRoadRibbon(points, width) {
        if (points.length < 2) return null;
        
        const halfWidth = width / 2;
        const vertices = [];
        const indices = [];
        const normals = [];
        const uvs = [];
        
        // Create ribbon geometry with proper tangent calculation
        for (let i = 0; i < points.length; i++) {
            const point = points[i];
            
            // Calculate forward direction (tangent)
            let tangent;
            if (i === 0) {
                // First point: use direction to next point
                tangent = new THREE.Vector3()
                    .subVectors(points[1], points[0])
                    .normalize();
            } else if (i === points.length - 1) {
                // Last point: use direction from previous point
                tangent = new THREE.Vector3()
                    .subVectors(points[i], points[i - 1])
                    .normalize();
            } else {
                // Middle points: average of incoming and outgoing directions
                const incoming = new THREE.Vector3()
                    .subVectors(points[i], points[i - 1])
                    .normalize();
                const outgoing = new THREE.Vector3()
                    .subVectors(points[i + 1], points[i])
                    .normalize();
                tangent = new THREE.Vector3()
                    .addVectors(incoming, outgoing)
                    .normalize();
            }
            
            // Calculate perpendicular vector (cross with up vector)
            const up = new THREE.Vector3(0, 1, 0);
            const perpendicular = new THREE.Vector3()
                .crossVectors(tangent, up)
                .normalize();
            
            // Create left and right edge points
            const left = new THREE.Vector3()
                .addVectors(point, perpendicular.clone().multiplyScalar(halfWidth));
            const right = new THREE.Vector3()
                .addVectors(point, perpendicular.clone().multiplyScalar(-halfWidth));
            
            // Add vertices (left and right)
            vertices.push(left.x, left.y, left.z);
            vertices.push(right.x, right.y, right.z);
            
            // Add normals (pointing up)
            normals.push(0, 1, 0);
            normals.push(0, 1, 0);
            
            // Add UVs
            const u = i / (points.length - 1);
            uvs.push(0, u);
            uvs.push(1, u);
            
            // Add indices for triangles (except for last point)
            if (i < points.length - 1) {
                const base = i * 2;
                // First triangle
                indices.push(base, base + 1, base + 2);
                // Second triangle
                indices.push(base + 1, base + 3, base + 2);
            }
        }
        
        // Create geometry
        const geometry = new THREE.BufferGeometry();
        geometry.setAttribute('position', new THREE.Float32BufferAttribute(vertices, 3));
        geometry.setAttribute('normal', new THREE.Float32BufferAttribute(normals, 3));
        geometry.setAttribute('uv', new THREE.Float32BufferAttribute(uvs, 2));
        geometry.setIndex(indices);
        
        // Compute normals for proper lighting
        geometry.computeVertexNormals();
        
        // Add slight edge beveling for smoother appearance
        const edgesGeometry = new THREE.EdgesGeometry(geometry, 15); // Angle threshold
        const edgesMaterial = new THREE.LineBasicMaterial({ 
            color: 0x2a2a2a, // Slightly darker edge
            linewidth: 1 
        });
        const edges = new THREE.LineSegments(edgesGeometry, edgesMaterial);
        
        // Create group with road and edges
        const group = new THREE.Group();
        const roadMesh = new THREE.Mesh(geometry, this.roadMaterial);
        group.add(roadMesh);
        // Commented out edges for performance - uncomment if you want subtle edge lines
        // group.add(edges);
        
        return roadMesh;
    }
}
