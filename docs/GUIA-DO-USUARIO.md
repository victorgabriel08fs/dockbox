# Guia do usuário do Dockbox

O Dockbox é um Dock no estilo do macOS para Windows 10 e 11. Ele fica numa borda da tela,
mostra seus apps favoritos, indica quais estão abertos e tem widgets (relógio, clima, lixeira e outros).

## Instalar e abrir

1. Baixe o `Dockbox.exe` (aba **Actions** do GitHub → execução mais recente do *build* → artefato `Dockbox-win-x64`).
2. Dê dois cliques em `Dockbox.exe`. Não precisa instalar nada.
3. Se aparecer **"O Windows protegeu o computador"**, clique em **Mais informações** → **Executar assim mesmo**.
   Isso acontece porque o executável ainda não tem assinatura digital.

Só roda um Dockbox por vez: abrir de novo não cria um segundo Dock.

Para abrir junto com o Windows: botão direito no Dock → **Gerenciar Dock...** → **Comportamento** →
**Iniciar o Dockbox junto com o Windows**. Se você mover o `Dockbox.exe` de pasta depois, ligue a opção de novo.

Para fechar: botão direito no Dock → **Sair do Dockbox**.

## Usar o Dock

O Dock some sozinho durante vídeo, jogo ou apresentação em tela cheia e volta depois.

| Ação | O que acontece |
|---|---|
| Clique num app fechado | Abre o app, com uma animação de pulo |
| Clique num app aberto | Traz a janela para a frente; se ela já está na frente, minimiza |
| **Ctrl** + clique | Abre uma nova janela/instância do app |
| Passar o mouse | Os ícones crescem e o nome aparece |
| Ponto embaixo do ícone | O app está aberto |
| Ícones depois do separador | Apps abertos que não estão fixados; somem quando fecham. Botão direito → **Manter no Dock** para fixar |
| Botão direito num app | Janelas abertas do app (clique para trazer), Nova janela, Mostrar no Explorador, Remover do Dock, Fechar e Opções do Dock |
| Botão direito num widget | Opções do widget e Remover widget |
| Botão direito no fundo | Gerenciar Dock, ampliação, ocultação automática, posição na tela, sair |
| Menus pelo teclado | Setas para navegar, Enter para escolher, Esc para fechar |

### Arrastar e soltar

- **Reordenar**: arraste um ícone do Dock para o lado; os vizinhos abrem espaço e ele entra onde você soltar.
- **Remover**: arraste o ícone para longe do Dock até aparecer **Remover** e solte.
- Um clique rápido continua abrindo o app; o arraste só começa depois que o mouse anda alguns pixels.
- Os widgets ficam fixos depois dos apps; reordene-os em Gerenciar Dock → Widgets.

- **Fixar um item**: arraste um `.exe`, atalho, pasta ou arquivo para um espaço vazio do Dock.
  Ele entra na posição em que você soltou.
- **Abrir arquivos com um app**: solte documentos em cima do ícone de um app (por exemplo, um `.txt` no Bloco de Notas).
- **Apagar arquivos**: solte arquivos na **Lixeira**. Eles vão para a Lixeira do Windows e podem ser restaurados de lá.
  O Dockbox pergunta antes de apagar.

## Gerenciar Dock

Abra pelo botão direito no Dock → **Gerenciar Dock...**. As mudanças aparecem no Dock na hora.

**Itens do Dock**
- **Adicionar app**: lista os apps do Menu Iniciar e os da Microsoft Store (Calculadora, Fotos, Configurações...). Dá para buscar e escolher vários com Ctrl + clique.
- **Adicionar arquivo...** e **Adicionar pasta...**: escolha qualquer arquivo ou pasta.
- Setas: mudam a ordem. Lixeira: remove do Dock (não apaga o app).
- **Item selecionado**: edite nome, caminho e argumentos de linha de comando.
  O caminho aceita variáveis como `%USERPROFILE%\Downloads`.

**Widgets**: ligue, desligue e mude a ordem. Widgets ficam no fim do Dock, depois do separador.

| Widget | Mostra | Clique |
|---|---|---|
| Relógio | Relógio analógico; data e hora no rótulo | — |
| Calendário | Dia de hoje | Abre o mês |
| Clima | Temperatura e condição | Abre a previsão no navegador |
| Monitor do sistema | Uso de CPU e RAM | Abre o Gerenciador de Tarefas |
| Bateria | Carga (só aparece em notebooks) | Abre a economia de bateria |
| Mídia | Música ou vídeo tocando agora | Tocar/pausar |
| Lixeira | Se tem itens | Abre a Lixeira; botão direito para esvaziar |

**Clima e privacidade**: digite sua cidade em **Cidade do clima** e clique em **Aplicar**. A previsão vem do
`open-meteo.com`. Se preferir não digitar, ligue **Sem cidade: detectar pela internet**: aí o Dockbox descobre sua
localização pelo endereço IP usando o serviço `ipwho.is` (seu IP é enviado a ele). Essa opção vem desligada.

**Estilo**: tema (automático, claro, escuro ou personalizado), cor do fundo, cor de destaque, opacidade,
cantos arredondados, tamanho dos ícones, intensidade da ampliação e posição (embaixo, em cima, esquerda, direita).

**Comportamento**: mostrar nome, ponto de app aberto, animação de pulo, separador, ocultar automaticamente
(com o tempo até aparecer), reservar espaço na tela (janelas maximizadas param antes do Dock) e iniciar com o Windows.

Com **Ocultar e mostrar o Dock automaticamente**, o Dock sai da tela quando o mouse vai embora e volta quando
você encosta o mouse na borda, na altura do Dock. Também dá para ligar e desligar pelo botão direito no Dock.
Se a barra de tarefas estiver na mesma borda, encoste o mouse logo acima dela.
**Restaurar padrões** volta estilo, widgets e comportamento ao original, mas mantém seus apps.

## Segurança dos itens

Apps (`.exe`), atalhos, pastas, arquivos comuns e links `http`/`https` abrem direto. Para o resto o Dockbox
pergunta antes, uma vez por item:

- **Scripts e instaladores** (`.bat`, `.cmd`, `.ps1`, `.vbs`, `.js`, `.hta`, `.msi`, `.reg`...), inclusive por trás de um atalho.
- **Protocolos desconhecidos** (por exemplo `steam://`) e arquivos `.url` que não apontam para um site.
- **Interpretadores com argumentos**, como `powershell.exe -c ...` ou `cmd.exe /c ...`.

Se você confirmar, o Dockbox lembra e não pergunta mais para aquele item; mudar o caminho ou os argumentos
no Gerenciar Dock faz perguntar de novo. Fixar um script arrastando também mostra o aviso.

Protocolos que já foram usados em ataques ao Windows (`ms-msdt:`, `search-ms:`, `ms-officecmd:`, links
`ms-word:` e parecidos) nunca são abertos pelo Dock.

## Onde ficam as configurações

Tudo fica em `%APPDATA%\Dockbox\` (botão **Abrir pasta de configurações** no Gerenciar Dock):

| Arquivo | Para que serve |
|---|---|
| `settings.json` | Itens, widgets, estilo e comportamento |
| `settings.json.bak` | Versão anterior, gravada a cada mudança |
| `settings.json.corrompido-AAAAMMDD-HHMMSS` | Cópia de um arquivo que não pôde ser lido |
| `erros.log` | Erros inesperados, útil para relatar problemas |
| `pinned.json` | Lista da primeira versão; importada automaticamente uma vez |

Dá para editar o `settings.json` à mão com o Dockbox fechado. Se o arquivo ficar inválido, o Dockbox avisa, guarda
uma cópia `.corrompido-...` e volta aos padrões; para recuperar, corrija a cópia e renomeie para `settings.json`.

## Problemas comuns

| Problema | O que fazer |
|---|---|
| O Dock cobre a parte de baixo de janelas maximizadas | Ligue **Reservar espaço na tela** em Gerenciar Dock → Comportamento |
| A área de trabalho ficou menor depois que o Dockbox fechou | Abra e feche o Dockbox de novo, ou reinicie o Explorador; isso só acontece se o processo for encerrado à força |
| Só aparece no monitor principal | Limitação atual (suporte a vários monitores está previsto) |
| Clima pede para definir a cidade | Digite a cidade no Gerenciar Dock → Widgets, ou ligue a detecção pela internet |
| Clima mostra "indisponível" | Sem internet ou cidade não encontrada. Tente o nome da cidade sem estado/país |
| Ícone em branco | O caminho do item não existe mais. Edite o caminho no Gerenciar Dock |
| Clique num app aberto não traz a janela | O Windows às vezes bloqueia trocar de janela; clique de novo ou use Ctrl + clique |
| Sumiu tudo depois de editar o arquivo | Procure `settings.json.corrompido-*` ou `settings.json.bak` na pasta de configurações |
