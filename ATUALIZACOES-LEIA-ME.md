# Atualizações automáticas (via GitHub Releases)

O app verifica, **toda vez que abre**, se existe uma versão mais nova publicada como
*release* no seu repositório do GitHub. Se houver, mostra um aviso e oferece abrir a
página de download. Nada é baixado automaticamente.

## 1. Criar o repositório (uma vez)

1. Acesse https://github.com/new
2. Crie um repositório **público** (ex.: nome `nyxar-concord`).
   - Precisa ser público para o app conseguir ler os releases sem login.
3. Anote o caminho no formato `usuario/repo` (ex.: `nyxarp2p/nyxar-concord`).

## 2. Apontar o app para o repositório (uma vez)

No arquivo `src/NyxarConcord/Services/UpdateService.cs`, ajuste a constante:

```csharp
public const string Repo = "SEU-USUARIO/SEU-REPO";
```

Hoje está `nyxarp2p/nyxar-concord` — **troque para o seu usuário/repositório real**.

## 3. Publicar uma atualização (a cada nova versão)

1. **Suba a versão** em `src/NyxarConcord/NyxarConcord.csproj`:
   ```xml
   <Version>0.2.0</Version>
   ```
   (a versão que está no app é o que ele compara com o GitHub)

2. **Gere o app** (o `.exe` ou um `.zip` com a pasta publicada). Ex.:
   ```
   dotnet publish src\NyxarConcord\NyxarConcord.csproj -c Release -r win-x64 --self-contained true
   ```
   e compacte a pasta de saída num `.zip`.

3. No GitHub, vá em **Releases → Draft a new release**:
   - **Tag**: `v0.2.0` (o "v" é opcional; precisa bater com a versão do passo 1).
   - **Title** e **descrição**: o que mudou (aparece como notas).
   - **Anexe** o `.zip`/`.exe` gerado em *Attach binaries*.
   - **Publish release**.

Pronto. Quando qualquer usuário abrir o app com uma versão menor, ele verá o aviso
"Atualização disponível: v0.2.0" e poderá abrir a página para baixar.

## Como funciona (resumo técnico)

- O app lê a própria versão do assembly (do `<Version>` do csproj).
- Consulta `https://api.github.com/repos/usuario/repo/releases/latest`.
- Compara as versões (ignora sufixos como `-beta`).
- Se a do GitHub for maior, avisa e abre `html_url` do release no navegador.
- Falhas (sem internet, sem release, repo privado) são silenciosas — não atrapalham o uso.

> Observação: a API pública do GitHub permite ~60 verificações por hora por IP,
> o que é mais que suficiente para checar na abertura.
