export class CoordinateConverter {
    constructor(mapInfo) {
        this.mapInfo = mapInfo;
        
        // Calculate scale to fit map into reasonable 3D space
        const mapWidth = mapInfo.x.max - mapInfo.x.min;
        const mapHeight = mapInfo.z.max - mapInfo.z.min;
        const maxDimension = Math.max(mapWidth, mapHeight);
        
        // Scale map to fit in ~100,000 units
        this.scale = 100000 / maxDimension;
        
        // Center offset
        this.offsetX = (mapInfo.x.min + mapInfo.x.max) / 2;
        this.offsetZ = (mapInfo.z.min + mapInfo.z.max) / 2;
    }
    
    convert(gameX, gameZ, elevation = 0) {
        // Convert game coordinates to Three.js world space
        // Game uses X/Z plane, Three.js uses X/Y/Z with Y as up
        
        const x = (gameX - this.offsetX) * this.scale;
        const y = elevation;
        const z = (gameZ - this.offsetZ) * this.scale;
        
        return { x, y, z };
    }
    
    convertPath(points) {
        // Convert an array of game coordinate points
        return points.map(p => this.convert(p.X || p.x, p.Z || p.z || p.Y || p.y));
    }
}
