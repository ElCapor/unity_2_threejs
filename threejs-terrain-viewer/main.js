import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';

// Setup
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x222222);
const camera = new THREE.PerspectiveCamera(60, window.innerWidth / window.innerHeight, 0.1, 10000);
const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setSize(window.innerWidth, window.innerHeight);
document.body.appendChild(renderer.domElement);
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

function animate() {
    requestAnimationFrame(animate);
    renderer.render(scene, camera);
}

animate();