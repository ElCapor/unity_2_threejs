import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';

// Setup
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x222222);
const camera = new THREE.PerspectiveCamera(60, window.innerWidth / window.innerHeight, 0.1, 10000);

// Remove old renderer append, use canvas from HTML
const canvas = document.getElementById('terrain-canvas');
const renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
renderer.setSize(window.innerWidth, window.innerHeight);
new OrbitControls(camera, renderer.domElement);

// Lights
const light = new THREE.DirectionalLight(0xffffff, 1);
light.position.set(500, 1000, 500);
scene.add(light);
scene.add(new THREE.AmbientLight(0x404040));

async function loadFullMap() {
    try {
        const res = await fetch('./FullMap.json');
        const data = await res.json();

        console.log(`Loaded ${data.terrains.length} chunks.`);

        data.terrains.forEach(chunk => {
            // 1. Create Geometry
            // Segments = Resolution - 1
            const geometry = new THREE.PlaneGeometry(chunk.width, chunk.depth, chunk.resolution - 1, chunk.resolution - 1);

            // 2. Apply Heights
            const posAttr = geometry.attributes.position;
            for (let i = 0; i < posAttr.count; i++) {
                posAttr.setZ(i, chunk.heightMap[i]);
            }

            // 3. Rotate to ground (XZ)
            geometry.rotateX(-Math.PI / 2);
            geometry.computeVertexNormals();

            // 4. Material
            const material = new THREE.MeshStandardMaterial({
                color: 0x44aa88,
                wireframe: true,
                side: THREE.DoubleSide
            });

            const mesh = new THREE.Mesh(geometry, material);

            // 5. POSITIONING (The Critical Part)
            // Unity Position (chunk.x, chunk.z) is the CORNER of the terrain.
            // Three.js PlaneGeometry position is the CENTER.
            // We must shift by +Width/2 and +Depth/2.

            mesh.position.set(
                chunk.x + (chunk.width / 2),
                chunk.y,
                chunk.z + (chunk.depth / 2)
            );

            scene.add(mesh);
        });

        // Center Camera on first chunk
        if (data.terrains.length > 0) {
            const first = data.terrains[0];
            camera.position.set(first.x, 100, first.z);
        }

    } catch (e) {
        console.error("Error loading map:", e);
    }
}

loadFullMap();

// Map selection logic
const mapSelect = document.getElementById('map-select');
const infoDiv = document.getElementById('info-content');
const toggleWireframeBtn = document.getElementById('toggle-wireframe');
let currentMeshes = [];
let isWireframe = true;

toggleWireframeBtn.addEventListener('click', () => {
    isWireframe = !isWireframe;
    currentMeshes.forEach(mesh => {
        mesh.material.wireframe = isWireframe;
    });
});

async function fetchMapList() {
    // Fetch the generated list of map files
    try {
        const res = await fetch('./maps.json');
        if (!res.ok) throw new Error('Failed to fetch map list');
        const maps = await res.json();
        return maps;
    } catch (e) {
        console.error('Error fetching map list:', e);
        return [];
    }
}

function clearScene() {
    for (const mesh of currentMeshes) {
        scene.remove(mesh);
        mesh.geometry.dispose();
        mesh.material.dispose();
    }
    currentMeshes = [];
}

async function loadMap(mapFile) {
    clearScene();
    clearScene();
    infoDiv.innerHTML = '<p class="placeholder">Loading terrain...</p>';
    try {
        const res = await fetch(`./public/maps/${mapFile}`);
        const data = await res.json();
        if (!data.terrains) throw new Error('No terrains in map');
        infoDiv.innerHTML = `<p>Loaded ${data.terrains.length} chunks.</p>`;
        data.terrains.forEach(chunk => {
            const geometry = new THREE.PlaneGeometry(chunk.width, chunk.depth, chunk.resolution - 1, chunk.resolution - 1);
            const posAttr = geometry.attributes.position;
            for (let i = 0; i < posAttr.count; i++) {
                posAttr.setZ(i, chunk.heightMap[i]);
            }
            geometry.rotateX(-Math.PI / 2);
            geometry.computeVertexNormals();
            const material = new THREE.MeshStandardMaterial({
                color: 0x44aa88,
                wireframe: true,
                side: THREE.DoubleSide
            });
            const mesh = new THREE.Mesh(geometry, material);
            mesh.position.set(
                chunk.x + (chunk.width / 2),
                chunk.y,
                chunk.z + (chunk.depth / 2)
            );
            scene.add(mesh);
            currentMeshes.push(mesh);
        });
        // Center camera if needed
        if (data.terrains.length > 0) {
            const first = data.terrains[0];
            camera.position.set(first.x, 100, first.z);
        }
    } catch (e) {
        infoDiv.innerHTML = `<p>Error loading map: ${e}</p>`;
    }
}

// Populate map select dropdown
fetchMapList().then(maps => {
    for (const map of maps) {
        const opt = document.createElement('option');
        opt.value = map;
        opt.textContent = map;
        mapSelect.appendChild(opt);
    }
});

mapSelect.addEventListener('change', () => {
    if (mapSelect.value) {
        loadMap(mapSelect.value);
    }
});

// Optionally, load the first map by default
// mapSelect.selectedIndex = 1;
// loadMap(mapSelect.value);

function animate() {
    requestAnimationFrame(animate);
    renderer.render(scene, camera);
}

animate();