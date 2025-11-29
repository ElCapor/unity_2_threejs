import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';

// Setup Scene
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x111111); // Dark background

const camera = new THREE.PerspectiveCamera(60, window.innerWidth / window.innerHeight, 0.1, 10000);
const renderer = new THREE.WebGLRenderer({ antialias: true });
renderer.setSize(window.innerWidth, window.innerHeight);
document.body.appendChild(renderer.domElement);

const controls = new OrbitControls(camera, renderer.domElement);

// Add light so we can see depth
const light = new THREE.DirectionalLight(0xffffff, 1);
light.position.set(100, 500, 100);
scene.add(light);
scene.add(new THREE.AmbientLight(0x404040));

async function loadTerrain() {
    try {
        // Replace with your generated filename
        const response = await fetch('./Terrain_1234.json'); 
        const data = await response.json();

        const { width, depth, resolution, heightMap, maxHeight } = data;

        console.log(`Loading Terrain: ${width}x${depth}, Res: ${resolution}`);

        // 1. Create Plane Geometry
        // Segments = Resolution - 1
        const geometry = new THREE.PlaneGeometry(width, depth, resolution - 1, resolution - 1);

        // 2. Apply Heights
        const pos = geometry.attributes.position;
        
        for (let i = 0; i < pos.count; i++) {
            // Data is already in world units from C#
            pos.setZ(i, heightMap[i]);
        }

        // 3. Rotate to lie flat (XZ plane)
        geometry.rotateX(-Math.PI / 2);
        geometry.computeVertexNormals();

        // 4. Create Material (Green Wireframe for checking)
        const material = new THREE.MeshStandardMaterial({
            color: 0x00ff00,
            wireframe: true, // Set to false to see solid mesh
            side: THREE.DoubleSide
        });

        const mesh = new THREE.Mesh(geometry, material);
        
        // Center the mesh visually
        mesh.position.set(0, 0, 0); 
        
        scene.add(mesh);

        // Adjust Camera
        camera.position.set(0, maxHeight * 2, width);
        controls.target.set(0, 0, 0);
        controls.update();

    } catch (e) {
        console.error("Error loading JSON:", e);
    }
}

loadTerrain();

function animate() {
    requestAnimationFrame(animate);
    renderer.render(scene, camera);
}
animate();