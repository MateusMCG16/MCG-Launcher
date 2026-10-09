# MCG Launcher

Launcher de jogos para Windows em **C# + WinUI 3**, com biblioteca local e interface escura.

## MVP

- Biblioteca em cards, busca sem distinção de acentos, filtros por plataforma e ordenação.
- Favoritos e histórico das últimas aberturas feitas pelo launcher.
- Cadastro e edição de jogos por executável `.exe` ou atalho `.lnk`, com argumentos, pasta de trabalho e capa local opcionais.
- Detecção de jogos instalados na Steam (incluindo bibliotecas em outros discos) e Epic Games.
- Imagens locais: capas e logos do cache Steam, imagens reconhecidas na instalação e ícones de executáveis/atalhos.
- Configurações abaixo de **Sobre o launcher**, com a opção **Desativar imagens** salva entre aberturas.
- Detecção automática na abertura quando a biblioteca está vazia; depois, atualização pelo botão **Detectar jogos**.
- Abertura pela plataforma original quando o jogo veio da Steam ou Epic.
- Remoção de cadastros com confirmação, sem desinstalar jogos.
- Persistência em `%LOCALAPPDATA%\MCGLauncher\library.json`, com escrita atômica e backup da versão anterior.

Não requer conta no launcher ou conexão para consultar a biblioteca. Os jogos e os clientes de suas plataformas podem exigir autenticação ou internet. O comando de abertura enviado não garante que o jogo iniciou: o cliente original pode solicitar login ou atualização.

## Ferramentas

- Windows 10 2004 ou posterior / Windows 11, **x64**.
- .NET SDK **10.0.300 ou posterior da série 10** (o `global.json` permite atualizar para outra feature band instalada).
- Visual Studio Build Tools 2026 com **WinUI application development build tools** e Windows SDK 10.0.26100.
- Acesso ao NuGet na primeira compilação para restaurar as dependências.

O projeto fixa o Windows App SDK 1.8.260921001 e o Windows SDK BuildTools 10.0.26100.9169. A publicação inclui os runtimes .NET e Windows App SDK: a pasta resultante pode ser aberta pelo executável, sem MSIX ou Modo de Desenvolvedor. A pasta é maior porque contém os runtimes.

## Compilar

Na raiz do repositório, em PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
```

O script localiza o MSBuild instalado, restaura as dependências e publica em:

```text
artifacts\launcher\MCG.Launcher.exe
```

O script **não abre** o aplicativo. Para testar, abra o executável pelo Explorador de Arquivos. Mantenha o conteúdo inteiro da pasta `artifacts\launcher` junto ao `.exe`.

Feche o launcher antes de publicar na mesma pasta. O script verifica isso antes de copiar os arquivos, evitando uma atualização parcial. Para gerar uma versão em outra pasta, use `scripts\build.ps1 -OutputDirectory artifacts\launcher-settings`.

## Usar

1. Na primeira abertura, aguarde a leitura local das instalações Steam e Epic.
2. Use **Adicionar jogo** para cadastrar jogos de outras plataformas ou instalações independentes.
3. Escolha um executável ou atalho e, se desejar, uma imagem local para a capa.
4. Clique em **Jogar**. Marque a estrela para adicionar aos favoritos.
5. Use **Imagens locais** na barra lateral para atualizar as imagens de jogos já cadastrados.
6. Use o menu do card para editar o cadastro, abrir a pasta ou remover da biblioteca.
7. Em **Configurações**, ative **Desativar imagens** e clique em **Salvar** para exibir apenas as iniciais nos cards. Desative a opção para voltar a carregar capas, logos e ícones. A preferência fica em `%LOCALAPPDATA%\MCGLauncher\settings.json`.

Somente instalações completas com arquivos presentes são importadas. A Steam é localizada pelo Registro do Windows e pela pasta padrão; a Epic é localizada pelos manifestos em `%PROGRAMDATA%\Epic\EpicGamesLauncher\Data\Manifests`. Instalações especiais de outras plataformas podem ser cadastradas manualmente. Não há varredura completa dos discos.

Uma nova detecção não sobrescreve nomes, capas personalizadas, favoritos ou histórico dos jogos já cadastrados. Cadastros removidos de jogos Steam/Epic podem reaparecer na próxima detecção. Capas manuais são referenciadas pelo caminho, portanto precisam continuar nesse local.

## Imagens da instalação

O launcher aproveita somente arquivos presentes no PC, sem downloads ou APIs externas:

- **Steam:** lê o cache `appcache\librarycache`, tanto no formato antigo quanto nos subdiretórios por AppID. Prioriza capas e banners; depois logos e ícones. Imagens pequenas de ícones aparecem centralizadas, sem serem esticadas como capas.
- **Instalação:** procura nomes de imagens como `cover`, `header`, `capsule`, `boxart`, `banner` e `logo` na raiz e nas pastas `Artwork`, `Images` e `Media`. Não varre arquivos de texturas nem abre arquivos de recursos dos jogos.
- **Executável ou atalho:** utiliza a miniatura/ícone fornecido pelo Windows, sem iniciar o jogo. Para jogos Epic, o executável vem do manifesto da instalação; para jogos manuais, do cadastro. Na Steam, a busca por executável se limita à raiz da instalação e ao nome do jogo ou da pasta.

O botão **Imagens locais** preenche imagens ausentes dos jogos existentes e pode substituir um logo/ícone automático por uma capa local que ficou disponível depois. Não adiciona jogos novos e preserva capas personalizadas. Jogos antigos sem imagem recebem essa atualização na próxima abertura do launcher. Se não houver imagem utilizável, o card mantém as iniciais.

Nem toda instalação inclui uma capa pronta: muitos jogos guardam suas imagens em arquivos internos. Na verificação deste PC, Oblivity e Counter-Strike 2 tinham capas no cache Steam, outros jogos tinham logos ou ícones, e as instalações Epic verificadas não tinham uma capa externa identificável. Nesse caso, o ícone do executável é a alternativa local.

## Dados e recuperação

`library.backup.json` guarda a versão anterior à última gravação. Se `library.json` estiver ilegível, o launcher apresenta o erro e bloqueia mudanças para preservar os dados. Com o aplicativo fechado, confira o arquivo e o backup antes de restaurar uma cópia. `library.pending.json` pode permanecer após uma gravação interrompida; a biblioteca principal continua preservada.

## Organização

- `src/MCG.Launcher`: janela WinUI, cards, diálogos e integração com o Registro.
- `src/MCG.Launcher.Core`: biblioteca, persistência, parsing dos manifestos e preparação da abertura de jogos.
- `tests/MCG.Launcher.Core.Tests`: verificações de parsing, deduplicação, busca, persistência e validação de abertura, sem iniciar jogos reais.
- `scripts/build.ps1`: publicação pelo MSBuild, sem a IDE completa.
- `scripts/check-launch.ps1`: verifica se o executável publicado cria uma janela que responde e solicita seu fechamento normal.
- `scripts/check-settings-dialog.ps1`: aciona o botão **Configurações** pela interface de automação do próprio controle WinUI, confirma que o modal abre e renderiza a opção, e fecha a instância de teste sem salvar configurações.

## Verificações

Com autorização para compilar e executar testes:

```powershell
dotnet run --project .\tests\MCG.Launcher.Core.Tests -c Release
```

Os testes criam fixtures isoladas na pasta temporária e as preservam para inspeção. Não executam jogos nem usam sua biblioteca real.

Para verificar a inicialização da publicação, com o launcher fechado:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-launch.ps1
```

Essa verificação abre o aplicativo por 10 segundos, confirma que a janela responde e solicita seu fechamento normal. A detecção automática da primeira abertura pode salvar os jogos encontrados na biblioteca local. A verificação não abre jogos.

Para verificar o botão e a abertura do modal de configurações, com o launcher fechado:

```powershell
.\scripts\check-settings-dialog.ps1
```

O teste usa a janela real do WinUI e preserva um relatório JSON em `artifacts\checks`. Não modifica as configurações nem a biblioteca.

Verificação em 08/10/2026: **25 testes do núcleo passaram, sem falhas**, incluindo descoberta de imagens locais, diferenciação entre capas/logos/ícones, preservação de imagens personalizadas e persistência das configurações. A publicação completa foi atualizada em `artifacts\launcher`, e o teste confirmou que clicar em **Configurações** abre o modal e renderiza **Desativar imagens**. A atualização anterior da pasta original havia falhado por arquivos bloqueados pelo launcher aberto; o script agora detecta esse caso antes de publicar. A aparência ainda precisa de validação manual.

Uma falha anterior de abertura da publicação foi corrigida habilitando `EnableMsixTooling`: isso gera e publica `MCG.Launcher.pri`, o índice que o WinUI usa para carregar a interface, mesmo sem pacote MSIX. O script de build verifica sua presença. A abertura de jogos reais ainda precisa de validação manual.
