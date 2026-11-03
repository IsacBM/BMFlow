# Changelog

Todas as mudanças relevantes serão registradas neste arquivo. O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e o projeto pretende adotar [Versionamento Semântico](https://semver.org/lang/pt-BR/).

## [Não lançado]

### Adicionado

- Posicionamento da dock nas bordas inferior, superior, esquerda e direita, com layout vertical nas laterais.
- Seleção de posição nas configurações e no menu de contexto, persistida entre execuções.
- Kit inicial de documentação para colaboração Open Source.
- Guias de arquitetura, desenvolvimento, assets e solução de problemas.
- Templates de issues e pull requests.
- Página de configurações dedicada ao mascote, com aparência, comportamento e tamanho.
- Comportamento orgânico de sono para os mascotes (especialmente a Raposinha com animações de deitar `rest`, soneca `sleep` e despertar `wake up`).
- Ação rápida "Tirar uma Soneca 💤" no menu de contexto do mascote.
- Projeto `BMFlow.csproj` para Visual Studio e builds com o .NET SDK.
- Metadados de produto e versão no executável.
- Guardião local para validar estrutura, documentação, higiene do Git, build, versão e SHA-256.
- Workflow do GitHub Actions para verificar pushes e pull requests e publicar o executável validado.

### Alterado

- Código-fonte separado por domínio dentro de `src/`, preservando o build portátil com `build.ps1`.
- Nas laterais, as prévias de um mesmo aplicativo agora são empilhadas verticalmente e paginadas quando não cabem na tela.
- A onda de ampliação acompanha o eixo vertical nas laterais, crescendo para dentro da tela sem deformar o indicador de execução.
- O mascote agora é exclusivo da borda inferior e é desativado permanentemente ao mover a dock para outra borda.

### Corrigido

- Dock e widget de relógio não aparecem mais no Alt+Tab nem na Visão de Tarefas do Windows.
- Submenus do mascote agora usam o mesmo tema fluido e contraste dos demais menus da Dock.
- Ao dormir, o mascote agora descansa exatamente no local onde parou na Dock em vez de ser teleportado para a origem.
- O indicador de execução acompanha o centro visual do ícone durante a onda nas laterais, sem ser ampliado.
- Na borda superior, o indicador de execução aparece acima dos ícones e não se mistura com eles durante a onda.

## [0.1.0] - 2026-11-03

### Adicionado

- Primeira versão pública da B.M Flow.
- Dock nativa com aplicativos fixados e janelas em execução.
- Miniaturas DWM, Jump Lists, bandeja e controles do sistema.
- Temas, transparência, auto-ocultação e integração AppBar.
- Espaços de trabalho, widget de relógio, mascotes e temas de cursor.

[Não lançado]: https://github.com/IsacBM/DockBar/commits/main
[0.1.0]: https://github.com/IsacBM/DockBar/commit/e5c2b5a
