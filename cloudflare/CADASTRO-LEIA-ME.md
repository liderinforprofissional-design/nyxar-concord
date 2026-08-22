# Nyxar Concord — Cadastro central (verificação por e-mail, senha e busca)

Isto liga três recursos novos, todos apoiados no seu Worker do Cloudflare:

- **Cadastro em 2 etapas**: o usuário recebe um **código de 6 dígitos** no e-mail.
- **Esqueci a senha**: envia um código e permite criar uma nova senha.
- **Busca global de usuários**: ao digitar, o app busca qualquer usuário cadastrado.

O e-mail é enviado pelo **Brevo** (grátis, sem precisar de domínio próprio) e os dados
ficam no **Cloudflare D1** (banco grátis).

---

## Parte 1 — Brevo (envio de e-mail)

1. Crie uma conta grátis em https://www.brevo.com (plano free: ~300 e-mails/dia).
2. Verifique um **remetente**: menu **Senders, Domains & Dedicated IPs → Senders → Add a sender**.
   Use o seu e-mail (ex.: `liderpacaja@gmail.com`) e confirme pelo link que chega nele.
3. Pegue a **chave da API**: menu **SMTP & API → API Keys → Generate a new API key**.
   Copie o valor (começa com `xkeysib-...`). **Não** cole no código — vamos guardar como segredo.

> No `wrangler.toml` o remetente já está como `liderpacaja@gmail.com`. Se você verificar
> outro e-mail no Brevo, troque `BREVO_SENDER_EMAIL` lá.

---

## Parte 2 — Banco D1 (uma vez)

Abra o PowerShell **na pasta cloudflare**:

```
cd "C:\Users\Roberto\source\repos\Nyxar Concord\cloudflare"
```

1. Crie o banco:
   ```
   wrangler d1 create nyxar-accounts
   ```
   Ele mostra um bloco com `database_id = "xxxxxxxx-...."`. **Copie esse id.**

2. Cole o id no arquivo `wrangler.toml`, na linha:
   ```
   database_id = "COLE_AQUI_O_DATABASE_ID"
   ```

3. Crie as tabelas (no banco remoto):
   ```
   wrangler d1 execute nyxar-accounts --remote --file=schema.sql
   ```

---

## Parte 3 — Segredos e deploy

1. Guarde a chave do Brevo como segredo (cole o valor quando ele pedir):
   ```
   wrangler secret put BREVO_API_KEY
   ```

2. (Se ainda não tinha feito) o segredo do TURN:
   ```
   wrangler secret put TURN_API_TOKEN
   ```

3. Publique o Worker:
   ```
   wrangler deploy
   ```

Pronto! A URL continua a mesma: `https://nyxar-signal.nyxarp2p.workers.dev`.

---

## Testar rápido

- **Busca** (deve responder um JSON com lista vazia no começo):
  `https://nyxar-signal.nyxarp2p.workers.dev/account/search?q=a`
- **Cadastro**: abra o app, crie uma conta nova → deve chegar o código no e-mail →
  digite o código → conta ativada.
- **Esqueci a senha**: na tela de login, clique em "Esqueci a senha".

---

## Como o app conversa com o servidor

- O app usa a mesma URL do Worker (definida em `WorkerRelay.cs` e `Services/AccountApi.cs`).
  Se algum dia você mudar o subdomínio do Worker, ajuste `BaseUrl` em **AccountApi.cs**
  e as URLs em **WorkerRelay.cs**.
- Senhas: o servidor guarda só o **hash PBKDF2** (nunca a senha em texto). O app também
  mantém um hash local para login rápido/offline neste computador.
- Códigos: 6 dígitos, válidos por 15 minutos, com limite de tentativas.

## Observações de segurança (para evoluir depois)

- Dá para adicionar limite de envios por e-mail/hora (anti-spam) e CAPTCHA.
- A busca hoje retorna nome e @handle de contas verificadas. Se quiser deixar a busca
  opcional (privacidade), dá para criar um campo "aparecer na busca: sim/não".
