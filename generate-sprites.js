#!/usr/bin/env node

/**
 * Generate MapLibre GL sprite sheets from overlay images
 * Groups images into separate sprites: companies, services, roads, misc
 * Requires: npm install sharp
 * 
 * Usage: node generate-sprites.js [game1] [game2] ...
 * Example: node generate-sprites.js ets2 ats promods
 */

const sharp = require('sharp');
const fs = require('fs');
const path = require('path');

const GAMES = process.argv.slice(2).length > 0 
  ? process.argv.slice(2) 
  : ['ets2', 'ats', 'promods'];

const MAP_DATA_DIR = path.join(__dirname, 'map_data');

// Image categorization patterns
const SPRITE_GROUPS = {
  company: /^(company_|dlc_)/i,
  service: /^(service_|map_)/i,
  road: /^(road_|highway_|sign_)/i,
  misc: /.*/  // Catch-all for everything else
};

function categorizeImages(files) {
  const groups = {
    company: [],
    service: [],
    road: [],
    misc: []
  };

  files.forEach(file => {
    const basename = path.basename(file, path.extname(file));
    
    // Check each pattern (order matters - first match wins)
    for (const [group, pattern] of Object.entries(SPRITE_GROUPS)) {
      if (pattern.test(basename)) {
        if (group !== 'misc') {
          groups[group].push(file);
          return;
        }
      }
    }
    
    // If nothing matched, it goes to misc
    groups.misc.push(file);
  });

  return groups;
}

async function generateSprite(images, inputDir, outputPath, scale = 1) {
  const maxHeight = 64 * scale; // Max height for all icons (increased from 32)
  const padding = 2 * scale;
  
  // Load all images and get their metadata
  const imageData = await Promise.all(
    images.map(async (img) => {
      const imgPath = path.join(inputDir, img);
      const metadata = await sharp(imgPath).metadata();
      const name = path.basename(img, path.extname(img));
      
      // Calculate scaled dimensions preserving aspect ratio
      const aspectRatio = metadata.width / metadata.height;
      const scaledHeight = maxHeight;
      const scaledWidth = Math.round(scaledHeight * aspectRatio);
      
      return {
        name,
        path: imgPath,
        width: metadata.width,
        height: metadata.height,
        scaledWidth,
        scaledHeight,
        buffer: null
      };
    })
  );

  // Simple grid packing - calculate dimensions based on actual icon sizes
  const iconsPerRow = Math.ceil(Math.sqrt(imageData.length));
  const maxWidth = Math.max(...imageData.map(img => img.scaledWidth));
  const spriteWidth = iconsPerRow * (maxWidth + padding);
  const spriteHeight = Math.ceil(imageData.length / iconsPerRow) * (maxHeight + padding);

  // Create sprite JSON metadata
  const spriteJson = {};
  const compositeOps = [];

  for (let i = 0; i < imageData.length; i++) {
    const icon = imageData[i];
    const col = i % iconsPerRow;
    const row = Math.floor(i / iconsPerRow);
    const x = col * (maxWidth + padding);
    const y = row * (maxHeight + padding);

    // Resize preserving aspect ratio - only constrain height
    const buffer = await sharp(icon.path)
      .resize(icon.scaledWidth, icon.scaledHeight, { fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 } })
      .toBuffer();

    compositeOps.push({
      input: buffer,
      left: x,
      top: y
    });

    // Add to JSON metadata with actual dimensions
    spriteJson[icon.name] = {
      width: icon.scaledWidth,
      height: icon.scaledHeight,
      x: x,
      y: y,
      pixelRatio: scale
    };
  }

  // Create sprite sheet
  await sharp({
    create: {
      width: spriteWidth,
      height: spriteHeight,
      channels: 4,
      background: { r: 0, g: 0, b: 0, alpha: 0 }
    }
  })
    .composite(compositeOps)
    .png()
    .toFile(outputPath + '.png');

  // Write JSON metadata
  fs.writeFileSync(
    outputPath + '.json',
    JSON.stringify(spriteJson, null, 2)
  );
}

async function main() {
  console.log('Generating grouped sprites for:', GAMES.join(', '));

  for (const game of GAMES) {
    const overlayDir = path.join(MAP_DATA_DIR, game, 'overlay_images');
    const outputDir = path.join(MAP_DATA_DIR, game, 'sprites');
    
    // Create sprites directory if it doesn't exist
    if (!fs.existsSync(outputDir)) {
      fs.mkdirSync(outputDir, { recursive: true });
    }
    
    if (!fs.existsSync(overlayDir)) {
      console.warn(`⚠️  Skipping ${game}: overlay_images directory not found`);
      continue;
    }

    const allImages = fs.readdirSync(overlayDir).filter(f => 
      f.endsWith('.png') || f.endsWith('.jpg') || f.endsWith('.svg')
    );

    if (allImages.length === 0) {
      console.warn(`⚠️  Skipping ${game}: no images found`);
      continue;
    }

    console.log(`\n==> Processing ${game} (${allImages.length} images)...`);

    // Categorize images into groups
    const groups = categorizeImages(allImages);
    
    try {
      // Generate sprites for each group
      for (const [group, images] of Object.entries(groups)) {
        const spritePath = path.join(outputDir, `sprite-${group}`);
        const sprite2xPath = path.join(outputDir, `sprite-${group}@2x`);

        if (images.length === 0) {
          // Write empty sprite files so the map style doesn't get 404s
          fs.writeFileSync(spritePath + '.json', '{}');
          fs.writeFileSync(sprite2xPath + '.json', '{}');
          // Minimal 1×1 transparent PNG
          const emptyPng = await sharp({
            create: { width: 1, height: 1, channels: 4, background: { r: 0, g: 0, b: 0, alpha: 0 } }
          }).png().toBuffer();
          fs.writeFileSync(spritePath + '.png', emptyPng);
          fs.writeFileSync(sprite2xPath + '.png', emptyPng);
          console.log(`  • ${group}: 0 images (empty sprite written)`);
          continue;
        }

        console.log(`  • ${group}: ${images.length} images`);

        // Generate 1x sprite
        await generateSprite(images, overlayDir, spritePath, 1);

        // Generate 2x sprite for retina
        await generateSprite(images, overlayDir, sprite2xPath, 2);
      }

      const groupCount = Object.values(groups).filter(g => g.length > 0).length;
      console.log(`  ✓ Generated ${groupCount} sprite groups`);
    } catch (error) {
      console.error(`  ✗ Failed to generate sprites for ${game}:`, error.message);
      process.exit(1);
    }
  }

  console.log('\n✓ All sprites generated!\n');
  console.log('Sprite groups: company, service, road, misc');
  console.log('\nNext steps:');
  console.log('1. Upload sprites to CDN: ./upload-overlay-images.sh sprites');
  console.log('2. Rebuild React app: docker compose build react-app\n');
}

main().catch(err => {
  console.error('Fatal error:', err);
  process.exit(1);
});
