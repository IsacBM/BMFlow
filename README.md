# B.M Flow

> Sua área de trabalho em movimento.

B.M Flow é uma dock nativa para Windows 11, feita em C# e WPF. Ela reúne aplicativos fixados e abertos, controles do sistema, espaços de trabalho, widgets e mascotes animados em uma interface fluida acima da barra de tarefas.

O projeto está aberto para aprender, evoluir e construir junto com a comunidade. 💙

![Windows 11](https://img.shields.io/badge/Windows%20%7C%2011-0078D4?logo=windows&logoColor=white)
![C#](https://img.shields.io/badge/C%23-WPF-512BD4?logo=dotnet&logoColor=white)
![Licença MIT](https://img.shields.io/badge/c%C3%B3digo-MIT-green)
[![Guardião Automático](https://github.com/IsacBM/BMFlow/actions/workflows/ci.yml/badge.svg)](https://github.com/IsacBM/BMFlow/actions/workflows/ci.yml)

## Visão geral

- dock acrílica com temas, transparência e tamanho configuráveis, posicionável nas bordas inferior, superior, esquerda ou direita;
- aplicativos fixados e janelas abertas, com agrupamento, reordenação e indicadores;
- miniaturas ao vivo, paginação e navegação completa por teclado;
- Jump Lists, itens recentes, pastas frequentes e ações rápidas;
- relógio, bateria, rede, volume, lixeira e bandeja do sistema;
- ocultação automática, suporte a tela cheia e integração opcional como AppBar;
- espaços de trabalho para abrir e restaurar conjuntos de aplicativos;
- widget de relógio para a área de trabalho;
- mascotes animados com diferentes aparências e comportamentos;
- temas opcionais de cursor, com backup e restauração do esquema anterior;
- detecção de assistentes instalados e integração com aplicativos em segundo plano.

Widgets de mídia e clima estão entre as ideias para versões futuras.

## Requisitos

- Windows 11 de 64 bits;
- PowerShell 5.1 ou mais recente para compilar;
- .NET Framework 4.x com o compilador C# em `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319`.

O .NET SDK ou Visual Studio é opcional e permite abrir e compilar `BMFlow.csproj` diretamente.

O projeto usa somente componentes presentes no Windows e não depende de pacotes NuGet.

## Como usar

1. Baixe ou clone o repositório.
2. Execute `BMFlow.exe`.
3. Clique com o botão direito no fundo da dock para abrir as configurações.
4. Para encerrar, escolha **Fechar B.M Flow** no mesmo menu.

As preferências ficam em `%APPDATA%\BMFlow\settings.ini`. A opção **Iniciar com o Windows** altera somente a entrada `B.M Flow` na inicialização do usuário atual. Preferências e inicialização de versões antigas chamadas DockBar são migradas automaticamente.

A borda da dock pode ser escolhida em **Configurações → Tamanho e posição → Posição na tela** ou no submenu **Tamanho e posição** do menu de contexto. Nas laterais, os itens são organizados verticalmente. O mascote funciona somente na borda inferior; ao mover a dock para a borda superior ou para uma lateral, ele é desativado e permanece assim ao retornar.

> [!IMPORTANT]
> Guarde o executável junto das pastas `Mascots` e `Cursors`. Esses recursos são carregados a partir da pasta do programa.

## Compilar do código-fonte

Abra o PowerShell na raiz do projeto e execute:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

O script localiza as bibliotecas WPF instaladas no Windows, compila todos os arquivos em `src/` e gera `BMFlow.exe` na raiz do repositório.

Com o .NET SDK ou Visual Studio instalado, também é possível usar o projeto:

```powershell
dotnet build .\BMFlow.csproj -c Release
```

Se aparecer a mensagem de que `BMFlow.exe` está sendo usado por outro processo, feche a B.M Flow antes de compilar novamente.

### Verificar o projeto

Antes de enviar uma contribuição, execute o mesmo Guardião usado pelo GitHub:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify.ps1
```

Ele valida a estrutura, documentação, arquivos obrigatórios, metadados e compilação, gerando `BMFlow.exe` e seu SHA-256 em `artifacts/verification/`.

## Dados e alterações no sistema

A B.M Flow funciona localmente e salva suas configurações no perfil do usuário. Alguns recursos opcionais interagem com o Windows:

- **Iniciar com o Windows** altera `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`;
- **Temas de cursor** alteram o esquema de cursores do usuário atual e mantêm um backup para restauração;
- **Ocultar barra de tarefas original** muda sua visibilidade enquanto a B.M Flow está em execução;
- um erro inesperado pode gerar `crash.log` ao lado do executável.

Nenhum desses recursos exige privilégios de administrador. Leia também [Solução de problemas](docs/TROUBLESHOOTING.md).

## Estrutura do projeto

```text
.
├── src/                   # código C# separado por domínio
│   ├── Dock/              # janela principal e ciclo de vida da Dock
│   ├── Settings/          # interface de configurações
│   ├── Mascots/           # comportamento e animação dos mascotes
│   ├── Workspaces/        # espaços de trabalho
│   ├── Preview/           # miniaturas de janelas
│   ├── Cursors/           # aplicação de temas de cursor
│   └── WindowsInterop/    # comandos e serviços nativos do Windows
├── BMFlow.csproj          # projeto para Visual Studio e .NET SDK
├── build.ps1              # compilação com o .NET Framework do sistema
├── Assets/Images/         # imagens incorporadas ao executável
├── Mascots/               # quadros de animação dos mascotes
├── Cursors/               # temas e arquivos de cursor
├── docs/                  # arquitetura, desenvolvimento e assets
└── .github/               # guias comunitários e templates do GitHub
```

O projeto continua sem dependências externas e preserva o build sem SDK. Consulte o mapa completo em [Arquitetura](docs/ARCHITECTURE.md).

## Contribuir

Contribuições de todos os níveis são bem-vindas — correções, documentação, testes, traduções, acessibilidade, desempenho e novas ideias.

Antes de começar:

1. leia o [Guia de contribuição](.github/CONTRIBUTING.md);
2. confira as issues existentes;
3. para mudanças maiores, abra uma discussão ou issue antes da implementação;
4. siga nosso [Código de Conduta](.github/CODE_OF_CONDUCT.md).

Encontrou uma falha de segurança? Não abra uma issue pública; consulte nossa [Política de Segurança](.github/SECURITY.md).

## Licença e recursos visuais

O código-fonte e a documentação produzidos para a B.M Flow estão disponíveis sob a [Licença MIT](LICENSE).

Imagens, sprites e cursores podem ter autoria ou condições próprias e não devem ser considerados automaticamente cobertos pela licença do código. Consulte [Assets e créditos](docs/ASSETS.md) e [Avisos de terceiros](THIRD_PARTY_NOTICES.md) antes de redistribuí-los.

---

Feito com carinho para a comunidade.
