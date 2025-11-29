import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';

// --- STANDARD SETUP ---
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x87ceeb);
const camera = new THREE.PerspectiveCamera(75, window.innerWidth / window.innerHeight, 0.1, 20000);
const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setSize(window.innerWidth, window.innerHeight);
renderer.shadowMap.enabled = true;
document.body.appendChild(renderer.domElement);

const controls = new OrbitControls(camera, renderer.domElement);
controls.maxPolarAngle = Math.PI / 2; 

// Lights
const ambientLight = new THREE.AmbientLight(0xffffff, 0.5);
scene.add(ambientLight);
const dirLight = new THREE.DirectionalLight(0xffffff, 1);
dirLight.position.set(1000, 2000, 1000); 
scene.add(dirLight);

// --- TERRAIN LOADING LOGIC ---

async function loadFullMap() {
    try {
        const response = await fetch('./TerrainDump.json');
        const data = await response.json();
        
        console.log(`Found ${data.terrains.length} chunks.`);

        // Loop through every terrain object in the JSON array
        data.terrains.forEach((terrainData, index) => {
            createChunk(terrainData);
        });

        // Roughly center camera on the first chunk
        if(data.terrains.length > 0) {
            const first = data.terrains[0];
            camera.position.set(first.x, first.maxHeight + 500, first.z);
            controls.target.set(first.x + first.width/2, 0, first.z + first.depth/2);
            controls.update();
        }

    } catch (e) {
        console.error("Failed to load map:", e);
    }
}

function createChunk(data) {
    const { x, y, z, width, depth, resolution, heightMap } = data;

    // 1. Geometry
    const geometry = new THREE.PlaneGeometry(width, depth, resolution - 1, resolution - 1);
    
    // 2. Apply Heights
    const posAttribute = geometry.attributes.position;
    for (let i = 0; i < posAttribute.count; i++) {
        // Data is already in world units from C#
        posAttribute.setZ(i, heightMap[i] || 0);
    }

    geometry.rotateX(-Math.PI / 2); // Lay flat
    geometry.computeVertexNormals();

    // 3. Material
    // Random color per chunk for debug visibility, or use a texture
    const material = new THREE.MeshStandardMaterial({
        color: 0x44aa88, 
        wireframe: false,
        side: THREE.DoubleSide
    });

    const mesh = new THREE.Mesh(geometry, material);

    // --- COORDINATE FIX ---
    // Unity Transform.Position is the CORNER of the terrain.
    // Three.js Plane Position is the CENTER of the plane.
    // We must shift the center by half the size to align with Unity coordinates.
    
    mesh.position.set(
        x + (width / 2), 
        y,               
        z + (depth / 2) 
    );

    mesh.name = data.name;
    scene.add(mesh);
}

// --- LOOP ---
function animate() {
    requestAnimationFrame(animate);
    controls.update();
    renderer.render(scene, camera);
}

loadFullMap();
animate();

// Resize Handler
window.addEventListener('resize', () => {
    camera.aspect = window.innerWidth / window.innerHeight;
    camera.updateProjectionMatrix();
    renderer.setSize(window.innerWidth, window.innerHeight);
});