# Como contribuir

Obrigado por considerar uma contribuição para a B.M Flow. Queremos manter um ambiente acolhedor para quem está fazendo o primeiro pull request e também para pessoas experientes.

## Antes de começar

- Procure uma issue existente para evitar trabalho duplicado.
- Bugs pequenos e melhorias de documentação podem ir direto para um pull request.
- Para recursos grandes, mudanças visuais extensas ou alterações nas integrações nativas do Windows, abra uma issue primeiro para alinharmos comportamento e escopo.
- Não inclua recursos visuais, fontes, cursores ou código de terceiros sem informar claramente a origem e a licença.

## Preparar o ambiente

1. Faça um fork e clone o repositório.
2. Crie uma branch a partir de `main`:

   ```powershell
   git switch -c tipo/descricao-curta
   ```

3. Compile:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\build.ps1
   ```

4. Execute `BMFlow.exe` e valide a mudança no Windows 10 ou 11.

O build usa o compilador do .NET Framework instalado no Windows; não há restauração de pacotes.

## Convenções

- Preserve compatibilidade com o compilador C# do .NET Framework usado por `build.ps1`.
- Prefira mudanças pequenas e focadas.
- Use nomes claros no código e mantenha textos da interface em português enquanto não houver uma camada de localização.
- Trate chamadas Win32, acesso ao Registro e manipulação de janelas como áreas de risco: valide falhas e restaure o estado do sistema ao encerrar.
- Não registre caminhos pessoais, títulos de janelas, conteúdo recente ou outros dados do usuário.
- Atualize README, changelog ou documentos em `docs/` quando o comportamento público mudar.

## Testes manuais mínimos

Como o projeto ainda não possui testes automatizados, descreva no pull request o que foi verificado. Quando aplicável, teste:

- inicialização e encerramento sem `crash.log`;
- clique, clique do meio e menu de contexto dos aplicativos;
- aplicativos com uma e várias janelas;
- ocultação automática e aplicativo em tela cheia;
- barra de tarefas visível e oculta;
- temas claro e escuro, transparência e escalas de exibição diferentes;
- persistência após reiniciar o aplicativo;
- restauração do cursor e da barra de tarefas ao sair.

Feche a B.M Flow antes de recompilar, pois o Windows bloqueia a substituição de um executável em uso.

## Commits e pull requests

Mensagens no estilo Conventional Commits são bem-vindas:

```text
feat: adiciona nova opção
fix: corrige restauração da barra de tarefas
docs: esclarece processo de build
refactor: separa serviço de cursores
```

No pull request:

- explique o problema e a solução;
- relacione a issue, se existir;
- inclua capturas ou gravações para mudanças visuais;
- liste os cenários e versões do Windows testados;
- declare novos recursos de terceiros e suas licenças.

## Relatar problemas

Use os templates de issue e remova dados pessoais das capturas e logs. Para vulnerabilidades, siga [SECURITY.md](SECURITY.md).
