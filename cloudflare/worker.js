// ============================================================
//  Nyxar Concord — Cloudflare Worker
//  1) Sinalização WebRTC (troca de SDP/ICE) via WebSocket + Durable Object
//  2) Entrega de credenciais TURN (o secret fica só aqui, nunca no app)
//  3) API de CONTAS: cadastro com verificação por e-mail (código de 6 dígitos),
//     login, "esqueci a senha" e busca global de usuários.  (D1 + Brevo)
// ============================================================

export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    // CORS simples (o app desktop nem precisa, mas ajuda em testes no browser)
    if (request.method === "OPTIONS") {
      return new Response(null, { headers: cors() });
    }

    // --- API de contas ---
    if (url.pathname.startsWith("/account/")) {
      return handleAccount(url.pathname, request, env);
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

// ============================================================
//  Utilidades
// ============================================================
function cors() {
  return {
    "Access-Control-Allow-Origin": "*",
    "Access-Control-Allow-Methods": "GET,POST,OPTIONS",
    "Access-Control-Allow-Headers": "Content-Type",
  };
}

function json(obj, status = 200) {
  return new Response(JSON.stringify(obj), {
    status,
    headers: { ...cors(), "Content-Type": "application/json" },
  });
}

function bytesToHex(bytes) {
  return [...bytes].map((b) => b.toString(16).padStart(2, "0")).join("");
}
function bytesToBase64(bytes) {
  let bin = "";
  for (const b of bytes) bin += String.fromCharCode(b);
  return btoa(bin);
}
function base64ToBytes(b64) {
  const bin = atob(b64);
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return out;
}

// Hash de senha com PBKDF2 (SHA-256, 100k iterações). Nunca guardamos a senha crua.
async function hashPassword(password, saltB64) {
  const enc = new TextEncoder();
  const salt = saltB64 ? base64ToBytes(saltB64) : crypto.getRandomValues(new Uint8Array(16));
  const keyMaterial = await crypto.subtle.importKey(
    "raw", enc.encode(password), "PBKDF2", false, ["deriveBits"]
  );
  const bits = await crypto.subtle.deriveBits(
    { name: "PBKDF2", salt, iterations: 100000, hash: "SHA-256" },
    keyMaterial, 256
  );
  return { hash: bytesToHex(new Uint8Array(bits)), salt: bytesToBase64(salt) };
}

// Código numérico de 6 dígitos.
function genCode() {
  const n = crypto.getRandomValues(new Uint32Array(1))[0] % 1000000;
  return String(n).padStart(6, "0");
}

function now() { return Math.floor(Date.now() / 1000); }
const CODE_TTL = 15 * 60; // 15 minutos
const MAX_TRIES = 5;

// Gera @handle curto e legível.
function makeHandle(username) {
  let slug = (username || "usuario").toLowerCase().replace(/[^a-z0-9]/g, "");
  if (!slug) slug = "usuario";
  if (slug.length > 12) slug = slug.slice(0, 12);
  const n = 1000 + (crypto.getRandomValues(new Uint32Array(1))[0] % 9000);
  return `@${slug}-${n}`;
}

// Envio de e-mail transacional via Brevo.
async function sendEmail(env, toEmail, subject, html) {
  if (!env.BREVO_API_KEY) return false;
  try {
    const res = await fetch("https://api.brevo.com/v3/smtp/email", {
      method: "POST",
      headers: {
        "api-key": env.BREVO_API_KEY,
        "Content-Type": "application/json",
        accept: "application/json",
      },
      body: JSON.stringify({
        sender: { email: env.BREVO_SENDER_EMAIL, name: env.BREVO_SENDER_NAME || "Nyxar Concord" },
        to: [{ email: toEmail }],
        subject,
        htmlContent: html,
      }),
    });
    return res.ok;
  } catch {
    return false;
  }
}

function codeEmailHtml(code, purpose) {
  return `
  <div style="font-family:Segoe UI,Arial,sans-serif;max-width:440px;margin:auto">
    <h2 style="color:#5865F2">Nyxar Concord</h2>
    <p>${purpose}</p>
    <p style="font-size:32px;font-weight:bold;letter-spacing:6px;background:#f2f3f8;
              padding:16px;border-radius:10px;text-align:center">${code}</p>
    <p style="color:#666;font-size:13px">Este código vale por 15 minutos. Se você não pediu isto, ignore este e-mail.</p>
  </div>`;
}

// ============================================================
//  Roteador da API de contas
// ============================================================
async function handleAccount(path, request, env) {
  if (!env.DB) {
    return json({ ok: false, error: "Banco de dados (D1) não configurado no Worker." }, 500);
  }
  let body = {};
  if (request.method === "POST") {
    try { body = await request.json(); } catch { body = {}; }
  }

  try {
    switch (path) {
      case "/account/register/start":  return registerStart(body, env);
      case "/account/register/verify": return registerVerify(body, env);
      case "/account/login":           return login(body, env);
      case "/account/forgot":          return forgot(body, env);
      case "/account/reset":           return reset(body, env);
      case "/account/search":          return search(new URL(request.url), env);
      default: return json({ ok: false, error: "Rota não encontrada." }, 404);
    }
  } catch (e) {
    return json({ ok: false, error: "Erro interno: " + String(e) }, 500);
  }
}

// 1) Início do cadastro: valida, guarda pendente e envia o código.
async function registerStart(body, env) {
  const email = String(body.email || "").trim().toLowerCase();
  const username = String(body.username || "").trim();
  const password = String(body.password || "");

  if (!email.includes("@")) return json({ ok: false, error: "E-mail inválido." }, 400);
  if (username.length < 3)  return json({ ok: false, error: "Nome de usuário muito curto." }, 400);
  if (password.length < 4)  return json({ ok: false, error: "Senha muito curta." }, 400);

  // Já existe conta VERIFICADA com este e-mail?
  const byEmail = await env.DB.prepare(
    "SELECT verified FROM accounts WHERE email = ?").bind(email).first();
  if (byEmail && byEmail.verified === 1)
    return json({ ok: false, error: "Este e-mail já está cadastrado. Tente entrar ou recuperar a senha." }, 409);

  // Nome de usuário já usado por conta VERIFICADA (de outro e-mail)?
  const byUser = await env.DB.prepare(
    "SELECT email FROM accounts WHERE username = ? AND verified = 1").bind(username).first();
  if (byUser && byUser.email !== email)
    return json({ ok: false, error: "Este nome de usuário já está em uso." }, 409);

  // Remove pendências antigas com o mesmo nome de usuário (de outro e-mail).
  await env.DB.prepare(
    "DELETE FROM accounts WHERE username = ? AND verified = 0 AND email <> ?"
  ).bind(username, email).run();

  const { hash, salt } = await hashPassword(password);
  const handle = makeHandle(username);

  await env.DB.prepare(
    `INSERT INTO accounts (email, username, handle, display_name, pass_hash, pass_salt, verified, created_at)
     VALUES (?, ?, ?, ?, ?, ?, 0, ?)
     ON CONFLICT(email) DO UPDATE SET
       username=excluded.username, handle=excluded.handle, display_name=excluded.display_name,
       pass_hash=excluded.pass_hash, pass_salt=excluded.pass_salt`
  ).bind(email, username, handle, username, hash, salt, now()).run();

  const code = genCode();
  await env.DB.prepare(
    `INSERT INTO codes (email, kind, code, expires_at, tries) VALUES (?, 'verify', ?, ?, 0)
     ON CONFLICT(email, kind) DO UPDATE SET code=excluded.code, expires_at=excluded.expires_at, tries=0`
  ).bind(email, code, now() + CODE_TTL).run();

  const sent = await sendEmail(env, email, "Seu código de verificação — Nyxar Concord",
    codeEmailHtml(code, "Use o código abaixo para confirmar seu e-mail e ativar sua conta:"));
  if (!sent) return json({ ok: false, error: "Não foi possível enviar o e-mail. Verifique a configuração do Brevo." }, 502);

  return json({ ok: true });
}

// 2) Confirma o código e ativa a conta.
async function registerVerify(body, env) {
  const email = String(body.email || "").trim().toLowerCase();
  const code = String(body.code || "").trim();

  const row = await env.DB.prepare(
    "SELECT code, expires_at, tries FROM codes WHERE email = ? AND kind = 'verify'").bind(email).first();
  if (!row) return json({ ok: false, error: "Nenhum código pendente. Cadastre-se novamente." }, 400);
  if (row.tries >= MAX_TRIES) return json({ ok: false, error: "Muitas tentativas. Cadastre-se novamente." }, 429);
  if (now() > row.expires_at)  return json({ ok: false, error: "Código expirado. Cadastre-se novamente." }, 400);

  if (code !== row.code) {
    await env.DB.prepare("UPDATE codes SET tries = tries + 1 WHERE email = ? AND kind = 'verify'").bind(email).run();
    return json({ ok: false, error: "Código incorreto." }, 400);
  }

  await env.DB.prepare("UPDATE accounts SET verified = 1 WHERE email = ?").bind(email).run();
  await env.DB.prepare("DELETE FROM codes WHERE email = ? AND kind = 'verify'").bind(email).run();

  const acc = await env.DB.prepare(
    "SELECT email, username, handle, display_name FROM accounts WHERE email = ?").bind(email).first();
  return json({ ok: true, account: dto(acc) });
}

// 3) Login pelo servidor (para entrar em outro computador).
async function login(body, env) {
  const loginId = String(body.login || "").trim().toLowerCase();
  const password = String(body.password || "");

  const acc = await env.DB.prepare(
    `SELECT email, username, handle, display_name, pass_hash, pass_salt
     FROM accounts WHERE verified = 1 AND (lower(email) = ? OR lower(username) = ?)`
  ).bind(loginId, loginId).first();
  if (!acc) return json({ ok: false, error: "Conta não encontrada." }, 404);

  const { hash } = await hashPassword(password, acc.pass_salt);
  if (hash !== acc.pass_hash) return json({ ok: false, error: "Senha incorreta." }, 401);

  return json({ ok: true, account: dto(acc) });
}

// 4) Esqueci a senha: envia código de redefinição (não revela se o e-mail existe).
async function forgot(body, env) {
  const email = String(body.email || "").trim().toLowerCase();
  const acc = await env.DB.prepare(
    "SELECT email FROM accounts WHERE email = ? AND verified = 1").bind(email).first();

  if (acc) {
    const code = genCode();
    await env.DB.prepare(
      `INSERT INTO codes (email, kind, code, expires_at, tries) VALUES (?, 'reset', ?, ?, 0)
       ON CONFLICT(email, kind) DO UPDATE SET code=excluded.code, expires_at=excluded.expires_at, tries=0`
    ).bind(email, code, now() + CODE_TTL).run();
    await sendEmail(env, email, "Redefinição de senha — Nyxar Concord",
      codeEmailHtml(code, "Use o código abaixo para criar uma nova senha:"));
  }
  // Sempre responde ok (segurança: não vaza quais e-mails existem).
  return json({ ok: true });
}

// 5) Redefine a senha com o código.
async function reset(body, env) {
  const email = String(body.email || "").trim().toLowerCase();
  const code = String(body.code || "").trim();
  const password = String(body.password || "");
  if (password.length < 4) return json({ ok: false, error: "Senha muito curta." }, 400);

  const row = await env.DB.prepare(
    "SELECT code, expires_at, tries FROM codes WHERE email = ? AND kind = 'reset'").bind(email).first();
  if (!row) return json({ ok: false, error: "Nenhum pedido de redefinição. Comece de novo." }, 400);
  if (row.tries >= MAX_TRIES) return json({ ok: false, error: "Muitas tentativas. Comece de novo." }, 429);
  if (now() > row.expires_at)  return json({ ok: false, error: "Código expirado. Comece de novo." }, 400);
  if (code !== row.code) {
    await env.DB.prepare("UPDATE codes SET tries = tries + 1 WHERE email = ? AND kind = 'reset'").bind(email).run();
    return json({ ok: false, error: "Código incorreto." }, 400);
  }

  const { hash, salt } = await hashPassword(password);
  await env.DB.prepare("UPDATE accounts SET pass_hash = ?, pass_salt = ? WHERE email = ?")
    .bind(hash, salt, email).run();
  await env.DB.prepare("DELETE FROM codes WHERE email = ? AND kind = 'reset'").bind(email).run();
  return json({ ok: true });
}

// 6) Busca global de usuários (para a busca ao vivo do app).
async function search(url, env) {
  const q = (url.searchParams.get("q") || "").trim();
  if (q.length < 1) return json({ ok: true, results: [] });
  const like = "%" + q.toLowerCase() + "%";
  const rs = await env.DB.prepare(
    `SELECT username, handle, display_name FROM accounts
     WHERE verified = 1 AND (lower(username) LIKE ? OR lower(handle) LIKE ? OR lower(display_name) LIKE ?)
     ORDER BY username LIMIT 20`
  ).bind(like, like, like).all();
  return json({ ok: true, results: (rs.results || []).map(dto) });
}

function dto(row) {
  return row ? {
    email: row.email,
    username: row.username,
    handle: row.handle,
    displayName: row.display_name,
  } : null;
}

// ============================================================
//  TURN
// ============================================================
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

    const others = [...this.sessions.keys()].filter((p) => p !== peer);
    server.send(JSON.stringify({ type: "peers", peers: others }));
    this.broadcast(peer, { type: "join", from: peer });

    server.addEventListener("message", (evt) => {
      let msg;
      try { msg = JSON.parse(evt.data); } catch { return; }
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
