# Nyxar Concord — Sincronizar atualizações com o GitHub

Este guia usa dois scripts que ficam na pasta do projeto. É só dar **duplo-clique**.

Repositório: **liderinforprofissional-design/nyxar-concord**

---

## ✅ O que já está pronto no app

Toda vez que o Nyxar Concord abre, ele **verifica sozinho** se há uma versão mais
nova publicada como *release* no GitHub. Se houver, mostra o aviso
"Atualização disponível" e oferece abrir a página de download.
(Isso já está no código: `UpdateService.cs` + `MainWindow.xaml.cs`.)

Ou seja: você não precisa mexer em nada disso. Só precisa **colocar o código no
GitHub** e, no futuro, **publicar cada nova versão** — os passos abaixo.

---

## 1. Conectar ao GitHub (fazer UMA vez)

Duplo-clique em **`1-conectar-github.bat`**.

Ele inicia o Git na pasta, aponta para o seu repositório e envia todo o código.
Ao terminar, seu projeto estará em:
https://github.com/liderinforprofissional-design/nyxar-concord

> Se pedir login do GitHub, faça uma vez com o comando `gh auth login` (ou a
> janela do navegador que aparecer) e rode o script de novo.

---

## 2. Publicar uma atualização (a CADA nova versão)

Sempre que quiser lançar uma versão nova, duplo-clique em
**`2-publicar-versao.bat`** e digite o número (ex.: `0.2.0`).

O script faz tudo sozinho:

1. Atualiza a versão no `NyxarConcord.csproj`.
2. Envia o código novo para o GitHub (commit + push).
3. Compila o app (versão que roda sem instalar o .NET).
4. Compacta num `.zip`.
5. Cria o **Release** `v0.2.0` no GitHub com o `.zip` anexado.

Pronto: qualquer pessoa que abrir o app numa versão menor vê o aviso de
atualização e baixa a nova.

### Regra do número da versão
Use sempre um número **maior** que o anterior (o app compara):
`0.1.0` → `0.2.0` → `0.2.1` → `1.0.0` ...

---

## Precisa instalado (você já tem)

- **Git** — controle de versão
- **GitHub CLI (`gh`)** — cria o release automaticamente
- **.NET SDK** — compila o app

---

## Resumo rápido

| Quando | O que fazer |
|---|---|
| Uma vez, no começo | Rodar `1-conectar-github.bat` |
| Cada nova versão | Rodar `2-publicar-versao.bat` e digitar o número |
| O app conferir updates | **Automático**, toda vez que abre |
