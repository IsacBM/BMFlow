# Desenvolvimento

## Ferramentas

- Windows 10 ou 11 de 64 bits;
- PowerShell 5.1+;
- .NET Framework 4.x instalado no sistema;
- editor com suporte a C# (Visual Studio, VS Code, Rider ou equivalente).

Não é necessário instalar o .NET SDK ou restaurar pacotes NuGet para o build portátil. O SDK ou Visual Studio são opcionais para trabalhar com `BMFlow.csproj`.

## Build

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

O script:

1. localiza `csc.exe` no .NET Framework de 64 bits;
2. encontra assemblies WPF no cache de assemblies do Windows;
3. incorpora `Assets/Images/windows11.png` como recurso;
4. compila recursivamente os arquivos C# em `src/` como aplicativo gráfico otimizado;
5. grava `BMFlow.exe` na raiz.

Feche uma instância em execução antes do build; o Windows não permite sobrescrever o executável aberto.

### Build pelo projeto SDK

Com o .NET SDK ou Visual Studio instalado:

```powershell
dotnet build .\BMFlow.csproj -c Release
```

Esse caminho gera a saída em `bin/Release/net48/`. Os dois builds usam os mesmos fontes e incorporam `Assets/Images/windows11.png` com o nome lógico `windows11.png`.

## Guardião Automático

Execute localmente antes de abrir um pull request:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify.ps1
```

O Guardião confere a estrutura modular, classes esperadas, projeto XML, arquivos comunitários, espaços finais, links locais, higiene do Git, build, metadados e hash SHA-256. A saída padrão fica em `artifacts/verification/`, que não é rastreada pelo Git.

O workflow `.github/workflows/ci.yml` executa o mesmo script em cada push e pull request e disponibiliza o executável validado como artefato temporário.

## Executar e depurar

Execute `BMFlow.exe` pela raiz para que `Mascots/` e `Cursors/` sejam encontrados. Exceções não tratadas geram `crash.log` ao lado do executável.

Para testar com configuração limpa, faça antes uma cópia de `%APPDATA%\BMFlow` e restaure-a ao terminar. Não publique esse diretório: ele pode conter caminhos de aplicativos do usuário.

## Compatibilidade

O código precisa continuar compatível com C# 7.3, definido no projeto e aceito pelo `csc.exe` do .NET Framework usado no script. Evite adotar sintaxe mais recente sem atualizar e documentar os dois caminhos de build.

Novas classes devem ficar no domínio correspondente dentro de `src/`. Integrações genéricas com Win32 pertencem a `WindowsInterop/`; lógica visual específica deve permanecer junto do recurso que a utiliza.

Mudanças visuais devem ser verificadas com:

- escala de 100% e uma escala maior;
- tema claro e escuro;
- um e múltiplos monitores, quando possível;
- as quatro posições da dock: inferior, superior, esquerda e direita;
- auto-ocultação, menus, prévias e pop-ups em cada borda;
- barra de tarefas em ocultação automática e sempre visível;
- janelas normais, maximizadas e em tela cheia.

## Diagnóstico

Ao investigar um bug, registre:

- edição, versão e número de compilação do Windows;
- escala e quantidade de monitores;
- configuração relacionada ao problema;
- passos exatos e frequência;
- conteúdo revisado de `crash.log`, se existir.

Não inclua nomes de usuário, caminhos pessoais, títulos privados de janelas ou listas de arquivos recentes.
