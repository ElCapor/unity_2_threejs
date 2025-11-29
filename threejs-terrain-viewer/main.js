import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import './style.css';

// Scene setup
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x87ceeb); // Sky blue
scene.fog = new THREE.Fog(0x87ceeb, 100, 2000);

// Camera setup
const camera = new THREE.PerspectiveCamera(
  75,
  window.innerWidth / window.innerHeight,
  0.1,
  5000
);
camera.position.set(0, 100, 200);

// Renderer setup
const canvas = document.getElementById('terrain-canvas');
const renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
renderer.setSize(window.innerWidth, window.innerHeight);
renderer.setPixelRatio(window.devicePixelRatio);
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;

// Controls
const controls = new OrbitControls(camera, renderer.domElement);
controls.enableDamping = true;
controls.dampingFactor = 0.05;
controls.maxPolarAngle = Math.PI / 2; // Don't go below ground

// Lighting
const ambientLight = new THREE.AmbientLight(0xffffff, 0.4);
scene.add(ambientLight);

const directionalLight = new THREE.DirectionalLight(0xffffff, 0.8);
directionalLight.position.set(100, 200, 100);
directionalLight.castShadow = true;
directionalLight.shadow.mapSize.width = 2048;
directionalLight.shadow.mapSize.height = 2048;
directionalLight.shadow.camera.near = 0.5;
directionalLight.shadow.camera.far = 1000;
scene.add(directionalLight);

// Grid helper
const gridHelper = new THREE.GridHelper(1000, 50, 0x444444, 0x222222);
scene.add(gridHelper);

// Terrain variables
let terrainMesh = null;
let wireframeEnabled = false;

// Load and create terrain
async function loadTerrain() {
  try {
    const response = await fetch('/terrain.json');
    const terrainData = await response.json();

    console.log('Loaded terrain data:', terrainData);

    const terrain = terrainData.terrain;
    const resolution = terrain.resolution;
    const size = terrain.size;
    const heightmap = terrain.heightmap.data;

    // Create plane geometry
    const geometry = new THREE.PlaneGeometry(
      size.width,
      size.length,
      resolution - 1,
      resolution - 1
    );

    // Apply heightmap to vertices
    const vertices = geometry.attributes.position.array;

    for (let i = 0; i < heightmap.length; i++) {
      // Z component is height in PlaneGeometry
      vertices[i * 3 + 2] = heightmap[i] * size.height;
    }

    // Rotate to make it horizontal (XZ plane)
    geometry.rotateX(-Math.PI / 2);

    // Recompute normals for proper lighting
    geometry.computeVertexNormals();

    // Create material
    const material = new THREE.MeshStandardMaterial({
      color: 0x4a7c4e,
      roughness: 0.8,
      metalness: 0.2,
      flatShading: false,
      side: THREE.DoubleSide
    });

    // Create mesh
    terrainMesh = new THREE.Mesh(geometry, material);
    terrainMesh.receiveShadow = true;
    terrainMesh.castShadow = true;
    scene.add(terrainMesh);

    // Update camera position based on terrain size
    const maxDim = Math.max(size.width, size.length);
    camera.position.set(maxDim * 0.5, size.height * 2, maxDim * 0.5);
    controls.target.set(0, 0, 0);
    controls.update();

    // Update info display
    updateInfo(terrain);

    console.log('Terrain created successfully');
  } catch (error) {
    console.error('Error loading terrain:', error);
    document.getElementById('info').innerHTML = `
      <p style="color: #ff6b6b;">Error loading terrain!</p>
      <p style="font-size: 11px;">${error.message}</p>
      <p style="font-size: 11px; margin-top: 10px;">Make sure to run the C# exporter first.</p>
    `;
  }
}

function updateInfo(terrain) {
  const info = document.getElementById('info');
  info.innerHTML = `
    <p><strong>Terrain Loaded</strong></p>
    <p>Resolution: ${terrain.resolution}x${terrain.resolution}</p>
    <p>Size: ${terrain.size.width.toFixed(1)} x ${terrain.size.length.toFixed(1)}</p>
    <p>Max Height: ${terrain.size.height.toFixed(1)}</p>
    <p>Vertices: ${terrain.heightmap.data.length.toLocaleString()}</p>
  `;
}

// Wireframe toggle
document.getElementById('toggle-wireframe').addEventListener('click', () => {
  if (terrainMesh) {
    wireframeEnabled = !wireframeEnabled;
    terrainMesh.material.wireframe = wireframeEnabled;
  }
});

// Handle window resize
window.addEventListener('resize', () => {
  camera.aspect = window.innerWidth / window.innerHeight;
  camera.updateProjectionMatrix();
  renderer.setSize(window.innerWidth, window.innerHeight);
});

// Animation loop
function animate() {
  requestAnimationFrame(animate);
  controls.update();
  renderer.render(scene, camera);
}

// Start
loadTerrain();
animate();
