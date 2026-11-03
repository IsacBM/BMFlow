# Solução de problemas

## O build informa que `BMFlow.exe` está em uso

Feche a B.M Flow pelo menu **Fechar B.M Flow** e execute `build.ps1` novamente. Se o processo não estiver visível, confirme no Gerenciador de Tarefas antes de encerrá-lo.

## O compilador do .NET Framework não foi encontrado

Confirme a existência de `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`. Instale ou repare o .NET Framework apropriado para o Windows e tente novamente.

## A dock ou os mascotes aparecem sem recursos

Mantenha `BMFlow.exe`, `Mascots/` e `Cursors/` na mesma pasta. Não execute somente uma cópia isolada do `.exe` se quiser usar esses recursos.

## A barra de tarefas ficou oculta

Encerre a B.M Flow normalmente para restaurá-la. Se o aplicativo já não estiver em execução, reinicie o Windows Explorer pelo Gerenciador de Tarefas ou entre novamente na sessão do Windows.

## O cursor não voltou ao tema anterior

Abra a B.M Flow, desative o tema de cursor nas configurações e encerre normalmente. Se necessário, escolha manualmente um esquema em **Configurações do Windows > Bluetooth e dispositivos > Mouse > Configurações adicionais do mouse > Ponteiros**.

## As configurações parecem corrompidas

Feche a B.M Flow, faça backup de `%APPDATA%\BMFlow` e renomeie `settings.ini`. Na próxima execução, o aplicativo criará valores padrão. Faça o mesmo com `workspaces.ini` somente se o problema estiver nos espaços de trabalho.

## Foi criado um `crash.log`

Revise o arquivo antes de compartilhar, remova caminhos e dados pessoais e abra uma issue com os passos para reproduzir. O log corresponde à falha mais recente e pode ser substituído por outra.
