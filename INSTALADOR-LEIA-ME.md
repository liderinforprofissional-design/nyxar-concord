# Criar o instalador do Windows — Nyxar Concord

Gera um **`NyxarConcordSetup-vX.Y.Z.exe`**: um instalador que a pessoa baixa,
dá dois cliques e o app é instalado com atalho no Menu Iniciar e desinstalador.
O app é **self-contained** — funciona mesmo em PCs sem o .NET instalado.

## Pré-requisito (uma vez)
Instale o **Inno Setup 6** (grátis):
```
winget install JRSoftware.InnoSetup
```
(ou baixe em https://jrsoftware.org/isdl.php)

## Gerar o instalador
Duplo-clique em **`criar-instalador.bat`**. Ele:
1. Compila o app (self-contained, 64-bit);
2. Empacota com o Inno Setup.

No fim, o instalador fica em:
```
installer\Output\NyxarConcordSetup-vX.Y.Z.exe
```
(a versão vem do `<Version>` do `.csproj`)

## Detalhes do instalador
- Instala **por usuário** em `%LocalAppData%\Programs\Nyxar Concord` — **não pede
  senha de administrador** (sem UAC), ótimo para testar.
- Cria atalho no Menu Iniciar e, opcionalmente, na Área de trabalho.
- Vem com desinstalador (aparece em "Adicionar ou remover programas").
- Usa o ícone `Assets\nyxar.ico`.

> Se preferir instalar em "Arquivos de Programas" (todos os usuários), no
> `installer\nyxar-concord.iss` troque `PrivilegesRequired=lowest` por `admin` e
> `DefaultDirName={localappdata}\Programs\Nyxar Concord` por `{autopf}\Nyxar Concord`.

## Distribuir com atualização automática
Como o app já verifica atualizações pelo GitHub Releases, o fluxo ideal por versão é:
1. Suba o `<Version>` no `.csproj`.
2. Rode `criar-instalador.bat`.
3. No GitHub, crie o Release `vX.Y.Z` e **anexe o `NyxarConcordSetup-vX.Y.Z.exe`**.

Quem tiver uma versão mais antiga verá o aviso de atualização e baixa o novo instalador.

## Observações
- Se você adicionou as DLLs do FFmpeg (H264), elas entram automaticamente no
  instalador (o build as copia para a saída).
- O instalador é grande (o app self-contained + runtime + FFmpeg somam algumas
  centenas de MB). É o esperado para um app que roda sem depender de nada instalado.
