# JobHunt MailBot

Aplicativo Windows Forms em C# e .NET 10 para cadastrar vagas em TXT e enviar candidaturas individualmente pelo Gmail, com OAuth 2.0, currículo opcional e histórico local.

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

## Configurar o Gmail no Google Cloud

Esta versão usa SMTP com MailKit e OAuth 2.0; a autenticação abre o navegador do sistema, com PKCE e retorno local. Não é necessário informar a senha pessoal do Gmail.

1. Acesse o [Google Cloud Console](https://console.cloud.google.com/) e crie ou selecione um projeto próprio.
2. Na biblioteca de APIs, habilite a **Gmail API**, para disponibilizar os escopos do Gmail na configuração do projeto.
3. Em **Google Auth platform**, configure os dados do aplicativo em **Branding**.
4. Em **Audience**, para uma conta pessoal, selecione público externo e mantenha o aplicativo em teste durante a configuração. Adicione seu Gmail como usuário de teste.
5. Em **Data Access**, configure os escopos `https://mail.google.com/`, `openid` e `email`. O escopo completo do Gmail é exigido pelo SMTP OAuth; a permissão `gmail.send` da API não autentica SMTP.
6. Em **Clients**, crie um cliente OAuth do tipo **Desktop app / Aplicativo para computador** e baixe seu JSON. O arquivo precisa conter a seção `installed`; clientes Web e contas de serviço não são aceitos.
7. Guarde o JSON em uma pasta local estável fora do repositório.
8. Na aba **Gmail e mensagem**, selecione esse JSON e clique em **Conectar ao Google**.
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
| `settings.json` | Caminhos, remetente, mensagem e opções de envio. |
| `tokens\...\*.bin` | Tokens criptografados por DPAPI para o usuário atual do Windows. |
| `logs\lote-*.jsonl` | Um objeto JSON por tentativa, gravado imediatamente. |

O TXT de vagas fica no caminho escolhido por você. O JSON OAuth permanece no local selecionado. Tokens não são incluídos no TXT nem nos logs.

Cada registro do log inclui lote, arquivo de origem, posição da vaga, email, assunto, data e hora com fuso, tentativa, resultado, detalhes e indicação de tentativa final. Os horários usam o relógio local do Windows. **Sucesso** significa aceitação pelo servidor SMTP, e não confirmação de leitura ou entrega final. A falha posterior de entrega pode aparecer como mensagem de devolução no Gmail.

Use **Abrir pasta de logs** ou **Abrir último log** na aba Acompanhamento. A proteção contra repetição depende da preservação desses logs. Ela identifica o par email/assunto e vale para todas as contas usadas nesta instalação.

Se o log não puder ser gravado, a execução é interrompida por erro de armazenamento, para não seguir enviando sem histórico. Esse problema é diferente de uma falha individual de email.

## Testes

```powershell
dotnet test JobHunt.MailBot.sln -c Release
```

Os testes não enviam emails reais. Cobrem o formato exato do TXT, acentos, duplicatas, recuperação de registros inválidos, continuidade após falhas, novas tentativas, cancelamento, persistência dos logs, histórico e criptografia dos tokens.

Utilitários locais de verificação, sem envio de email:

```powershell
dotnet src/JobHunt.MailBot/bin/Release/net10.0-windows/JobHunt.MailBot.dll --verify-file "C:\Vagas\vagas.txt"
dotnet src/JobHunt.MailBot/bin/Release/net10.0-windows/JobHunt.MailBot.dll --preview "C:\Vagas\previews"
```

O primeiro informa a quantidade de vagas válidas e problemas; retorna código 1 se houver problemas. O segundo gera imagens das quatro abas com dados demonstrativos, sem usar as configurações pessoais.

## Publicar para Windows x64

```powershell
dotnet publish src/JobHunt.MailBot -c Release -r win-x64 --self-contained false -o artifacts/publish
```

Copie a pasta inteira para o computador de destino e execute `JobHunt.MailBot.exe`. Essa publicação exige o .NET Desktop Runtime 10. Para incluir o runtime na distribuição, use `--self-contained true`. Configure a conta Google no usuário Windows de destino; os tokens DPAPI não devem ser transferidos entre usuários.
