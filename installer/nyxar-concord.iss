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
