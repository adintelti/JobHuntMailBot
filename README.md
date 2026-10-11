# JobHunt MailBot

Aplicativo Windows Forms em C# e .NET 10 para cadastrar vagas em TXT e enviar candidaturas individualmente pelo Gmail, com configuração simples por senha de app, OAuth 2.0 opcional, currículo e histórico local.

Também inclui uma interface web ASP.NET Core Razor Pages, em paralelo ao Windows Forms, para uso local no Linux ou Windows.

## Interface web para Linux e Windows

### Estrutura da solução

| Projeto | Função |
|---|---|
| `src/JobHunt.Core` | Biblioteca `net10.0`: modelos, TXT, SMTP/MailKit, processamento de lotes e logs. |
| `src/JobHunt.MailBot` | Windows Forms `net10.0-windows`, OAuth desktop e credenciais DPAPI. |
| `src/JobHunt.Web` | ASP.NET Core Razor Pages `net10.0`, armazenamento web e serviço de lotes em segundo plano. |
| `tests/JobHunt.Core.Tests` | Testes compartilhados executáveis no Windows e Linux. |
| `tests/JobHunt.Web.Tests` | Testes web e de persistência, com SMTP simulado. |
| `tests/JobHunt.MailBot.Tests` | Testes do aplicativo Windows, incluindo DPAPI e OAuth. |

As interfaces usam os mesmos serviços de TXT e envio, sem referência do projeto web ao executável Windows. Os namespaces existentes foram preservados para manter compatibilidade com o aplicativo desktop.

### Executar a partir do código

No Visual Studio, instale a carga **ASP.NET e desenvolvimento Web** e selecione `JobHunt.Web` como projeto de inicialização. No terminal, com o SDK .NET 10:

```sh
dotnet run --project src/JobHunt.Web
```

Abra **http://127.0.0.1:5080**. O serviço aceita apenas conexões e nomes de host locais. Esta distribuição é para uma pessoa na própria máquina; não inclui login multiusuário nem exposição pela rede. Não configure proxy público para esta versão. Quem tiver acesso ao serviço local poderá consultar vagas e iniciar envios. Fechar a aba não encerra o serviço; Ctrl+C encerra a execução no terminal.

1. Em **Cadastro de vagas**, escolha o nome do TXT e acrescente uma vaga por vez, ou importe um TXT de até 2 MB. Importar nunca substitui um arquivo existente. Duplicatas exatas são rejeitadas ao acrescentar; emails iguais com assuntos distintos são permitidos.
2. Em **Gmail e mensagem**, informe seu email, senha de app, mensagem e currículo opcional de até 10 MB. A configuração SMTP é automática, como no Windows Forms. O OAuth desktop continua disponível na interface Windows; a versão web inicialmente usa senha de app.
3. Se desejar, altere a pasta de vagas para um caminho absoluto **na máquina do serviço**. A seleção de arquivos no navegador faz upload; **Baixar** gera uma cópia no computador do navegador. Isso também se aplica ao currículo.
4. Salve e use **Enviar teste para meu email**: envia uma mensagem real ao próprio remetente com a mensagem e o anexo salvos. O teste só ocorre quando você clica nesse botão.
5. Em **Envio em lote**, carregue o TXT, marque as vagas, revise a mensagem e confirme a revisão antes de enviar. Há apenas um lote ativo por instalação.
6. **Acompanhamento** atualiza automaticamente durante o envio, permite cancelar e baixar os logs JSONL. Falhas individuais não impedem os próximos envios. As novas tentativas seguem as mesmas regras do desktop.

Itens com sucesso ou resultado indeterminado ficam bloqueados na seleção web, inclusive após reiniciar. O serviço registra o item em transmissão antes de chamar o SMTP. Se for interrompido nesse ponto, o item passa a indeterminado na recuperação. Lotes interrompidos não são retomados automaticamente; itens ainda pendentes podem ser selecionados em outro lote. Confira a pasta Enviados no Gmail quando houver dúvida. Não apague o histórico para forçar reenvios.

### Dados e credenciais web

Por padrão, a pasta de dados é `~/.local/share/JobHunt.Web` no Linux e `%LOCALAPPDATA%\JobHunt.Web` no Windows. As configurações e credenciais do Windows Forms permanecem separadas; copie apenas seus TXT para importar na versão web.

| Local | Conteúdo |
|---|---|
| `web-state.json` | Configuração sem senha, estado dos lotes e recuperação de envios interrompidos. |
| `jobs/` | Pasta padrão para TXT; pode ser alterada na interface. |
| `attachments/` | Currículos enviados pelo navegador, em subpastas privadas. |
| `logs/` | Logs JSONL das tentativas. |
| `credentials/` | Senhas salvas por opção explícita, protegidas com Data Protection e vinculadas ao email. |
| `keys/` e `protection.pfx` | Chaves persistentes de Data Protection e certificado local que as protege. |

Sem **Lembrar senha nesta máquina**, a senha fica apenas na memória até o serviço encerrar; ao salvar desmarcado, a cópia persistida dessa conta é removida. **Esquecer senha local** remove também a credencial da sessão. Nenhuma dessas ações revoga a senha no Google. Trocar de conta exige a senha correspondente e remove a cópia da conta anterior.

No Linux, a pasta de dados e as subpastas de proteção têm permissões restritas ao usuário do serviço. As chaves de Data Protection são criptografadas com um certificado local; sua chave privada também depende das permissões do sistema de arquivos. Isso não protege contra um administrador ou alguém com acesso integral à pasta. Em backups, preserve a pasta de dados completa e mantenha-a privada. Perder o certificado ou as chaves impede recuperar as senhas salvas. Os arquivos DPAPI do Windows não são importados.

Para escolher outra pasta antes de iniciar:

```sh
JobHunt__DataDirectory=/home/seu-usuario/.local/share/JobHunt.Web ./JobHunt.Web
```

Se a porta estiver ocupada, use `JobHunt__Port=5081 ./JobHunt.Web` e abra http://127.0.0.1:5081. O endereço de escuta continua sendo loopback.

### Executar a versão web no Windows

Os comandos abaixo usam PowerShell. Execute a aplicação com seu usuário habitual do Windows para manter os dados no mesmo perfil.

#### Pelo código-fonte

Instale o **SDK .NET 10**. No Visual Studio 2026, use a carga **ASP.NET e desenvolvimento Web**, abra `JobHunt.MailBot.sln`, defina **JobHunt.Web** como projeto de inicialização e pressione F5.

Para executar pelo terminal, abra o PowerShell na pasta da solução:

```powershell
Set-Location C:\git\JobHunt
dotnet run --project .\src\JobHunt.Web
```

Quando aparecer `Now listening on: http://127.0.0.1:5080`, abra **http://127.0.0.1:5080** no navegador. Mantenha o terminal aberto durante o uso. Para encerrar, pressione **Ctrl+C** e aguarde o término do processo. Fechar apenas o navegador mantém o serviço e eventuais lotes em execução.

#### Pela publicação Windows x64

Para gerar uma pasta pronta para distribuir, execute na raiz da solução, com o SDK .NET 10 instalado:

```powershell
.\scripts\publish-web.ps1 -Runtime win-x64
```

O resultado fica em `artifacts\web-win-x64`. Também é possível publicar diretamente:

```powershell
dotnet publish .\src\JobHunt.Web -c Release -r win-x64 --self-contained true -o .\artifacts\web-win-x64
```

Essa publicação inclui o runtime .NET. No Windows x64 de destino, copie **a pasta inteira**, incluindo `wwwroot` e as bibliotecas. Abra o PowerShell na pasta publicada e execute:

```powershell
Set-Location C:\git\JobHunt\artifacts\web-win-x64
.\JobHunt.Web.exe
```

Se copiou a publicação para outro local, ajuste o caminho do `Set-Location`. Execute a partir dessa pasta para que os arquivos de estilo e scripts sejam encontrados. Abra **http://127.0.0.1:5080** no navegador da mesma máquina. O executável da interface web é `JobHunt.Web.exe`; `JobHunt.MailBot.exe` abre a interface Windows Forms.

#### Porta e pasta de dados no Windows

As configurações, senhas salvas, currículos e histórico web ficam por padrão em `%LOCALAPPDATA%\JobHunt.Web`, separados da pasta dos binários e dos dados do Windows Forms. Para abrir essa pasta no Explorador de Arquivos, cole `%LOCALAPPDATA%\JobHunt.Web` na barra de endereço.

Se a porta 5080 estiver ocupada, inclusive por uma instância no WSL, escolha outra porta antes de iniciar:

```powershell
$env:JobHunt__Port = '5081'
.\JobHunt.Web.exe
```

Nesse caso, abra **http://127.0.0.1:5081**. Para executar pelo código na mesma porta, use `dotnet run --project .\src\JobHunt.Web` na raiz da solução após definir a variável.

Para escolher uma pasta permanente diferente para os dados:

```powershell
$env:JobHunt__DataDirectory = Join-Path $env:LOCALAPPDATA 'JobHunt.Web-Pessoal'
.\JobHunt.Web.exe
```

Essas variáveis valem para os processos iniciados a partir desse terminal. Ao escolher uma pasta nova, a aplicação inicia com configurações e histórico próprios; os dados da pasta anterior não são migrados automaticamente. Para voltar aos valores padrão, encerre a aplicação e abra um novo PowerShell sem definir essas variáveis.

Na primeira execução, abra **Gmail e mensagem**, configure a senha de app, a mensagem e o currículo e salve. A pasta dos TXT pode ser alterada nessa tela, por exemplo para `C:\Vagas`. Para atualizar a publicação, encerre a aplicação, substitua os binários e preserve a pasta de dados completa, incluindo as chaves e o certificado de proteção.

### Publicar e executar no Linux

No PowerShell, inclusive a partir do Windows:

```powershell
./scripts/publish-web.ps1 -Runtime linux-x64
```

Ou diretamente com o SDK, no Windows ou Linux:

```sh
dotnet publish src/JobHunt.Web -c Release -r linux-x64 --self-contained true -o artifacts/web-linux-x64
```

Copie **a pasta inteira** publicada para o Linux. O runtime .NET vem incluído; permanecem necessárias as bibliotecas nativas da distribuição suportada pelo .NET 10. `linux-x64` é para Linux x86-64 com glibc, como Ubuntu; ARM64 exige publicação `linux-arm64`, e Alpine exige uma publicação própria com musl.

```sh
cd /caminho/JobHunt.Web
chmod +x JobHunt.Web
./JobHunt.Web
```

Abra http://127.0.0.1:5080 no navegador da mesma máquina. O script PowerShell também copia `run-web.sh`, que posiciona o diretório de trabalho corretamente; execute-o com `sh ./run-web.sh`. Atualize os binários com o serviço parado, preservando a pasta de dados.

### Iniciar como serviço de usuário no Linux

O arquivo `deploy/JobHunt.Web.service` é um modelo para systemd de usuário, sem instalar ou habilitar serviços automaticamente:

1. Copie a publicação para `~/.local/opt/JobHunt.Web` e conceda execução ao binário.
2. Copie o arquivo de serviço para `~/.config/systemd/user/JobHunt.Web.service`.
3. Execute:

```sh
systemctl --user daemon-reload
systemctl --user enable --now JobHunt.Web.service
systemctl --user status JobHunt.Web.service
```

O serviço inicia com a sessão do usuário. Para encerrar: `systemctl --user stop JobHunt.Web.service`. Para consultar diagnósticos: `journalctl --user -u JobHunt.Web.service`. A porta continua restrita à máquina local.

### Testes no Linux

Execute apenas os projetos multiplataforma, com o SDK .NET 10 instalado:

```sh
dotnet test tests/JobHunt.Core.Tests -c Release
dotnet test tests/JobHunt.Web.Tests -c Release
```

A solução completa também contém Windows Forms e seus testes de DPAPI, portanto deve ser compilada e testada integralmente no Windows. Todos os testes automatizados usam SMTP simulado e não enviam emails reais.

## Abrir e executar

1. Abra `JobHunt.MailBot.sln` no Visual Studio 2026, com **Desenvolvimento para desktop com .NET** e .NET 10 instalados.
2. Defina `JobHunt.MailBot` como projeto de inicialização.
3. Execute com F5. Os pacotes NuGet serão restaurados na primeira compilação.

Também é possível usar o terminal na pasta da solução:

```powershell
dotnet restore
dotnet run --project src/JobHunt.MailBot
```

## Cadastrar vagas

1. Na aba **Cadastrar vagas**, use **Novo TXT** para escolher um novo nome ou **Escolher TXT** para continuar um arquivo.
2. Cole uma vaga:

```text
Email : recrutamento@example.com
Assunto a ser colocado no email : apinfo - 85202 - An. Negocios
```

3. Confira a prévia e clique em **Adicionar ao arquivo**.
4. Repita para cada vaga. O arquivo é criado na primeira inclusão.

A saída usa exatamente os dois prefixos acima, UTF-8 sem BOM, CRLF e uma quebra ao final de cada linha, sem linhas vazias entre as inclusões. Negrito Markdown envolvendo os valores é aceito na entrada e removido. Acentos e pontuação do assunto são preservados. Cada email pode aparecer em várias vagas com assuntos distintos. Repetir o mesmo email e assunto exige confirmação.

O arquivo existente não é substituído. Antes de acrescentar, o programa verifica seu formato e sua codificação. Se a gravação falhar, a entrada fica disponível para uma nova tentativa.

## Configurar o Gmail com senha de app (simples)

A opção padrão para novas instalações é **Gmail com senha de app (simples)**. Basta informar seu email e uma senha de app do Google; projeto no Google Cloud e JSON OAuth não são necessários nessa modalidade.

1. Na Conta do Google, ative a **Verificação em duas etapas**.
2. Acesse [Senhas de app](https://myaccount.google.com/apppasswords) e gere uma senha para **JobHunt MailBot**.
3. No programa, abra **Gmail e mensagem** e selecione **Gmail com senha de app (simples)**.
4. Informe seu endereço completo e cole a senha de app de 16 caracteres. Os espaços usados para exibir a senha em grupos são removidos automaticamente; os caracteres ficam ocultos na tela.
5. Se desejar lembrar a senha após fechar o programa, marque **Salvar senha neste computador**. Essa opção vem desmarcada; nesse caso, a senha permanece somente na sessão.
6. Preencha o nome do remetente, a mensagem e, opcionalmente, selecione o currículo. Clique em **Salvar configurações**.
7. Use **Enviar email de teste**. Se o destinatário de teste ficar em branco, a mensagem será enviada para seu próprio Gmail.

Servidor, porta e segurança são automáticos: `smtp.gmail.com`, porta `587`, **STARTTLS obrigatório**. O programa usa MailKit e valida os certificados TLS normalmente.

Use a senha de app gerada pelo Google; a senha habitual da conta não substitui essa credencial. Senhas de app exigem verificação em duas etapas e podem não estar disponíveis em contas organizacionais, com Proteção Avançada ou com determinadas configurações de segurança. Alterar a senha principal do Google revoga as senhas de app existentes. Se esse recurso não estiver disponível, use a alternativa OAuth.

A senha salva fica criptografada por DPAPI e associada ao email informado. Alterar o email limpa a senha da tela, impedindo o uso acidental da credencial de outra conta. **Esquecer senha salva** remove a cópia local da conta informada e limpa a entrada; isso não revoga a credencial no Google. Para revogá-la, use a página Senhas de app da Conta do Google. Desmarcar **Salvar senha neste computador** e salvar ou fechar o programa também remove a cópia local dessa conta.

O Google recomenda OAuth como forma preferencial de conexão. A modalidade por senha de app prioriza a configuração local simples quando a conta permite usá-la. [Orientações oficiais sobre senhas de app](https://support.google.com/accounts/answer/185833?hl=pt-BR), [configuração SMTP](https://knowledge.workspace.google.com/admin/gmail/send-email-from-a-printer-scanner-or-app?hl=en).

## Configurar o Gmail com OAuth 2.0 (alternativa)

Selecione **Google OAuth 2.0 (alternativa)** em **Forma de acesso**. Os campos do JSON e os botões de conexão ficam visíveis somente nesse modo. A autenticação abre o navegador do sistema, com PKCE e retorno local.

Instalações configuradas antes da opção por senha de app continuam usando OAuth automaticamente. Os endereços dos dois modos são preservados separadamente ao alternar a forma de acesso.

1. Acesse o [Google Cloud Console](https://console.cloud.google.com/) e crie ou selecione um projeto próprio.
2. Na biblioteca de APIs, habilite a **Gmail API**, para disponibilizar os escopos do Gmail na configuração do projeto.
3. Em **Google Auth platform**, configure os dados do aplicativo em **Branding**.
4. Em **Audience**, para uma conta pessoal, selecione público externo e mantenha o aplicativo em teste durante a configuração. Adicione seu Gmail como usuário de teste.
5. Em **Data Access**, configure os escopos `https://mail.google.com/`, `openid` e `email`. O escopo completo do Gmail é exigido pelo SMTP OAuth; a permissão `gmail.send` da API não autentica SMTP.
6. Em **Clients**, crie um cliente OAuth do tipo **Desktop app / Aplicativo para computador** e baixe seu JSON. O arquivo precisa conter a seção `installed`; clientes Web e contas de serviço não são aceitos.
7. Guarde o JSON em uma pasta local estável fora do repositório.
8. Na aba **Gmail e mensagem**, escolha a alternativa OAuth, selecione esse JSON e clique em **Conectar ao Google**.
9. Autorize a conta no navegador. O endereço do remetente será preenchido a partir da conta autorizada.
10. Configure nome, mensagem e currículo opcional. Salve as configurações.
11. Informe seu próprio email como destinatário e use **Enviar email de teste** antes do primeiro lote.

O cancelamento da autorização fica disponível em **Cancelar operação**, na aba **Envio em lote**. A espera pela autorização termina em três minutos. Para trocar de conta usando o mesmo cliente OAuth, desconecte primeiro e depois conecte novamente.

Projetos externos em modo de teste que solicitam acesso ao Gmail podem ter refresh tokens com validade de sete dias. Nesse caso, desconecte e autorize novamente. A disponibilização pública de um aplicativo com escopos restritos pode exigir verificação do Google; mantenha a configuração de uso pessoal adequada à sua conta. Administradores de Google Workspace também podem restringir a autorização.

Fontes oficiais: [tela de consentimento](https://developers.google.com/workspace/guides/configure-oauth-consent), [OAuth para desktop](https://developers.google.com/identity/protocols/oauth2/native-app), [expiração de tokens](https://developers.google.com/identity/protocols/oauth2#expiration), [SMTP do Gmail](https://developers.google.com/workspace/gmail/imap/imap-smtp).

## Enviar um lote

1. Na aba **Envio em lote**, selecione e carregue um TXT.
2. Revise os destinatários, assuntos e eventuais problemas de formato. Linhas inválidas não são enviadas e não deslocam a associação dos blocos seguintes.
3. Marque as vagas desejadas. Mensagens com sucesso anterior ou resultado indeterminado ficam desmarcadas ao recarregar o arquivo.
4. Confira a mensagem na aba **Gmail e mensagem**, usando **Prévia da mensagem**.
5. Clique em **Enviar selecionadas**. Cada vaga gera uma mensagem separada, sem compartilhar destinatários.
6. Consulte os resultados na tabela e na aba **Acompanhamento**.

Falhas individuais são registradas e o processamento segue para a próxima vaga. Rejeições temporárias e falhas de conexão anteriores à transmissão podem receber até três novas tentativas, conforme a configuração. A espera cresce entre tentativas; respostas SMTP 421 recebem uma espera de 60 segundos. A execução mantém apenas um envio em andamento e permite cancelamento.

Uma interrupção durante a transmissão pode deixar o resultado **Indeterminado**: o servidor pode ter aceitado a mensagem antes da queda. Esses itens não são repetidos automaticamente. Confira a pasta Enviados antes de selecionar um reenvio manual. **Selecionar falhas** marca somente falhas da execução atual.

O cancelamento mantém os itens restantes como pendentes. Se uma mensagem estiver em transmissão, o programa registra seu resultado antes de encerrar. A interface continua responsiva. Fechar a janela durante uma operação solicita cancelamento e aguarda o registro dos resultados.

O Gmail pode recusar todos os itens seguintes quando a conta atinge um limite; continuar o processamento não contorna as cotas. [Limites oficiais](https://support.google.com/mail/answer/22839?hl=pt-BR).

## Configurações e logs

Os arquivos locais ficam em `%LOCALAPPDATA%\JobHunt.MailBot`:

| Local | Conteúdo |
|---|---|
| `settings.json` | Caminhos, modo de autenticação, endereços, mensagem e opções de envio; sem senhas. |
| `app-passwords\*.bin` | Senhas de app salvas por opção do usuário, criptografadas por DPAPI. |
| `tokens\...\*.bin` | Tokens criptografados por DPAPI para o usuário atual do Windows. |
| `logs\lote-*.jsonl` | Um objeto JSON por tentativa, gravado imediatamente. |

O TXT de vagas fica no caminho escolhido por você. Quando OAuth é utilizado, seu JSON permanece no local selecionado. Senhas e tokens não são incluídos nas configurações JSON, no TXT nem nos logs. A proteção DPAPI vincula as credenciais ao usuário atual do Windows.

Cada registro do log inclui lote, arquivo de origem, posição da vaga, email, assunto, data e hora com fuso, tentativa, resultado, detalhes e indicação de tentativa final. Os horários usam o relógio local do Windows. **Sucesso** significa aceitação pelo servidor SMTP, e não confirmação de leitura ou entrega final. A falha posterior de entrega pode aparecer como mensagem de devolução no Gmail.

Use **Abrir pasta de logs** ou **Abrir último log** na aba Acompanhamento. A proteção contra repetição depende da preservação desses logs. Ela identifica o par email/assunto e vale para todas as contas usadas nesta instalação.

Se o log não puder ser gravado, a execução é interrompida por erro de armazenamento, para não seguir enviando sem histórico. Esse problema é diferente de uma falha individual de email.

## Testes

```powershell
dotnet test JobHunt.MailBot.sln -c Release
```

Os testes não enviam emails reais. Cobrem o formato exato do TXT, acentos, duplicatas, recuperação de registros inválidos, continuidade após falhas, novas tentativas, cancelamento, persistência dos logs, histórico, criptografia dos tokens e senhas, remoção de credenciais, migração das configurações OAuth e autenticação SMTP por senha de app em uma sessão simulada.

Utilitários locais de verificação, sem envio de email:

```powershell
dotnet src/JobHunt.MailBot/bin/Release/net10.0-windows/JobHunt.MailBot.dll --verify-file "C:\Vagas\vagas.txt"
dotnet src/JobHunt.MailBot/bin/Release/net10.0-windows/JobHunt.MailBot.dll --preview "C:\Vagas\previews"
```

O primeiro informa a quantidade de vagas válidas e problemas; retorna código 1 se houver problemas. O segundo gera imagens das quatro abas e uma imagem adicional da configuração OAuth, com dados demonstrativos e sem usar as configurações pessoais.

## Publicar para Windows x64

```powershell
dotnet publish src/JobHunt.MailBot -c Release -r win-x64 --self-contained false -o artifacts/publish
```

Copie a pasta inteira para o computador de destino e execute `JobHunt.MailBot.exe`. Essa publicação exige o .NET Desktop Runtime 10. Para incluir o runtime na distribuição, use `--self-contained true`. Configure a conta Google no usuário Windows de destino; senhas e tokens DPAPI não devem ser transferidos entre usuários.
