// ============================================================
//  Nyxar Concord — Cloudflare Worker
//  1) Sinalização WebRTC (troca de SDP/ICE) via WebSocket + Durable Object
//  2) Entrega de credenciais TURN (o secret fica só aqui, nunca no app)
// ============================================================

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    // CORS simples (o app desktop nem precisa, mas ajuda em testes no browser)
    if (request.method === "OPTIONS") {
      return new Response(null, { headers: cors() });
    }

    // --- Credenciais TURN ---
    if (url.pathname === "/turn") {
      return handleTurn(env);
    }

    // --- Sinalização (WebSocket) ---
    if (url.pathname === "/ws") {
      if (request.headers.get("Upgrade") !== "websocket") {
        return new Response("Esperado WebSocket", { status: 426 });
      }
      const room = url.searchParams.get("room") || "global";
      const id = env.SIGNAL.idFromName(room);
      return env.SIGNAL.get(id).fetch(request);
    }

    return new Response("Nyxar Concord signaling online", { status: 200, headers: cors() });
  },
};

function cors() {
  return {
    "Access-Control-Allow-Origin": "*",
    "Access-Control-Allow-Methods": "GET,POST,OPTIONS",
    "Access-Control-Allow-Headers": "Content-Type",
  };
}

// Gera credenciais TURN de curta duração chamando a API do Cloudflare.
async function handleTurn(env) {
  try {
    const res = await fetch(
      `https://rtc.live.cloudflare.com/v1/turn/keys/${env.TURN_KEY_ID}/credentials/generate`,
      {
        method: "POST",
        headers: {
          Authorization: `Bearer ${env.TURN_API_TOKEN}`,
          "Content-Type": "application/json",
        },
        body: JSON.stringify({ ttl: 86400 }), // 24h
      }
    );
    const data = await res.text();
    return new Response(data, {
      status: res.status,
      headers: { ...cors(), "Content-Type": "application/json" },
    });
  } catch (e) {
    return new Response(JSON.stringify({ error: String(e) }), {
      status: 500,
      headers: { ...cors(), "Content-Type": "application/json" },
    });
  }
}

// ------------------------------------------------------------
//  Durable Object: uma "sala" de sinalização (hub de WebSockets)
//  Roteia mensagens: se tiver "to", envia direto; senão, faz broadcast.
// ------------------------------------------------------------
export class SignalRoom {
  constructor(state, env) {
    this.sessions = new Map(); // peerId -> WebSocket
  }

  async fetch(request) {
    const url = new URL(request.url);
    const peer = url.searchParams.get("peer") || crypto.randomUUID();

    const pair = new WebSocketPair();
    const client = pair[0];
    const server = pair[1];
    server.accept();
    this.sessions.set(peer, server);

    // Avisa o recém-chegado quem já está na sala
    const others = [...this.sessions.keys()].filter((p) => p !== peer);
    server.send(JSON.stringify({ type: "peers", peers: others }));
    // Avisa os outros que ele entrou
    this.broadcast(peer, { type: "join", from: peer });

    server.addEventListener("message", (evt) => {
      let msg;
      try {
        msg = JSON.parse(evt.data);
      } catch {
        return;
      }
      msg.from = peer;
      const to = msg.to;
      if (to && this.sessions.has(to)) {
        try { this.sessions.get(to).send(JSON.stringify(msg)); } catch {}
      } else {
        this.broadcast(peer, msg);
      }
    });

    const close = () => {
      this.sessions.delete(peer);
      this.broadcast(peer, { type: "leave", from: peer });
    };
    server.addEventListener("close", close);
    server.addEventListener("error", close);

    return new Response(null, { status: 101, webSocket: client });
  }

  broadcast(exceptPeer, msg) {
    const text = JSON.stringify(msg);
    for (const [pid, ws] of this.sessions) {
      if (pid !== exceptPeer) {
        try { ws.send(text); } catch {}
      }
    }
  }
}
