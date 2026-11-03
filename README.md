# B.M Flow

Uma Dock nativa e fluida para Windows 10/11, integrada aos aplicativos, janelas e painéis do sistema.

> Sua área de trabalho em movimento.

## Compilar

Abra o PowerShell nesta pasta e execute:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

O script usa o compilador do .NET Framework que já acompanha o Windows e gera `BMFlow.exe`.

## Usar

Execute `BMFlow.exe`. A B.M Flow aparece acima da barra de tarefas. As preferências são salvas em `%APPDATA%\BMFlow\settings.ini`. A opção **Iniciar com o Windows** cria ou remove somente a entrada `B.M Flow` da inicialização do usuário atual. Para sair, clique com o botão direito no fundo da Dock e selecione **Fechar B.M Flow**.

Ao executar a nova versão pela primeira vez, as preferências e a inicialização automática da antiga DockBar são migradas sem perder os ajustes existentes.

A versão atual inclui:

- fundo acrílico translúcido;
- atalhos fixados lidos da barra de tarefas e programas abertos acrescentados automaticamente;
- ícones reais dos executáveis, ativação de janelas e indicadores de execução;
- bateria, volume e relógio em tempo real;
- clique no volume para ativar ou desativar o som;
- opção **Ocultar barra de tarefas original** no menu de botão direito, restaurada automaticamente ao fechar a B.M Flow.
- temas escuro e claro com transparência configurável;
- agrupamento de várias janelas no mesmo aplicativo;
- clique inteligente: restaura, minimiza ou alterna entre janelas;
- Jump Lists completas estilo Windows 11 com seções organizadas (Janelas, Tarefas, Recentes/Frequentes e Fixação);
- Exibição direta de arquivos recentes e pastas frequentes do sistema com ícones contextuais e submenu expansível;
- Ações rápidas de aplicativo (Executar como Administrador, abas/janelas anônimas em navegadores, pastas rápidas no Explorer);
- Detecção aprimorada de programas em segundo plano e bandeja (ex: Wallpaper Engine com indicador dedicado, alternância de plano de fundo e controles de pausa/retomada);
- Menus de contexto e submenus com design fluente consistente (acrílico adaptativo, cantos arredondados e suporte dinâmico a tema claro/escuro);
- animação de abertura em bounce;
- miniatura ao vivo com botões de fechar, minimizar/restaurar e clique do meio para fechar;
- agrupamento avançado para muitas janelas com paginação automática e cartões dinamicamente compactos;
- navegação completa por teclado nas prévias (Setas, Tab, Home, End, PgUp, PgDn, Enter/Espaço, Delete, M e Esc);
- suspensão e desregistro imediato de miniaturas DWM quando as prévias não estão visíveis ou em jogos de tela cheia, reduzindo o consumo de memória e GPU;
- ocultação automática após 0,5 segundo, com revelação pela borda inferior;
- suspensão automática durante jogos e aplicativos em tela cheia;
- modo expansível: com auto-ocultação, janelas maximizadas usam a tela inteira e a Dock aparece por cima;
- integração AppBar opcional no modo fixo para manter janelas maximizadas acima da Dock;
- opções **Ocultar automaticamente** e **Manter janelas acima da Dock** no menu de botão direito.
- janela completa de configurações com galeria visual de temas e ajustes em tempo real;
- opção **Iniciar com o Windows**, sem necessidade de administrador;
- preferências persistentes após fechar a Dock ou reiniciar o computador;
- reordenação dos aplicativos por arrastar, com a ordem salva automaticamente.

Widgets opcionais de mídia e clima continuam planejados para etapas posteriores.
