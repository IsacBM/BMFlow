# Changelog

Todas as mudanças relevantes serão registradas neste arquivo. O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/) e o projeto pretende adotar [Versionamento Semântico](https://semver.org/lang/pt-BR/).

## [Não lançado]

## [0.3.0] - 2026-11-06

### Adicionado

- Verificação manual de atualizações na página Sobre, consultando a release mais recente pela API oficial do GitHub sem downloads automáticos.
- Resolvedor universal de aplicativos com prioridade para AppUserModelID, identidade de pacote, classe de janela e executável.
- Suporte dedicado ao Windows Terminal e às janelas Cascadia, agrupando `WindowsTerminal.exe` e `OpenConsole.exe` sob um único ícone.
- Pré-visualizações realistas dos temas da Dock com ícones reais, indicadores de execução, transparência, bordas e sombras.
- Botão de minimizar e nova página Sobre com informações da instalação, links da comunidade e identidade visual do projeto.

### Alterado

- Tela de configurações redesenhada com navegação, cartões, seletores, sliders, switches e barra de rolagem próprios.
- Cor de destaque migrada do coral para azul em toda a interface de configurações.
- Menu principal e submenus da Dock modernizados com fundo cinza-grafite translúcido, checks azuis, ícones Fluent e agrupamentos mais claros.
- Aplicativos da Microsoft Store, PWAs, programas elevados e processos compartilhados como Java e Python agora recebem identidades mais confiáveis.
- Frequência dos monitores de sistema reduzida e atualização por eventos priorizada para diminuir alocações recorrentes.

### Corrigido

- Seletores das configurações não exibem mais texto branco sobre fundo branco.
- Controles de escala agora respeitam o tema escuro e mantêm alinhamento e interação consistentes.
- Aplicativos abertos e não fixados voltam a aparecer quando o espaço de trabalho não está isolado.
- PWAs não são mais agrupadas incorretamente junto ao navegador principal.
- Aplicações diferentes que usam `javaw.exe`, `pythonw.exe` ou outros processos compartilhados não colidem mais no mesmo item.
- Ícones e indicadores das prévias de tema permanecem centralizados na mini Dock.
- Páginas, imagens e caches das configurações são liberados ao fechar ou minimizar a janela, reduzindo o consumo observado de cerca de 205 MB para aproximadamente 59 MB em repouso.

## [0.2.0] - 2026-11-05

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
- Monitores de áudio, rede, bateria, contraste e aplicativos foram consolidados e desacelerados em repouso, reduzindo em cerca de 62% o uso médio de CPU observado com o mascote ativo.

### Corrigido

- Dock e widget de relógio não aparecem mais no Alt+Tab nem na Visão de Tarefas do Windows.
- Submenus do mascote agora usam o mesmo tema fluido e contraste dos demais menus da Dock.
- Ao dormir, o mascote agora descansa exatamente no local onde parou na Dock em vez de ser teleportado para a origem.
- O indicador de execução acompanha o centro visual do ícone durante a onda nas laterais, sem ser ampliado.
- Na borda superior, o indicador de execução aparece acima dos ícones e não se mistura com eles durante a onda.
- A faixa da Dock auto-oculta na borda inferior agora fica mais discreta sem prejudicar a revelação pelo cursor.
- Passar o cursor sobre o mascote não revela mais a Dock enquanto ela está oculta.
- Elementos visuais antigos de aplicativos são liberados ao reconstruir a Dock, evitando retenção cumulativa de memória.

## [0.1.0] - 2026-11-03

### Adicionado

- Primeira versão pública da B.M Flow.
- Dock nativa com aplicativos fixados e janelas em execução.
- Miniaturas DWM, Jump Lists, bandeja e controles do sistema.
- Temas, transparência, auto-ocultação e integração AppBar.
- Espaços de trabalho, widget de relógio, mascotes e temas de cursor.

[Não lançado]: https://github.com/IsacBM/BMFlow/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/IsacBM/BMFlow/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/IsacBM/BMFlow/compare/e5c2b5a...v0.2.0
[0.1.0]: https://github.com/IsacBM/BMFlow/commit/e5c2b5a
