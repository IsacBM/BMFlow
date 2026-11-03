# Arquitetura

## Visão geral

A B.M Flow é uma aplicação desktop WPF compilada como `winexe`. O projeto evita dependências externas e mantém a implementação em `DockBar.cs`, permitindo que o compilador do .NET Framework presente no Windows gere o executável diretamente.

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

| Componente | Responsabilidade |
| --- | --- |
| `App` | Ponto de entrada, instância única e captura de falhas não tratadas. |
| `DockWindow` | Janela principal, estado da dock, descoberta de apps, menus, bandeja e ciclo de vida. |
| `WindowCommands` | Operações de ativar, restaurar, minimizar e fechar janelas. |
| `PreviewWindow` | Miniaturas DWM, agrupamento, paginação e navegação por teclado. |
| `MacCursorService` | Instalação local dos arquivos, backup, aplicação e restauração do esquema de cursor. |
| `WorkspaceManager` | Persistência e seleção de espaços de trabalho. |
| `WorkspaceHudWindow` | Retorno visual ao alternar espaços de trabalho. |
| `DesktopWidgetWindow` | Widget reposicionável de data e hora. |
| `DockMascot` | Animações, comportamentos e interação dos mascotes. |
| `DockSettingsWindow` | Interface das páginas de configuração. |
| `AudioController` | Consulta e alteração de volume por interfaces de áudio do Windows. |

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

## Direção de evolução

O arquivo único reduz a barreira de build, mas aumenta o custo de manutenção. Uma futura separação pode preservar o build simples agrupando classes por domínio:

```text
src/
├── App.cs
├── Dock/
├── WindowsInterop/
├── Preview/
├── Workspaces/
├── Widgets/
├── Mascots/
└── Settings/
```

Essa migração deve ser incremental e manter um build reproduzível antes de cada etapa.
