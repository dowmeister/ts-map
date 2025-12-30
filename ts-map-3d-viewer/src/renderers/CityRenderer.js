import * as THREE from 'three';
import { TextGeometry } from 'three/examples/jsm/geometries/TextGeometry.js';
import { FontLoader } from 'three/examples/jsm/loaders/FontLoader.js';

export class CityRenderer {
    constructor(scene) {
        this.scene = scene;
        this.font = null;
        
        // City label material
        this.textMaterial = new THREE.MeshBasicMaterial({ 
            color: 0xffffff,
            transparent: true,
            opacity: 0.9
        });
        
        this.shadowMaterial = new THREE.MeshBasicMaterial({ 
            color: 0x000000,
            transparent: true,
            opacity: 0.5
        });
    }
    
    async render(cities, container, converter) {
        if (!cities || cities.length === 0) {
            console.warn('No city data to render');
            return;
        }
        
        let cityCount = 0;
        
        for (const city of cities) {
            try {
                const label = this.createCityLabel(city, converter);
                if (label) {
                    container.add(label);
                    cityCount++;
                }
            } catch (error) {
                console.error('Error rendering city:', error);
            }
        }
        
        console.log(`? Rendered ${cityCount} cities`);
    }
    
    createCityLabel(city, converter) {
        const group = new THREE.Group();
        const pos = converter.convert(city.X, city.Y);
        
        // Create city name as sprite (billboard)
        const canvas = document.createElement('canvas');
        const context = canvas.getContext('2d');
        canvas.width = 512;
        canvas.height = 128;
        
        // Draw city name
        context.font = 'Bold 48px Arial';
        context.fillStyle = 'rgba(0, 0, 0, 0.7)';
        context.fillText(city.Name || city.name || 'City', 10, 74); // Shadow
        context.fillStyle = 'white';
        context.fillText(city.Name || city.name || 'City', 8, 72);
        
        // Create sprite
        const texture = new THREE.CanvasTexture(canvas);
        const spriteMaterial = new THREE.SpriteMaterial({ 
            map: texture,
            transparent: true,
            depthTest: false
        });
        const sprite = new THREE.Sprite(spriteMaterial);
        sprite.scale.set(200, 50, 1);
        sprite.position.y = 100; // Elevated above ground
        
        group.add(sprite);
        
        // Position in world
        group.position.set(pos.x, 0, pos.z);
        
        return group;
    }
}
