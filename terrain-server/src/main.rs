use axum::{
    Json, Router,
    extract::State,
    extract::ws::{WebSocket, WebSocketUpgrade},
    response::Response,
    routing::get,
    routing::post,
};
use futures_util::{SinkExt, StreamExt};
use serde::{Deserialize, Serialize};
use std::{fs, path::PathBuf, sync::Arc};
use tokio::sync::{RwLock, broadcast};
use tower_http::{cors::CorsLayer, services::ServeDir};

#[tokio::main]
async fn main() {
    // Determine paths relative to where the binary is run (usually terrain-server root)
    // We assume the frontend is in ../threejs-terrain-viewer
    let frontend_path = PathBuf::from("../threejs-terrain-viewer");
    let maps_path = frontend_path.join("public/maps");

    // In a real build, we might serve from ./dist, but for now let's serve the source
    // Note: Serving source directly won't work for bare module imports (like 'three') without a bundler/Vite.
    // So we will assume the user runs 'npm run build' and we serve 'dist'.
    let dist_path = frontend_path.join("dist");

    // Check if dist exists, otherwise warn
    if !dist_path.exists() {
        println!(
            "Warning: {:?} does not exist. Please run 'npm run build' in the frontend directory.",
            dist_path
        );
    }

    // Create broadcast channel for player updates
    let (tx, _) = broadcast::channel::<PlayerUpdate>(100);

    let app = Router::new()
        .route("/api/maps", get(list_maps))
        .route("/api/players", get(get_players))
        .route("/api/players", post(create_player))
        .route("/api/players/move", post(move_player))
        .route("/api/players/clear", post(clear_players))
        .route("/ws", get(websocket_handler))
        .nest_service("/maps", ServeDir::new(maps_path.clone()))
        .fallback_service(ServeDir::new(dist_path))
        .layer(CorsLayer::permissive())
        .with_state(Arc::new(AppState {
            maps_dir: maps_path,
            players: Arc::new(RwLock::new(Vec::new())),
            tx,
        }));

    let addr = "0.0.0.0:3000";
    let listener = tokio::net::TcpListener::bind(addr).await.unwrap();
    println!("Server running on http://localhost:3000");
    axum::serve(listener, app).await.unwrap();
}

#[derive(Clone, Serialize, Deserialize, Debug)]
struct Player {
    id: String,
    x: f32,
    z: f32,
    #[serde(skip_serializing_if = "Option::is_none")]
    y: Option<f32>, // Will be calculated on frontend
}

#[derive(Clone, Serialize, Deserialize, Debug)]
#[serde(tag = "type")]
enum PlayerUpdate {
    #[serde(rename = "player_created")]
    Created { player: Player },
    #[serde(rename = "player_moved")]
    Moved { id: String, x: f32, z: f32 },
    #[serde(rename = "player_removed")]
    Removed { id: String },
    #[serde(rename = "all_cleared")]
    AllCleared,
    #[serde(rename = "initial_state")]
    InitialState { players: Vec<Player> },
}

#[derive(Clone)]
struct AppState {
    maps_dir: PathBuf,
    players: Arc<RwLock<Vec<Player>>>,
    tx: broadcast::Sender<PlayerUpdate>,
}

async fn list_maps(State(state): State<Arc<AppState>>) -> Json<Vec<String>> {
    let mut maps = Vec::new();
    match fs::read_dir(&state.maps_dir) {
        Ok(entries) => {
            for entry in entries.flatten() {
                if let Ok(file_type) = entry.file_type() {
                    if file_type.is_file() {
                        if let Some(name) = entry.file_name().to_str() {
                            if name.ends_with(".json") {
                                maps.push(name.to_string());
                            }
                        }
                    }
                }
            }
        }
        Err(e) => {
            eprintln!("Error reading maps directory {:?}: {}", state.maps_dir, e);
        }
    }
    // Sort by extracted number
    maps.sort_by(|a, b| {
        let extract_num = |s: &str| -> Option<u32> {
            s.split('_')
                .nth(1)
                .and_then(|part| part.parse::<u32>().ok())
        };

        let num_a = extract_num(a);
        let num_b = extract_num(b);

        match (num_a, num_b) {
            (Some(na), Some(nb)) => na.cmp(&nb),
            (Some(_), None) => std::cmp::Ordering::Less,
            (None, Some(_)) => std::cmp::Ordering::Greater,
            (None, None) => a.cmp(b),
        }
    });
    Json(maps)
}

async fn get_players(State(state): State<Arc<AppState>>) -> Json<Vec<Player>> {
    let players = state.players.read().await;
    Json(players.clone())
}

#[derive(Deserialize)]
struct CreatePlayerRequest {
    x: f32,
    z: f32,
}

async fn create_player(
    State(state): State<Arc<AppState>>,
    Json(req): Json<CreatePlayerRequest>,
) -> Json<Player> {
    let mut players = state.players.write().await;
    let id = format!("player_{}", players.len() + 1);
    let player = Player {
        id: id.clone(),
        x: req.x,
        z: req.z,
        y: None,
    };
    players.push(player.clone());
    println!("Created player {} at ({}, {})", id, req.x, req.z);

    // Broadcast the creation
    let _ = state.tx.send(PlayerUpdate::Created {
        player: player.clone(),
    });

    Json(player)
}

#[derive(Deserialize)]
struct MovePlayerRequest {
    id: String,
    x: f32,
    z: f32,
}

async fn move_player(
    State(state): State<Arc<AppState>>,
    Json(req): Json<MovePlayerRequest>,
) -> Json<String> {
    let mut players = state.players.write().await;

    if let Some(player) = players.iter_mut().find(|p| p.id == req.id) {
        player.x = req.x;
        player.z = req.z;

        // Broadcast the move
        let _ = state.tx.send(PlayerUpdate::Moved {
            id: req.id.clone(),
            x: req.x,
            z: req.z,
        });

        Json(format!("Player {} moved to ({}, {})", req.id, req.x, req.z))
    } else {
        Json(format!("Player {} not found", req.id))
    }
}

async fn clear_players(State(state): State<Arc<AppState>>) -> Json<String> {
    let mut players = state.players.write().await;
    players.clear();
    println!("Cleared all players");

    // Broadcast the clear
    let _ = state.tx.send(PlayerUpdate::AllCleared);

    Json("All players cleared".to_string())
}

async fn websocket_handler(ws: WebSocketUpgrade, State(state): State<Arc<AppState>>) -> Response {
    ws.on_upgrade(|socket| handle_socket(socket, state))
}

async fn handle_socket(socket: WebSocket, state: Arc<AppState>) {
    let (mut sender, mut receiver) = socket.split();
    let mut rx = state.tx.subscribe();

    // Send initial state
    let players = state.players.read().await.clone();
    let initial_msg = PlayerUpdate::InitialState { players };
    if let Ok(json) = serde_json::to_string(&initial_msg) {
        let _ = sender
            .send(axum::extract::ws::Message::Text(json.into()))
            .await;
    }

    // Spawn task to send updates to this client
    let mut send_task = tokio::spawn(async move {
        while let Ok(msg) = rx.recv().await {
            if let Ok(json) = serde_json::to_string(&msg) {
                if sender
                    .send(axum::extract::ws::Message::Text(json.into()))
                    .await
                    .is_err()
                {
                    break;
                }
            }
        }
    });

    // Handle incoming messages (for future use)
    let mut recv_task = tokio::spawn(async move {
        while let Some(Ok(_msg)) = receiver.next().await {
            // Handle incoming WebSocket messages if needed
        }
    });

    // Wait for either task to finish
    tokio::select! {
        _ = (&mut send_task) => recv_task.abort(),
        _ = (&mut recv_task) => send_task.abort(),
    }
}
