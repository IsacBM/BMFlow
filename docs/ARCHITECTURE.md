# Arquitetura

## Visão geral

A B.M Flow é uma aplicação desktop WPF compilada como `winexe`. O projeto evita dependências externas e organiza a implementação por domínio dentro de `src/`. Tanto o projeto `BMFlow.csproj` quanto o script que usa o compilador do .NET Framework consomem os mesmos arquivos.

```text
App
 └─ DockWindow
     ├─ descoberta de aplicativos e janelas
     ├─ dock, menus e bandeja
     ├─ AppBar, tela cheia e auto-ocultação
     ├─ PreviewWindow
     ├─ WorkspaceManager
     ├─ DesktopWidgetWindow
     ├─ DockMascot
     ├─ DockSettingsWindow
     └─ serviços do Windows
```

## Componentes principais

| Componente | Caminho | Responsabilidade |
| --- | --- | --- |
| `App` | `src/App.cs` | Ponto de entrada, instância única e captura de falhas não tratadas. |
| `DockWindow` | `src/Dock/` | Janela principal, estado da dock, descoberta de apps, menus, bandeja e ciclo de vida. |
| `WindowCommands` | `src/WindowsInterop/` | Operações de ativar, restaurar, minimizar e fechar janelas. |
| `PreviewWindow` | `src/Preview/` | Miniaturas DWM, agrupamento, paginação e navegação por teclado. |
| `MacCursorService` | `src/Cursors/` | Instalação local dos arquivos, backup, aplicação e restauração do esquema de cursor. |
| `WorkspaceManager` | `src/Workspaces/` | Persistência e seleção de espaços de trabalho. |
| `WorkspaceHudWindow` | `src/Workspaces/` | Retorno visual ao alternar espaços de trabalho. |
| `DesktopWidgetWindow` | `src/Widgets/` | Widget reposicionável de data e hora. |
| `DockMascot` | `src/Mascots/` | Animações, comportamentos e interação dos mascotes. |
| `DockSettingsWindow` | `src/Settings/` | Interface das páginas de configuração. |
| `AudioController` | `src/WindowsInterop/` | Consulta e alteração de volume por interfaces de áudio do Windows. |

## Persistência

Os dados do usuário ficam em `%APPDATA%\BMFlow`:

- `settings.ini`: aparência e comportamento geral;
- `workspaces.ini`: espaços de trabalho e seus aplicativos;
- `Mascots/`: possível pasta de mascotes personalizados;
- `Cursors/`: cópias de temas aplicados pelo serviço de cursor.

A inicialização automática usa `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. O backup do cursor usa `HKCU\Control Panel\Cursors\BMFlow_Original_Backup`.

## Integrações nativas

A aplicação usa P/Invoke para interagir com User32, DWM, Shell, AppBar, monitores, Registro, áudio e automação da interface do Windows. Essas integrações são sensíveis a versão do sistema, escala de exibição, múltiplos monitores e reinicializações do Explorer.

Ao alterar essa área:

- preserve o estado anterior do sistema;
- libere handles, hooks e miniaturas;
- mantenha caminhos de erro sem exceções não tratadas;
- teste encerramento normal e encerramento após falha parcial;
- valide Windows 10 e 11 sempre que possível.

## Concorrência e atualização

Timers do WPF atualizam a interface e coordenam prévias, ocultação e detecção de tela cheia. Timers de thread monitoram volume, bateria, rede e estado de itens fixados. Trabalho fora da thread de UI deve retornar pelo `Dispatcher` e evitar acessar controles diretamente.

## Organização do código

```text
src/
├── App.cs
├── Dock/
├── Settings/
├── Mascots/
├── Workspaces/
├── Preview/
├── Cursors/
├── Widgets/
├── Models/
├── Properties/
└── WindowsInterop/
```

O `DockWindow` ainda concentra a orquestração principal. Novas extrações devem ser incrementais: primeiro isolar serviços sem estado visual e, depois, componentes de interface com limites claros. Cada etapa precisa manter funcionais os dois builds:

```text
build.ps1          -> BMFlow.exe na raiz
BMFlow.csproj      -> bin/Release/net48/BMFlow.exe
```
