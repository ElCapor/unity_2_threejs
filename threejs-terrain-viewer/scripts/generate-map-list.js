import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const mapsDir = path.join(__dirname, '../public/maps');
const outputFile = path.join(__dirname, '../public/maps.json');

// Ensure public/maps exists
if (!fs.existsSync(mapsDir)) {
    console.log('Creating public/maps directory...');
    fs.mkdirSync(mapsDir, { recursive: true });
}

try {
    const files = fs.readdirSync(mapsDir);
    const mapFiles = files.filter(file => file.endsWith('.json'));

    fs.writeFileSync(outputFile, JSON.stringify(mapFiles, null, 2));
    console.log(`Generated maps.json with ${mapFiles.length} maps.`);
} catch (err) {
    console.error('Error generating map list:', err);
    process.exit(1);
}
