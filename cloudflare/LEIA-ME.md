# Nyxar Concord — Worker de sinalização + TURN (Cloudflare)

Este Worker faz duas coisas:
1. **Sinalização WebRTC** (troca de SDP/ICE entre os usuários) via WebSocket.
2. **Entrega das credenciais TURN** com segurança — o *secret* da sua TURN Key
   fica só aqui no Cloudflare, nunca dentro do app.

## Pré-requisitos
- Node.js instalado (https://nodejs.org)
- Wrangler (CLI do Cloudflare):
  ```
  npm install -g wrangler
  ```

## Passo a passo (uma vez só)

1. Entre nesta pasta pelo terminal (PowerShell):
   ```
   cd "C:\Users\Carlos Roberto\source\repos\Nyxar Concord\cloudflare"
   ```

2. Faça login no Cloudflare:
   ```
   wrangler login
   ```

3. Defina o **secret** da TURN Key (o "API Token" que apareceu quando você criou
   a key). Rode o comando abaixo e **cole o secret quando ele pedir** — assim ele
   nunca aparece em texto no chat nem no código:
   ```
   wrangler secret put TURN_API_TOKEN
   ```

4. Publique o Worker:
   ```
   wrangler deploy
   ```

5. No fim, o wrangler mostra a URL, algo como:
   ```
   https://nyxar-signal.SEU-SUBDOMINIO.workers.dev
   ```
   **Me mande essa URL.** É com ela que o app vai:
   - Sinalizar: `wss://nyxar-signal.SEU-SUBDOMINIO.workers.dev/ws`
   - Pegar TURN: `https://nyxar-signal.SEU-SUBDOMINIO.workers.dev/turn`

## Testar rápido
Depois do deploy, abra no navegador:
`https://nyxar-signal.SEU-SUBDOMINIO.workers.dev/turn`
Deve retornar um JSON com `iceServers` (urls, username, credential). Se vier isso,
o TURN está funcionando.

## Observações
- **Durable Objects**: usa um Durable Object (SQLite, gratuito no plano free). Se o
  deploy reclamar de plano, me avise que ajusto a abordagem.
- Token ID já configurado no `wrangler.toml`: `832d58ceef40edbd74566f6078ad2314`.
