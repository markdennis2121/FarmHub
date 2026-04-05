const canvas = document.getElementById('gameCanvas');
const ctx = canvas.getContext('2d');
const coinCounterEl = document.getElementById('coinCounter');
const playerCountEl = document.getElementById('playerCount');
const clockDisplayEl = document.getElementById('clockDisplay');
const timeOverlayEl = document.getElementById('timeOverlay');
const chatInput = document.getElementById('chatInput');
const chatMessages = document.getElementById('chatMessages');
const interactionPrompt = document.getElementById('interactionPrompt');
const shopOverlay = document.getElementById('shopOverlay');
const btnBuySprinkler = document.getElementById('btnBuySprinkler');

// --- AUTH CHECK ---
const token = localStorage.getItem('farmhub_token');
const myStoredUsername = localStorage.getItem('farmhub_user');

if (!token) {
    window.location.href = 'index.html';
}

// Web Audio API Synth Engine
const audioCtx = new (window.AudioContext || window.webkitAudioContext)();
function playSound(type) {
    if (audioCtx.state === 'suspended') audioCtx.resume();
    const osc = audioCtx.createOscillator();
    const gain = audioCtx.createGain();
    osc.connect(gain); gain.connect(audioCtx.destination);
    
    if (type === 'hoe') {
        osc.type = 'square'; osc.frequency.setValueAtTime(120, audioCtx.currentTime); osc.frequency.exponentialRampToValueAtTime(40, audioCtx.currentTime + 0.1);
        gain.gain.setValueAtTime(0.3, audioCtx.currentTime); gain.gain.exponentialRampToValueAtTime(0.01, audioCtx.currentTime + 0.1);
        osc.start(); osc.stop(audioCtx.currentTime + 0.1);
    } 
    else if (type === 'plant') {
        osc.type = 'sine'; osc.frequency.setValueAtTime(300, audioCtx.currentTime); osc.frequency.exponentialRampToValueAtTime(450, audioCtx.currentTime + 0.05);
        gain.gain.setValueAtTime(0.2, audioCtx.currentTime); gain.gain.exponentialRampToValueAtTime(0.01, audioCtx.currentTime + 0.1);
        osc.start(); osc.stop(audioCtx.currentTime + 0.1);
    } 
    else if (type === 'harvest') {
        osc.type = 'triangle'; osc.frequency.setValueAtTime(600, audioCtx.currentTime); osc.frequency.setValueAtTime(1000, audioCtx.currentTime + 0.1);
        gain.gain.setValueAtTime(0.1, audioCtx.currentTime); gain.gain.exponentialRampToValueAtTime(0.01, audioCtx.currentTime + 0.3);
        osc.start(); osc.stop(audioCtx.currentTime + 0.3);
    }
    else if (type === 'buy') {
        osc.type = 'sine'; osc.frequency.setValueAtTime(800, audioCtx.currentTime); osc.frequency.linearRampToValueAtTime(1600, audioCtx.currentTime + 0.1);
        gain.gain.setValueAtTime(0.1, audioCtx.currentTime); gain.gain.exponentialRampToValueAtTime(0.01, audioCtx.currentTime + 0.2);
        osc.start(); osc.stop(audioCtx.currentTime + 0.2);
    }
    else if (type === 'footstep') {
        osc.type = 'sawtooth'; osc.frequency.setValueAtTime(50, audioCtx.currentTime);
        gain.gain.setValueAtTime(0.03, audioCtx.currentTime); gain.gain.exponentialRampToValueAtTime(0.001, audioCtx.currentTime + 0.05);
        osc.start(); osc.stop(audioCtx.currentTime + 0.05);
    }
}

const TILE_SIZE = 50; 
let GRID_W = 0; let GRID_H = 0; let grid = []; 
let offsetX = 0; let offsetY = 0; 
let targetOffsetX = 0; let targetOffsetY = 0; 

function resize() { canvas.width = window.innerWidth; canvas.height = window.innerHeight; }
window.addEventListener('resize', resize);

const players = new Map();
let myId = null;
const myFarmer = { x: 300, y: 300, color: 'white', speed: 6, coins: 0, username: myStoredUsername };
let activeTool = 1; 
const MERCHANT_POS = { x: 10 * TILE_SIZE, y: 10 * TILE_SIZE };
let nearMerchant = false;

const floatingTexts = [];
class FloatingText {
    constructor(x, y, text, color) { this.x = x; this.y = y; this.text = text; this.color = color; this.life = 1.0; }
    update() { this.y -= 1; this.life -= 0.02; }
    draw(ctx) {
        ctx.globalAlpha = Math.max(0, this.life);
        ctx.fillStyle = this.color; ctx.font = "bold 24px Arial"; ctx.fillText(this.text, this.x, this.y);
        ctx.globalAlpha = 1.0;
    }
}

const keys = { w: false, a: false, s: false, d: false };

window.addEventListener('keydown', e => { 
    if(chatInput === document.activeElement) {
        if(e.key === 'Enter') {
            const txt = chatInput.value.trim();
            if(txt) connection.invoke("SendChat", txt);
            chatInput.value = ''; chatInput.blur();
        }
        return; 
    }
    
    if (e.key === 'Enter') { chatInput.focus(); e.preventDefault(); return; }
    if (e.key === 'Escape') { shopOverlay.classList.remove('show'); return; }

    if (['1','2','3','4'].includes(e.key)) {
        activeTool = parseInt(e.key);
        document.querySelectorAll('.slot').forEach(el => el.classList.remove('active'));
        document.getElementById(`slot-${activeTool}`).classList.add('active');
    }

    if (e.key.toLowerCase() === 'e' && nearMerchant) {
        shopOverlay.classList.add('show');
    }

    if (keys.hasOwnProperty(e.key.toLowerCase())) keys[e.key.toLowerCase()] = true; 
});
window.addEventListener('keyup', e => { if (keys.hasOwnProperty(e.key.toLowerCase())) keys[e.key.toLowerCase()] = false; });

window.addEventListener('mousedown', e => {
    if(e.target.tagName === 'INPUT' || e.target.closest('.shop') || e.target.closest('#chatBox') || e.target.closest('#ui') || e.target.closest('#hotbar')) return;
    if(shopOverlay.classList.contains('show')) { shopOverlay.classList.remove('show'); return; }

    if (audioCtx.state === 'suspended') audioCtx.resume();

    const worldX = e.clientX - offsetX;
    const worldY = e.clientY - offsetY;
    const tx = Math.floor(worldX / TILE_SIZE);
    const ty = Math.floor(worldY / TILE_SIZE);
    
    const dist = Math.hypot(myFarmer.x - worldX, myFarmer.y - worldY);
    if (dist <= 150) { 
        if (grid[ty] && grid[ty][tx] !== undefined) {
            const state = grid[ty][tx];
            if (activeTool === 1 && state === 0) connection.invoke("Interact", tx, ty);
            else if (activeTool === 2 && state === 1) connection.invoke("Interact", tx, ty);
            else if (activeTool === 3 && state === 4) connection.invoke("Interact", tx, ty);
            else if (activeTool === 4 && myFarmer.coins >= 50 && (state === 0 || state === 1)) {
                connection.invoke("BuySprinkler", tx, ty).then(() => playSound('buy'));
            }
        }
    }
});

btnBuySprinkler.addEventListener('click', () => {
    if (myFarmer.coins >= 50) {
        shopOverlay.classList.remove('show');
        activeTool = 4;
        document.querySelectorAll('.slot').forEach(el => el.classList.remove('active'));
        document.getElementById(`slot-4`).classList.add('active');
    }
});

// --- SignalR with JWT ---
const connection = new signalR.HubConnectionBuilder()
    .withUrl("http://localhost:8081/gameHub", {
        accessTokenFactory: () => token // <--- Pass the real cryptographic token
    })
    .withAutomaticReconnect() // <--- Fix "a lot of error" on blips
    .build();

connection.on("initFarm", (serverPlayers, w, h, flatGrid, serverTime, myCoins) => {
    myId = connection.connectionId; GRID_W = w; GRID_H = h;
    myFarmer.coins = myCoins; coinCounterEl.innerText = `🪙 ${myCoins}`;
    
    grid = []; let i = 0;
    for(let y=0; y<h; y++) { grid[y] = []; for(let x=0; x<w; x++) grid[y][x] = flatGrid[i++]; }

    serverPlayers.forEach(p => {
        if (p.id === myId) { myFarmer.color = p.color; myFarmer.x = p.x; myFarmer.y = p.y; } 
        else players.set(p.id, p);
    });

    updateTimeOfDay(serverTime); resize();
});
connection.on("playerJoined", p => { players.set(p.id, p); playerCountEl.innerText = `Farmers: ${players.size+1}`; });
connection.on("playerLeft", id => { players.delete(id); playerCountEl.innerText = `Farmers: ${players.size+1}`; });
connection.on("playerMoved", (id, x, y) => { if (players.has(id)) { const p = players.get(id); p.x = x; p.y = y; } });
connection.on("tilesUpdated", updates => {
    updates.forEach(u => { const [x, y, state] = u; if (grid[y] && grid[y][x] !== undefined) grid[y][x] = state; });
});
connection.on("coinsUpdated", coins => {
    const diff = coins - myFarmer.coins;
    myFarmer.coins = coins; coinCounterEl.innerText = `🪙 ${coins}`;
    if (diff > 0) floatingTexts.push(new FloatingText(myFarmer.x, myFarmer.y - 40, `+${diff} 🪙`, '#facc15'));
});
connection.on("playSound", (type, originalState) => {
    if(originalState===0) playSound('hoe'); else if(originalState===1) playSound('plant'); else if(originalState===4) playSound('harvest');
});
connection.on("timeUpdated", updateTimeOfDay);

// --- PERSISTENT HISTORY HANDLER ---
connection.on("chatHistory", (history) => {
    chatMessages.innerHTML = ''; // <--- DE-DUPLICATION: Clear before loading!
    history.forEach(item => {
        const div = document.createElement('div'); div.className = 'chat-line';
        div.innerHTML = `<span class="chat-color" style="background:${item.color};"></span> <span style="font-weight:bold; color:#fde047;">${item.username}:</span> <span>${item.message}</span>`;
        chatMessages.appendChild(div);
    });
    chatMessages.scrollTop = chatMessages.scrollHeight;
});

// CRITICAL UPDATE: Handle real authenticated usernames in chat
connection.on("receiveChat", (color, username, msg) => {
    const div = document.createElement('div'); div.className = 'chat-line';
    div.innerHTML = `<span class="chat-color" style="background:${color};"></span> <span style="font-weight:bold; color:#fde047;">${username}:</span> <span>${msg}</span>`;
    chatMessages.appendChild(div); chatMessages.scrollTop = chatMessages.scrollHeight;
});

function updateTimeOfDay(t) {
    let hours = Math.floor(t); let mins = Math.floor((t - hours) * 60); const ampm = hours >= 12 ? 'PM' : 'AM';
    let dH = hours % 12; if (dH === 0) dH = 12;
    clockDisplayEl.innerText = `Time: ${dH}:${mins.toString().padStart(2, '0')} ${ampm}`;
    if (t >= 18 || t < 5) timeOverlayEl.style.backgroundColor = 'rgba(10, 10, 50, 0.6)'; 
    else if (t >= 5 && t < 8) timeOverlayEl.style.backgroundColor = 'rgba(255, 150, 50, 0.2)';
    else if (t >= 16 && t < 18) timeOverlayEl.style.backgroundColor = 'rgba(255, 100, 0, 0.3)';
    else timeOverlayEl.style.backgroundColor = 'rgba(0,0,0,0)';
}

connection.start().then(() => console.log("FarmHub Auth Success!")).catch(() => window.location.href = 'index.html');

let lastNetUpdate = 0; let lastFootstep = 0;
function update() {
    if(chatInput === document.activeElement) return;

    let dx = 0, dy = 0;
    if (keys.w) dy -= myFarmer.speed; if (keys.s) dy += myFarmer.speed;
    if (keys.a) dx -= myFarmer.speed; if (keys.d) dx += myFarmer.speed;

    if (dx !== 0 && dy !== 0) { const len = Math.sqrt(dx*dx + dy*dy); dx = (dx/len) * myFarmer.speed; dy = (dy/len) * myFarmer.speed; }

    myFarmer.x += dx; myFarmer.y += dy;
    myFarmer.x = Math.max(0, Math.min(myFarmer.x, GRID_W * TILE_SIZE - 1));
    myFarmer.y = Math.max(0, Math.min(myFarmer.y, GRID_H * TILE_SIZE - 1));

    targetOffsetX = (window.innerWidth / 2) - myFarmer.x;
    targetOffsetY = (window.innerHeight / 2) - myFarmer.y;
    offsetX += (targetOffsetX - offsetX) * 0.1;
    offsetY += (targetOffsetY - offsetY) * 0.1;

    const now = Date.now();
    if ((dx !== 0 || dy !== 0) && now - lastFootstep > 250) { playSound('footstep'); lastFootstep = now; }

    if (Math.hypot(myFarmer.x - MERCHANT_POS.x, myFarmer.y - MERCHANT_POS.y) < 100 && !shopOverlay.classList.contains('show')) {
        nearMerchant = true; interactionPrompt.style.display = 'block';
    } else { nearMerchant = false; interactionPrompt.style.display = 'none'; }

    if (connection.state === "Connected" && (dx !== 0 || dy !== 0) && now - lastNetUpdate > 33) {
        connection.invoke("UpdatePosition", myFarmer.x, myFarmer.y); lastNetUpdate = now;
    }

    for (let i = floatingTexts.length - 1; i >= 0; i--) {
        floatingTexts[i].update();
        if (floatingTexts[i].life <= 0) floatingTexts.splice(i, 1);
    }
}

function drawTile(x, y, state) {
    const px = x * TILE_SIZE; const py = y * TILE_SIZE;
    if (px + offsetX < -TILE_SIZE || px + offsetX > window.innerWidth || py + offsetY < -TILE_SIZE || py + offsetY > window.innerHeight) return;

    ctx.fillStyle = "#a3e635"; ctx.fillRect(px, py, TILE_SIZE, TILE_SIZE);
    
    ctx.font = "34px Arial"; ctx.textAlign = "center"; ctx.textBaseline = "middle";
    const cx = px + TILE_SIZE/2; const cy = py + TILE_SIZE/2;

    if (state >= 1) {
        ctx.fillStyle = "#78350f"; ctx.fillRect(px, py, TILE_SIZE, TILE_SIZE);
        ctx.strokeStyle = "#451a03"; ctx.lineWidth = 2; ctx.strokeRect(px+1, py+1, TILE_SIZE-2, TILE_SIZE-2);
        
        if (state === 2) ctx.fillText("🌱", cx, cy + 4);
        if (state === 3) ctx.fillText("🌿", cx, cy + 4);
        if (state === 4) ctx.fillText("🍅", cx, cy + 4);
        if (state === 5) {
            ctx.fillText("🚿", cx, cy + 4);
            ctx.strokeStyle = "rgba(56, 189, 248, 0.4)"; ctx.beginPath(); ctx.arc(cx, cy, Date.now() % 1000 / 20, 0, Math.PI*2); ctx.stroke();
        }
    } else if ((x * 13 + y * 7) % 5 === 0) { ctx.fillText("🌾", cx, cy + 4); }
}

function drawEntity(x, y, color, username) {
    ctx.font = "40px Arial"; ctx.textAlign = "center"; ctx.textBaseline = "middle";
    ctx.fillStyle = "rgba(0,0,0,0.3)"; ctx.beginPath(); ctx.ellipse(x, y + 20, 16, 6, 0, 0, Math.PI*2); ctx.fill();
    ctx.fillText("👨‍🌾", x, y);
    
    // Username Label
    ctx.font = "bold 14px Inter";
    ctx.fillStyle = "white";
    ctx.fillText(username, x, y - 45);
    
    ctx.strokeStyle = color; ctx.lineWidth = 3; ctx.beginPath(); ctx.arc(x, y, 22, 0, Math.PI*2); ctx.stroke();
}

function draw() {
    ctx.fillStyle = '#0ea5e9'; ctx.fillRect(0, 0, canvas.width, canvas.height); 

    ctx.save();
    ctx.translate(offsetX, offsetY);

    ctx.fillStyle = "#15803d"; ctx.fillRect(-10, -10, (GRID_W * TILE_SIZE)+20, (GRID_H * TILE_SIZE)+20);

    if (grid.length > 0) { for(let py=0; py<GRID_H; py++) { for(let px=0; px<GRID_W; px++) { drawTile(px, py, grid[py][px]); }}}

    ctx.fillText("🧙‍♂️", MERCHANT_POS.x, MERCHANT_POS.y);
    ctx.fillStyle = "white"; ctx.font = "bold 14px Inter"; ctx.fillText("EMERALD MERCHANT", MERCHANT_POS.x, MERCHANT_POS.y - 45);

    players.forEach(p => drawEntity(p.x, p.y, p.color, p.username));
    if (myId) drawEntity(myFarmer.x, myFarmer.y, myFarmer.color, myFarmer.username);

    floatingTexts.forEach(ft => ft.draw(ctx));
    ctx.restore();
}

function loop() { update(); draw(); requestAnimationFrame(loop); }
loop();
