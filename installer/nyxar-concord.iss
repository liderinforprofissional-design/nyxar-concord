; ============================================================
;  Nyxar Concord — script do instalador (Inno Setup 6)
;  Gera um setup.exe que instala o app (self-contained) com
;  atalhos no Menu Iniciar / Área de trabalho e desinstalador.
; ============================================================

#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif
#define MyAppName "Nyxar Concord"
#define MyAppPublisher "Nyxar"
#define MyAppExeName "NyxarConcord.exe"

[Setup]
; ID único do app (NÃO mude entre versões — é o que permite atualizar/desinstalar).
AppId={{9C4B1E7A-2D3F-4A88-B6E1-7F0A5C2D9E10}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\Nyxar Concord
DefaultGroupName=Nyxar Concord
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
OutputDir=Output
OutputBaseFilename=NyxarConcordSetup-v{#MyAppVersion}
SetupIconFile=..\src\NyxarConcord\Assets\nyxar.ico
WizardStyle=modern
; Instalação por usuário — não pede permissão de administrador (UAC).
PrivilegesRequired=lowest
; Atualização no lugar: fecha o app aberto e atualiza. O reabrir automático é feito
; pela entrada [Run] com "Check: WizardSilent" abaixo (mais determinístico que o
; Restart Manager, que não pegava o app porque ele se fecha antes de instalar).
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar um atalho na Área de trabalho"; GroupDescription: "Atalhos adicionais:"

[Files]
; Todo o conteúdo publicado (exe + runtime + DLLs + ffmpeg + assets).
Source: "..\dist\app\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\Nyxar Concord"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Desinstalar Nyxar Concord"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Nyxar Concord"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Instalação interativa (1ª vez): caixa "abrir agora" na tela final.
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir o Nyxar Concord agora"; Flags: nowait postinstall skipifsilent
; Atualização silenciosa (auto-update): reabre o app sozinho ao terminar.
Filename: "{app}\{#MyAppExeName}"; Flags: nowait; Check: WizardSilent

; ============================================================
;  Tema escuro do instalador (combinando com o app) + barra de
;  progresso personalizada no azul do Nyxar Concord.
;  Cores em TColor (formato $00BBGGRR).
; ============================================================
[Code]
const
  clBgDarkest = $20120B;   // #0B1220
  clBgDark    = $2E1A10;   // #101A2E
  clBgCard    = $40271B;   // #1B2740
  clAccent    = $F5961E;   // #1E96F5 (azul do app)
  clTextW     = $FFFFFF;   // branco
  clTextMut   = $BFA99A;   // #9AA9BF (texto suave)

var
  ProgBg, ProgFill: TPanel;

procedure InitializeWizard;
begin
  // ---- Fundo geral escuro ----
  WizardForm.Color := clBgDarkest;

  // ---- Cabeçalho (faixa de cima com título/descrição) ----
  WizardForm.MainPanel.Color := clBgDark;
  WizardForm.PageNameLabel.Font.Color := clTextW;
  WizardForm.PageDescriptionLabel.Font.Color := clTextMut;
  // O ícone padrão do Inno tem fundo branco e destoaria do tema escuro.
  WizardForm.WizardSmallBitmapImage.Visible := False;

  // Linha divisória clara sobre o rodapé: some (fica mais limpo no escuro).
  WizardForm.Bevel.Visible := False;

  // ---- Corpo (páginas) ----
  // (TNewNotebook não tem Color; só as páginas têm — e a página ativa
  //  cobre o notebook inteiro, então basta colorir as páginas.)
  WizardForm.InnerPage.Color := clBgDarkest;
  WizardForm.InstallingPage.Color := clBgDarkest;

  // Textos das páginas de boas-vindas / instalação / fim
  WizardForm.WelcomeLabel1.Font.Color := clTextW;
  WizardForm.WelcomeLabel2.Font.Color := clTextMut;
  WizardForm.StatusLabel.Font.Color := clTextW;
  WizardForm.FilenameLabel.Font.Color := clTextMut;
  WizardForm.FinishedHeadingLabel.Font.Color := clTextW;
  WizardForm.FinishedLabel.Font.Color := clTextMut;

  // ---- Barra de progresso personalizada (tema do app) ----
  // Esconde a barra verde padrão do Windows e desenha a nossa por cima.
  WizardForm.ProgressGauge.Visible := False;

  ProgBg := TPanel.Create(WizardForm);
  ProgBg.Parent := WizardForm.InstallingPage;
  ProgBg.BevelOuter := bvNone;
  ProgBg.BevelInner := bvNone;
  ProgBg.Color := clBgCard;
  ProgBg.Left := WizardForm.ProgressGauge.Left;
  ProgBg.Top := WizardForm.ProgressGauge.Top;
  ProgBg.Width := WizardForm.ProgressGauge.Width;
  ProgBg.Height := WizardForm.ProgressGauge.Height;

  ProgFill := TPanel.Create(WizardForm);
  ProgFill.Parent := ProgBg;
  ProgFill.BevelOuter := bvNone;
  ProgFill.BevelInner := bvNone;
  ProgFill.Color := clAccent;
  ProgFill.Left := 0;
  ProgFill.Top := 0;
  ProgFill.Height := ProgBg.Height;
  ProgFill.Width := 0;
end;

procedure CurInstallProgressChanged(CurProgress, MaxProgress: Integer);
begin
  if (ProgBg <> nil) and (ProgFill <> nil) and (MaxProgress > 0) then
    ProgFill.Width := (ProgBg.Width * CurProgress) div MaxProgress;
end;
