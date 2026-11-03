# Desenvolvimento

## Ferramentas

- Windows 10 ou 11 de 64 bits;
- PowerShell 5.1+;
- .NET Framework 4.x instalado no sistema;
- editor com suporte a C# (Visual Studio, VS Code, Rider ou equivalente).

Não é necessário instalar o .NET SDK ou restaurar pacotes NuGet para o build atual.

## Build

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

O script:

1. localiza `csc.exe` no .NET Framework de 64 bits;
2. encontra assemblies WPF no cache de assemblies do Windows;
3. incorpora `Assets/Images/windows11.png` como recurso;
4. compila `DockBar.cs` como aplicativo gráfico otimizado;
5. grava `BMFlow.exe` na raiz.

Feche uma instância em execução antes do build; o Windows não permite sobrescrever o executável aberto.

## Executar e depurar

Execute `BMFlow.exe` pela raiz para que `Mascots/` e `Cursors/` sejam encontrados. Exceções não tratadas geram `crash.log` ao lado do executável.

Para testar com configuração limpa, faça antes uma cópia de `%APPDATA%\BMFlow` e restaure-a ao terminar. Não publique esse diretório: ele pode conter caminhos de aplicativos do usuário.

## Compatibilidade

O código precisa continuar compatível com a linguagem aceita pelo `csc.exe` do .NET Framework usado no script. Evite adotar sintaxe moderna sem atualizar e documentar toda a cadeia de build.

Mudanças visuais devem ser verificadas com:

- escala de 100% e uma escala maior;
- tema claro e escuro;
- um e múltiplos monitores, quando possível;
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
