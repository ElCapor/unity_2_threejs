import './style.css';
import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';

// --- Scene Setup ---
const canvas = document.getElementById('terrain-canvas');
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x0f0f13);
scene.fog = new THREE.FogExp2(0x0f0f13, 0.002);

const camera = new THREE.PerspectiveCamera(60, window.innerWidth / window.innerHeight, 0.1, 10000);
camera.position.set(0, 100, 100);

const renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
renderer.setSize(window.innerWidth, window.innerHeight);
renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));

const controls = new OrbitControls(camera, renderer.domElement);
controls.enableDamping = true;
controls.dampingFactor = 0.05;

// --- Lighting ---
const ambientLight = new THREE.AmbientLight(0x404040, 2);
scene.add(ambientLight);

const dirLight = new THREE.DirectionalLight(0xffffff, 2);
dirLight.position.set(100, 200, 100);
scene.add(dirLight);

// --- Grid ---
const gridSize = 1000;
const gridDivisions = 50;
const gridHelper = new THREE.GridHelper(gridSize, gridDivisions, 0x444444, 0x222222);
scene.add(gridHelper);

// --- State ---
let currentMeshes = [];
let isWireframe = false;
let currentTerrainData = null; // Store current terrain data for height calculation
let playerMeshes = new Map(); // Map of player ID to mesh
let mouseWorldPos = { x: 0, z: 0, valid: false }; // Mouse position in world coordinates

// --- UI Elements ---
const mapSelect = document.getElementById('map-select');
const infoDiv = document.getElementById('info-content');
const toggleWireframeBtn = document.getElementById('toggle-wireframe');
const clearPlayersBtn = document.getElementById('clear-players');

// WebSocket connection
let ws = null;

// --- Logic ---

async function fetchMapList() {
    try {
        // Try fetching from the Rust backend API
        const res = await fetch('/api/maps');
        if (!res.ok) throw new Error('Failed to fetch map list');
        const maps = await res.json();
        return maps;
    } catch (e) {
        console.error('Error fetching map list:', e);
        // Fallback for dev mode without backend proxy
        return [];
    }
}

function clearScene() {
    currentMeshes.forEach(mesh => {
        scene.remove(mesh);
        if (mesh.geometry) mesh.geometry.dispose();
        if (mesh.material) mesh.material.dispose();
    });
    currentMeshes = [];
}

function getHeightAtPosition(x, z, terrainData) {
    if (!terrainData || !terrainData.terrains) return 0;

    // Find which terrain chunk contains this position
    for (const chunk of terrainData.terrains) {
        const minX = chunk.x;
        const maxX = chunk.x + chunk.width;
        const minZ = chunk.z;
        const maxZ = chunk.z + chunk.depth;

        if (x >= minX && x <= maxX && z >= minZ && z <= maxZ) {
            // Position is within this chunk
            // Convert world position to local chunk coordinates
            const localX = x - chunk.x;
            const localZ = z - chunk.z;

            // Convert to heightmap indices
            const resolution = chunk.resolution;
            const xIndex = Math.floor((localX / chunk.width) * (resolution - 1));
            const zIndex = Math.floor((localZ / chunk.depth) * (resolution - 1));

            // Get height from heightmap
            const index = zIndex * resolution + xIndex;
            if (index >= 0 && index < chunk.heightMap.length) {
                return chunk.y + chunk.heightMap[index];
            }
        }
    }

    return 0; // Default height if not on any terrain
}

async function loadMap(mapFile) {
    clearScene();
    infoDiv.innerHTML = '<p class="placeholder">Loading terrain...</p>';

    try {
        // Fetch map data
        // The backend serves static files from 'dist'.
        // However, 'public/maps' are NOT in 'dist' unless copied by build process.
        // Vite copies public/* to dist root. So public/maps/x.json -> dist/maps/x.json
        // So fetching /maps/x.json should work if the server serves dist correctly.
        const res = await fetch(`/maps/${mapFile}`);
        if (!res.ok) throw new Error(`Failed to load ${mapFile}`);

        const data = await res.json();

        if (!data.terrains || data.terrains.length === 0) {
            throw new Error('Map contains no terrain data');
        }

        // Store terrain data for height calculations
        currentTerrainData = data;

        let totalVerts = 0;

        data.terrains.forEach(chunk => {
            // Create Geometry
            const geometry = new THREE.PlaneGeometry(
                chunk.width,
                chunk.depth,
                chunk.resolution - 1,
                chunk.resolution - 1
            );

            // Apply Heights
            const posAttr = geometry.attributes.position;
            for (let i = 0; i < posAttr.count; i++) {
                posAttr.setZ(i, chunk.heightMap[i]);
            }

            // Rotate to XZ plane
            geometry.rotateX(-Math.PI / 2);
            geometry.computeVertexNormals();

            // Material
            const material = new THREE.MeshStandardMaterial({
                color: 0x646cff,
                wireframe: isWireframe,
                side: THREE.DoubleSide,
                flatShading: true
            });

            const mesh = new THREE.Mesh(geometry, material);

            // Position (Unity corner to Three.js center)
            mesh.position.set(
                chunk.x + (chunk.width / 2),
                chunk.y,
                chunk.z + (chunk.depth / 2)
            );

            scene.add(mesh);
            currentMeshes.push(mesh);
            totalVerts += posAttr.count;
        });

        // Center camera on first chunk if needed
        if (data.terrains.length > 0) {
            const first = data.terrains[0];
            // Don't reset camera completely, just look at it? 
            // Actually, let's keep user control, but maybe look at the center of the first chunk
            controls.target.set(
                first.x + first.width / 2,
                first.y,
                first.z + first.depth / 2
            );
        }

        infoDiv.innerHTML = `
            <p><strong>Chunks:</strong> ${data.terrains.length}</p>
            <p><strong>Total Vertices:</strong> ${totalVerts.toLocaleString()}</p>
        `;

    } catch (e) {
        console.error(e);
        infoDiv.innerHTML = `<p style="color: #ff6b6b">Error: ${e.message}</p>`;
    }
}

// --- Event Listeners ---

toggleWireframeBtn.addEventListener('click', () => {
    isWireframe = !isWireframe;
    currentMeshes.forEach(mesh => {
        mesh.material.wireframe = isWireframe;
    });
});

clearPlayersBtn.addEventListener('click', async () => {
    await clearAllPlayers();
    console.log('All players cleared');
});

mapSelect.addEventListener('change', (e) => {
    if (e.target.value) {
        loadMap(e.target.value);
    }
});

window.addEventListener('resize', () => {
    camera.aspect = window.innerWidth / window.innerHeight;
    camera.updateProjectionMatrix();
    renderer.setSize(window.innerWidth, window.innerHeight);
});

// Track mouse position on terrain
canvas.addEventListener('mousemove', (event) => {
    if (!currentTerrainData) {
        mouseWorldPos.valid = false;
        return;
    }

    const rect = canvas.getBoundingClientRect();
    const mouse = new THREE.Vector2(
        ((event.clientX - rect.left) / rect.width) * 2 - 1,
        -((event.clientY - rect.top) / rect.height) * 2 + 1
    );

    const raycaster = new THREE.Raycaster();
    raycaster.setFromCamera(mouse, camera);
    const intersects = raycaster.intersectObjects(currentMeshes);

    if (intersects.length > 0) {
        const point = intersects[0].point;
        mouseWorldPos.x = point.x;
        mouseWorldPos.z = point.z;
        mouseWorldPos.valid = true;
    } else {
        mouseWorldPos.valid = false;
    }
});

// --- Player Management ---

function createPlayerMesh(player) {
    const geometry = new THREE.CylinderGeometry(2, 2, 5, 16);
    const material = new THREE.MeshStandardMaterial({
        color: 0xff6b6b,
        emissive: 0xff0000,
        emissiveIntensity: 0.2,
    });
    const mesh = new THREE.Mesh(geometry, material);

    // Calculate height from terrain
    const y = getHeightAtPosition(player.x, player.z, currentTerrainData);
    mesh.position.set(player.x, y + 2.5, player.z); // +2.5 to center cylinder (height/2)

    scene.add(mesh);
    return mesh;
}

async function fetchPlayers() {
    try {
        const res = await fetch('/api/players');
        if (!res.ok) throw new Error('Failed to fetch players');
        return await res.json();
    } catch (e) {
        console.error('Error fetching players:', e);
        return [];
    }
}

function connectWebSocket() {
    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    const wsUrl = `${protocol}//${window.location.host}/ws`;

    ws = new WebSocket(wsUrl);

    ws.onopen = () => {
        console.log('WebSocket connected');
        document.getElementById('connection-status').textContent = 'Connected';
        document.getElementById('connection-status').style.background = 'rgba(16, 185, 129, 0.2)';
        document.getElementById('connection-status').style.color = '#34d399';
    };

    ws.onmessage = (event) => {
        const update = JSON.parse(event.data);
        handlePlayerUpdate(update);
    };

    ws.onerror = (error) => {
        console.error('WebSocket error:', error);
    };

    ws.onclose = () => {
        console.log('WebSocket disconnected, reconnecting...');
        document.getElementById('connection-status').textContent = 'Reconnecting...';
        document.getElementById('connection-status').style.background = 'rgba(255, 193, 7, 0.2)';
        document.getElementById('connection-status').style.color = '#ffc107';

        // Reconnect after 2 seconds
        setTimeout(connectWebSocket, 2000);
    };
}

function handlePlayerUpdate(update) {
    switch (update.type) {
        case 'initial_state':
            // Clear existing players and load initial state
            for (const [id, mesh] of playerMeshes.entries()) {
                scene.remove(mesh);
                mesh.geometry.dispose();
                mesh.material.dispose();
            }
            playerMeshes.clear();

            // Add all players from initial state
            for (const player of update.players) {
                const mesh = createPlayerMesh(player);
                playerMeshes.set(player.id, mesh);
            }
            break;

        case 'player_created':
            if (!playerMeshes.has(update.player.id)) {
                const mesh = createPlayerMesh(update.player);
                playerMeshes.set(update.player.id, mesh);
                console.log(`Player ${update.player.id} created`);
            }
            break;

        case 'player_moved':
            if (playerMeshes.has(update.id)) {
                const mesh = playerMeshes.get(update.id);
                const y = getHeightAtPosition(update.x, update.z, currentTerrainData);
                mesh.position.set(update.x, y + 2.5, update.z);
            }
            break;

        case 'player_removed':
            if (playerMeshes.has(update.id)) {
                const mesh = playerMeshes.get(update.id);
                scene.remove(mesh);
                mesh.geometry.dispose();
                mesh.material.dispose();
                playerMeshes.delete(update.id);
                console.log(`Player ${update.id} removed`);
            }
            break;

        case 'all_cleared':
            for (const [id, mesh] of playerMeshes.entries()) {
                scene.remove(mesh);
                mesh.geometry.dispose();
                mesh.material.dispose();
            }
            playerMeshes.clear();
            console.log('All players cleared');
            break;
    }
}

async function updatePlayers() {
    // This function is now handled by WebSocket
    // Keeping it for backwards compatibility but it does nothing
}

async function createPlayer(x, z) {
    try {
        const res = await fetch('/api/players', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ x, z })
        });
        if (!res.ok) throw new Error('Failed to create player');
        await updatePlayers();
        return await res.json();
    } catch (e) {
        console.error('Error creating player:', e);
    }
}

async function clearAllPlayers() {
    try {
        const res = await fetch('/api/players/clear', { method: 'POST' });
        if (!res.ok) throw new Error('Failed to clear players');
        await updatePlayers();
    } catch (e) {
        console.error('Error clearing players:', e);
    }
}

// Click to spawn player
canvas.addEventListener('click', async (event) => {
    if (!currentTerrainData) return;

    // Calculate mouse position in normalized device coordinates
    const rect = canvas.getBoundingClientRect();
    const mouse = new THREE.Vector2(
        ((event.clientX - rect.left) / rect.width) * 2 - 1,
        -((event.clientY - rect.top) / rect.height) * 2 + 1
    );

    // Raycast to find intersection with terrain
    const raycaster = new THREE.Raycaster();
    raycaster.setFromCamera(mouse, camera);
    const intersects = raycaster.intersectObjects(currentMeshes);

    if (intersects.length > 0) {
        const point = intersects[0].point;
        await createPlayer(point.x, point.z);
        console.log(`Created player at (${point.x.toFixed(2)}, ${point.z.toFixed(2)})`);
    }
});

// Expose functions to window for console access
window.createPlayer = createPlayer;
window.clearAllPlayers = clearAllPlayers;
window.updatePlayers = updatePlayers;

// --- Init ---

async function init() {
    const maps = await fetchMapList();

    mapSelect.innerHTML = '<option value="" disabled selected>Select a terrain...</option>';

    if (maps.length === 0) {
        const opt = document.createElement('option');
        opt.text = "No maps found";
        opt.disabled = true;
        mapSelect.appendChild(opt);
    } else {
        maps.forEach(map => {
            const opt = document.createElement('option');
            opt.value = map;
            opt.textContent = map;
            mapSelect.appendChild(opt);
        });
    }

    animate();

    // Connect to WebSocket for real-time updates
    connectWebSocket();
}

function animate() {
    requestAnimationFrame(animate);
    controls.update();
    renderer.render(scene, camera);

    // Update position overlay
    updatePositionOverlay();
}

function updatePositionOverlay() {
    const posOverlay = document.getElementById('position-overlay');
    if (posOverlay) {
        const camPos = camera.position;
        const target = controls.target;

        let mouseHtml = '';
        if (mouseWorldPos.valid) {
            mouseHtml = `
            <div class="overlay-row">
                <span class="label">Mouse:</span>
                <span class="value">X: ${mouseWorldPos.x.toFixed(1)} Z: ${mouseWorldPos.z.toFixed(1)}</span>
            </div>`;
        }

        posOverlay.innerHTML = `
            <div class="overlay-row">
                <span class="label">Camera:</span>
                <span class="value">X: ${camPos.x.toFixed(1)} Y: ${camPos.y.toFixed(1)} Z: ${camPos.z.toFixed(1)}</span>
            </div>
            <div class="overlay-row">
                <span class="label">Target:</span>
                <span class="value">X: ${target.x.toFixed(1)} Y: ${target.y.toFixed(1)} Z: ${target.z.toFixed(1)}</span>
            </div>${mouseHtml}
            <div class="overlay-row">
                <span class="label">Players:</span>
                <span class="value">${playerMeshes.size}</span>
            </div>
        `;
    }
}

init();
