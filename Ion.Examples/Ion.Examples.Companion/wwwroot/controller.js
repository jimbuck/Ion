// The phone controller of the Ion companion sample: no framework, no build step. It opens the game's /paddle WebSocket
// (the token, when the game requires one, comes from the URL fragment "#token=...", which browsers never send to the
// server) and sends the stick position {"x": -1..1} while a finger is on the pad. The game pushes the score back.
(() => {
	"use strict";
	const status = document.getElementById("status");
	const pad = document.getElementById("pad");
	const knob = document.getElementById("knob");
	const scoreText = document.getElementById("score");
	const missesText = document.getElementById("misses");
	const token = new URLSearchParams(location.hash.slice(1)).get("token");
	let socket = null;
	let x = 0;
	let sent = null;

	function show(score) {
		scoreText.textContent = score.score;
		missesText.textContent = score.misses;
	}

	function connect() {
		const url = (location.protocol === "https:" ? "wss://" : "ws://") + location.host + "/paddle";
		// Browsers cannot set headers on a WebSocket: the token travels as an offered subprotocol next to "ion".
		socket = new WebSocket(url, token ? ["ion", "bearer." + token] : ["ion"]);
		socket.onopen = () => { status.textContent = "Connected: you are a gamepad."; status.className = "status ok"; sent = null; };
		socket.onmessage = (event) => { try { show(JSON.parse(event.data)); } catch { /* not a score */ } };
		socket.onclose = (event) => {
			status.textContent = event.code === 1013 ? "Every controller slot is taken; retrying..." : "Disconnected; retrying...";
			status.className = "status";
			setTimeout(connect, 1000);
		};
	}

	function setStick(value) {
		x = Math.max(-1, Math.min(1, value));
		knob.style.transform = "translateX(" + (x * (pad.clientWidth / 2 - 36)) + "px)";
	}

	// About 30 messages a second, only when the value changed.
	setInterval(() => {
		if (!socket || socket.readyState !== WebSocket.OPEN) return;
		const rounded = Math.round(x * 100) / 100;
		if (rounded === sent) return;
		sent = rounded;
		socket.send(JSON.stringify({ x: rounded }));
	}, 33);

	function fromPointer(event) {
		const rect = pad.getBoundingClientRect();
		setStick(((event.clientX - rect.left) / rect.width) * 2 - 1);
	}

	pad.addEventListener("pointerdown", (event) => { pad.setPointerCapture(event.pointerId); fromPointer(event); });
	pad.addEventListener("pointermove", (event) => { if (pad.hasPointerCapture(event.pointerId)) fromPointer(event); });
	pad.addEventListener("pointerup", () => setStick(0));
	pad.addEventListener("pointercancel", () => setStick(0));

	const keys = new Set();
	function fromKeys() { setStick((keys.has("ArrowRight") ? 1 : 0) - (keys.has("ArrowLeft") ? 1 : 0)); }
	addEventListener("keydown", (event) => { if (event.key.startsWith("Arrow")) { keys.add(event.key); fromKeys(); } });
	addEventListener("keyup", (event) => { keys.delete(event.key); fromKeys(); });

	fetch("/score").then((response) => response.json()).then(show).catch(() => {});
	connect();
})();
