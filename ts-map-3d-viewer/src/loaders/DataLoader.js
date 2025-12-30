export class DataLoader {
    constructor() {
        this.loadedChunks = new Set(); // Track which road chunks are loaded
        this.chunkSize = 2000; // Must match C# export
    }
    
    async loadAll(paths) {
        const data = {};
        
        // Load all JSON files in parallel
        const [services, cities, mapInfo, roadsIndex, prefabs, buildings] = await Promise.all([
            this.loadJSON(paths.services).catch(() => []),
            this.loadJSON(paths.cities).catch(() => []),
            this.loadJSON(paths.mapInfo).catch(() => this.getDefaultMapInfo()),
            this.loadJSON(paths.roadsIndex || './data/roads_index.json').catch(() => []),
            this.loadJSON(paths.prefabs || './data/Prefabs.json').catch(() => []),
            this.loadJSON(paths.buildings || './data/Buildings.json').catch(() => [])
        ]);
        
        data.services = services;
        data.cities = cities;
        data.mapInfo = mapInfo;
        data.roadsIndex = roadsIndex; // Store index for lazy loading
        data.roads = []; // Start with empty roads array
        data.prefabs = this.processPrefabs(prefabs);
        data.buildings = buildings;
        
        console.log('?? Loaded data:', {
            services: data.services.length,
            cities: data.cities.length,
            roadChunks: roadsIndex.length,
            prefabs: data.prefabs.length,
            buildings: data.buildings.length
        });
        
        return data;
    }
    
    async loadJSON(path) {
        const response = await fetch(path);
        if (!response.ok) {
            throw new Error(`Failed to load ${path}`);
        }
        return await response.json();
    }
    
    /**
     * Load road chunks based on camera position
     * @param {number} cameraX - Camera X position in game coordinates
     * @param {number} cameraZ - Camera Z position in game coordinates
     * @param {number} viewDistance - How far to load chunks around camera
     * @param {Array} roadsIndex - Road chunk index
     * @returns {Array} - Newly loaded roads
     */
    async loadRoadChunksNear(cameraX, cameraZ, viewDistance, roadsIndex) {
        if (!roadsIndex || roadsIndex.length === 0) return [];
        
        // Calculate which chunks are in view
        const centerChunkX = Math.floor(cameraX / this.chunkSize);
        const centerChunkZ = Math.floor(cameraZ / this.chunkSize);
        const chunkRadius = Math.ceil(viewDistance / this.chunkSize);
        
        const chunksToLoad = [];
        
        // Find chunks within radius
        for (let dx = -chunkRadius; dx <= chunkRadius; dx++) {
            for (let dz = -chunkRadius; dz <= chunkRadius; dz++) {
                const chunkX = centerChunkX + dx;
                const chunkZ = centerChunkZ + dz;
                const chunkKey = `${chunkX}_${chunkZ}`;
                
                if (!this.loadedChunks.has(chunkKey)) {
                    // Find this chunk in the index
                    const chunkInfo = roadsIndex.find(c => 
                        c.chunkX === chunkX && c.chunkZ === chunkZ
                    );
                    
                    if (chunkInfo) {
                        chunksToLoad.push({ key: chunkKey, info: chunkInfo });
                    }
                }
            }
        }
        
        if (chunksToLoad.length === 0) return [];
        
        console.log(`?? Loading ${chunksToLoad.length} road chunks...`);
        
        // Load all needed chunks in parallel
        const roadArrays = await Promise.all(
            chunksToLoad.map(async ({ key, info }) => {
                try {
                    const roads = await this.loadJSON(`./data/roads/${info.file}`);
                    this.loadedChunks.add(key);
                    return roads;
                } catch (error) {
                    console.warn(`Failed to load chunk ${key}:`, error);
                    return [];
                }
            })
        );
        
        // Flatten arrays
        return roadArrays.flat();
    }
    
    processRoads(roads) {
        // TsMap exports roads as list - we may need to process them
        return roads;
    }
    
    processPrefabs(prefabs) {
        // Process prefab data if needed
        return prefabs;
    }
    
    getDefaultMapInfo() {
        // Default bounds if TileMapInfo.json doesn't exist
        return {
            x: { min: -50000, max: 50000 },
            z: { min: -50000, max: 50000 },
            scale: 1
        };
    }
}
