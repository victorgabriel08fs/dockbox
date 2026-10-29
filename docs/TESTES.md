# Testes do Dockbox

## Testes automáticos

Projeto `tests/Dockbox.Tests` (xUnit). Precisa do Windows, porque usa tipos do WPF.

```powershell
dotnet test tests/Dockbox.Tests
```

O GitHub Actions roda os testes antes de gerar o `Dockbox.exe`.

| Arquivo | O que cobre |
|---|---|
| `SettingsStoreTests.cs` | Primeira execução, salvar/carregar, arquivo corrompido (cópia de segurança), `.bak`, migração do `pinned.json`, limites de valores, cores inválidas, widgets desconhecidos, enums |
| `ColorUtilTests.cs` | Validação e conversão de cores, alfa, mistura, luminância |
| `DockThemeTests.cs` | Tema claro/escuro/personalizado, opacidade, contorno visível |
| `ShellLinksTests.cs` | Chave do executável (caminho normalizado), variáveis de ambiente, atalho inexistente |
| `DockWidgetTests.cs` | Geometria do anel dos widgets, criação de todos os widgets |

Os testes de `SettingsStore` usam uma pasta temporária (`SettingsStore.UseFolder`) e nunca tocam no
`%APPDATA%` real. Como o store é estático, eles rodam em sequência.

Alguns testes protegem bugs encontrados na revisão e já corrigidos: perda da lista com JSON inválido,
`"Edge": 99` deixando o Dock sem posição, aviso de arquivo corrompido e localização por IP desligada por padrão.

O que não tem teste automático (precisa de teste manual abaixo): tudo que depende da tela, do mouse,
de outras janelas ou da internet.

## Checklist de teste manual (Windows)

Marque cada item. Antes de começar, faça uma cópia da pasta `%APPDATA%\Dockbox`.

### 1. Primeira execução
- [ ] Apague (ou renomeie) `%APPDATA%\Dockbox` e abra o `Dockbox.exe`.
- [ ] O aviso do SmartScreen aparece e **Executar assim mesmo** funciona.
- [ ] O Dock aparece embaixo, centralizado, com apps padrão e relógio, calendário, clima e lixeira.
- [ ] `settings.json` foi criado. Com um `pinned.json` antigo na pasta, os apps dele foram importados.
- [ ] O item padrão "Terminal" abre o Windows Terminal (se instalado); sem ele, o PowerShell.
- [ ] Abrir o `Dockbox.exe` de novo não cria um segundo Dock.
- [ ] O `Dockbox.exe` mostra o ícone do Dockbox no Explorador (ícones pequenos, médios e grandes).

### 2. Foco e janelas
- [ ] Com o Bloco de Notas em foco, clique no fundo do Dock: o cursor continua piscando no Bloco de Notas.
- [ ] O Dock não aparece no Alt+Tab nem na barra de tarefas.
- [ ] Clique nas áreas transparentes acima do Dock (sem ícone): o clique chega na janela de trás.
- [ ] Maximize uma janela: anote se o Dock cobre o conteúdo (limitação conhecida).
- [ ] Vídeo em tela cheia (YouTube, F11), apresentação do PowerPoint e um jogo: o Dock some e volta ao sair da tela cheia.

### 3. Ampliação e rótulos
- [ ] Passe o mouse devagar da esquerda para a direita: ícones crescem suavemente, sem tremer.
- [ ] Rótulo com o nome aparece acima do ícone certo e não sai da tela nos ícones das pontas.
- [ ] Saia do Dock pelo topo dos ícones ampliados: tudo volta ao tamanho normal.
- [ ] Desligue a ampliação pelo menu: ícones não crescem mais.
- [ ] Gerenciador de Tarefas: com o mouse longe do Dock, uso de CPU do Dockbox perto de 0%.

### 4. Abrir e alternar apps
- [ ] Clique num app fechado: abre e o ícone pula (duas vezes, o segundo pulo menor).
- [ ] Aparece o ponto embaixo do app aberto em até 1 s; some quando o app é fechado.
- [ ] Clique no app aberto que está atrás de outra janela: vem para a frente.
- [ ] Clique de novo no app em primeiro plano: minimiza. Mais um clique: restaura.
- [ ] Ctrl + clique abre uma segunda janela.
- [ ] App fixado por atalho `.lnk` também ganha o ponto.
- [ ] Item com caminho que não existe: aparece mensagem de erro clara, o Dock não fecha.
- [ ] Clique pressionado e arraste para fora do ícone antes de soltar: não abre nada.

### 5. Arrastar e soltar
- [ ] Arraste um `.exe` do Explorador para um espaço entre dois ícones: entra naquela posição.
- [ ] Arraste uma pasta e um atalho: são fixados.
- [ ] Solte um `.txt` em cima do Bloco de Notas: abre o arquivo nele.
- [ ] Confirme um arquivo de teste na Lixeira: vai para a Lixeira do Windows e pode ser restaurado.
- [ ] Solte arquivos na Lixeira: aparece a pergunta "Mover ... para a Lixeira?"; **Não** mantém o arquivo, **Sim** apaga.
- [ ] Arraste texto ou imagem de um navegador: nada acontece, sem erro.
- [ ] Arraste vários arquivos de uma vez: todos entram, na ordem.
- [ ] Ao arrastar um arquivo de fora, os ícones abrem espaço mostrando onde ele vai entrar; sair do Dock fecha o espaço.
- [ ] Arraste um ícone do Dock para o lado: os vizinhos abrem espaço e a nova ordem fica salva (feche e abra o Dockbox).
- [ ] Arraste um ícone para longe do Dock: aparece "Remover"; soltar remove com uma nuvenzinha. Voltar antes de soltar cancela.
- [ ] Clique rápido (sem mover) continua abrindo o app; apertar, sair do ícone e soltar fora não abre nada.
- [ ] Widgets não arrastam, mas o clique neles continua funcionando.

### 5a. Desfazer e duplicados
- [ ] Remova um item pelo menu: aparece "… removido · Desfazer"; clicar devolve na mesma posição. Sem clicar, o aviso some em 6 s.
- [ ] Remova arrastando para fora e pelo Gerenciar Dock: o mesmo aviso aparece.
- [ ] No Gerenciar Dock, remova dois itens e aperte Ctrl+Z duas vezes: voltam na ordem certa. Ctrl+Z dentro de um campo de texto desfaz o texto.
- [ ] Arraste para o Dock o `.exe` de um app já fixado por atalho: não duplica, o ícone existente pula.
- [ ] Adicione pelo seletor um app já fixado: não duplica e o existente fica selecionado.
- [ ] Com auto-ocultar ligado, remover mostra o Dock com o aviso até ele sumir.

### 5b. Apps abertos não fixados e apps da Store
- [ ] Abra um app que não está no Dock: ganha um ícone depois dos fixados (com separador) e ponto; fechar remove o ícone.
- [ ] Clique nesse ícone: traz/minimiza a janela. Botão direito → **Manter no Dock**: passa para os fixados e continua depois de reiniciar.
- [ ] As janelas do próprio Dockbox (Gerenciar Dock) não aparecem como app aberto.
- [ ] Gerenciar Dock → Adicionar apps: a Calculadora e outros apps da Store aparecem com ícone; fixe a Calculadora.
- [ ] Abra a Calculadora fixada: o ponto aparece e ela não ganha ícone duplicado; clique traz/minimiza.
- [ ] Desligue **Mostrar apps abertos que não estão fixados** em Comportamento: a seção some.
- [ ] Janelas de ferramenta e invisíveis (ex.: bandeja, popups) não viram ícone.
- [ ] Reordenar funciona com o Dock embaixo, em cima e nas laterais.

### 6. Menus
- [ ] Botão direito num app fechado: cabeçalho com o nome, Abrir, Mostrar no Explorador (seleciona o arquivo),
      Remover do Dock e o submenu Opções do Dock.
- [ ] Botão direito num app aberto com 2+ janelas: lista as janelas pelo título (minimizadas marcadas); clicar traz aquela janela.
      Nova janela abre outra instância; **Fechar todas as janelas (N)** fecha (o app pode perguntar se quer salvar).
- [ ] Numa pasta fixada: "Mostrar a pasta no Explorador".
- [ ] Menus pelo teclado: setas, Enter e Esc funcionam; ao fechar com Esc o foco volta para o app que estava ativo.
- [ ] Botão direito num widget: opções do widget e Remover widget.
- [ ] Botão direito no fundo: Gerenciar Dock, ampliação, posição (marca a atual), Sair.
- [ ] Com o menu aberto, o rótulo some e os ícones não ficam ampliados presos.
- [ ] O ícone do Dockbox aparece na bandeja, nítido, com a dica "Dockbox".
- [ ] Clique esquerdo no ícone da bandeja abre o Gerenciar Dock; abrir de novo não cria outra janela.
- [ ] Botão direito na bandeja: Gerenciar Dock e Sair do Dockbox; Sair fecha o Dock e o ícone some da bandeja.
- [ ] Gerenciar Dock e Adicionar apps mostram o ícone do Dockbox na barra de título e na barra de tarefas.

### 7. Gerenciar Dock
- [ ] Abrir duas vezes pelo menu: só uma janela, que vem para a frente.
- [ ] **Adicionar app**: lista carrega sem travar, busca filtra, Ctrl + clique escolhe vários, duplo clique adiciona.
- [ ] Adicionar arquivo e pasta pelos diálogos.
- [ ] Setas reordenam e o Dock acompanha na hora. Remover tira do Dock.
- [ ] Editar nome: rótulo no Dock muda. Editar caminho para outro `.exe`: ícone muda.
- [ ] O caminho só é aplicado ao sair do campo ou apertar Enter. Digitar um caminho de rede (`\\algum-servidor\x`): nada trava.
- [ ] Argumentos: em um item `notepad.exe` coloque um caminho de arquivo entre aspas; ao clicar, abre o arquivo.
- [ ] Remover um item pelo Dock com o Gerenciar aberto: a lista atualiza.
- [ ] Com o Dock nas laterais, a dica das setas diz "mais acima/abaixo no Dock"; embaixo/em cima, "mais à esquerda/direita".
- [ ] Clicar várias cores seguidas no Estilo é instantâneo, mesmo com muitos itens (os ícones não piscam nem recarregam).
- [ ] Navegação só com teclado (Tab, setas, Espaço, Enter): anote o que não dá para alcançar.

### 8. Estilo e posição
- [ ] Tema Automático: troque o Windows entre claro e escuro; Dock e Gerenciar acompanham.
- [ ] Claro, Escuro e Personalizado com cada cor da paleta; cor hex digitada (com e sem `#`), Enter aplica,
      valor inválido volta ao anterior.
- [ ] Opacidade 0% (o contorno continua visível) e 100%.
- [ ] Cantos 0 e máximo; tamanho mínimo e máximo; ampliação mínima e máxima.
- [ ] Posição Embaixo, Esquerda, Direita e Em cima: ampliação, rótulos, pulo, ponto, menus e arrastar funcionam em todas.
- [ ] Mude a barra de tarefas de lugar ou de tamanho: o Dock se reposiciona.
- [ ] Escala de tela 100%, 125%, 150% e 200%: ícones nítidos, Dock no lugar certo.
- [ ] **Restaurar padrões**: estilo volta, apps continuam.

### 8a. Desfoque
- [ ] Estilo → **Desfocar o que está atrás do Dock** ligado: com uma janela colorida atrás, o fundo do Dock fica desfocado (Windows 10 e 11).
- [ ] O desfoque acompanha o fundo ao ampliar os ícones, sem sobrar desfoque fora do fundo nem nos cantos arredondados; mude os cantos (0 e 32 px) e confira.
- [ ] Nas 4 posições e em monitor com escala 150%: o desfoque fica exatamente atrás do fundo.
- [ ] Com Ocultar automaticamente: o desfoque some junto com o Dock e volta com ele.
- [ ] Arrastar uma janela atrás do Dock não fica lento.
- [ ] Configurações do Windows → Personalização → Cores → **Efeitos de transparência** desligado: o desfoque some em até 3 s; ligado de novo, volta.
- [ ] Economia de bateria ligada (notebook): o desfoque some. Alto contraste: some.
- [ ] Opção desligada: o fundo volta ao visual de antes.

### 8a2. Miniaturas das janelas
- [ ] Abra 2 ou 3 janelas do Bloco de Notas e do Explorador de Arquivos. Pare o mouse no ícone: em meio segundo aparece o painel com uma miniatura por janela, com título e ícone, encostado no ícone.
- [ ] As miniaturas são ao vivo: digite numa janela do Bloco de Notas visível e veja o texto aparecer na miniatura.
- [ ] Clique numa miniatura: aquela janela vem para a frente (mesmo minimizada) e o painel some.
- [ ] Clique no **X** de uma miniatura: a janela fecha (o Bloco de Notas com texto pergunta se quer salvar) e o painel se atualiza com as que sobraram.
- [ ] Janela minimizada: aparece com o ícone do app e "(minimizada)" no título.
- [ ] App com uma janela só: não aparece painel, só o rótulo com o nome.
- [ ] App da Store com 2 janelas (ex.: duas Calculadoras): as miniaturas aparecem.
- [ ] Com o painel aberto, passe o mouse para outro app com várias janelas: troca na hora, sem esperar. Para um app com uma janela ou um widget: o painel some.
- [ ] Leve o mouse do ícone até o painel: o painel não some no caminho. Tire o mouse do painel: some em menos de meio segundo.
- [ ] Nas 4 posições do Dock: o painel fica acima/abaixo/ao lado do ícone, dentro da tela, nas 4 bordas e com o ícone perto do canto da tela.
- [ ] Monitor com escala 150% e Dock em outro monitor: miniaturas no lugar certo dentro das caixas, sem cortar.
- [ ] Tema claro, escuro e alto contraste: o painel acompanha as cores; cantos arredondados acompanham a opção de cantos.
- [ ] Com Ocultar automaticamente: o Dock não some enquanto o mouse está no painel.
- [ ] Botão direito com o painel aberto: o painel some e o menu abre normalmente.
- [ ] Comportamento → desligue **Mostrar miniaturas das janelas**: o painel não aparece mais. Mude o tempo para 0 s e 1,5 s e confira a espera.
- [ ] Clicar no painel não tira o foco da janela que você estava usando (a não ser a janela escolhida).

### 8a4. Animação ao minimizar
- [ ] Comportamento → **Ao minimizar uma janela: Escala** (padrão). Minimize o Bloco de Notas (fixado no Dock): a janela encolhe até o ícone dele, sem a animação do Windows indo para a barra de tarefas.
- [ ] Minimize uma janela maximizada e uma janela pequena num canto: as duas saem do lugar certo.
- [ ] **Gênio**: a janela se estreita primeiro e depois desce até o ícone.
- [ ] Nas 4 posições do Dock: vai até o ícone certo (no Gênio com o Dock do lado, estreita a altura primeiro).
- [ ] App aberto que não está fixado (com "Mostrar apps abertos" ligado): vai até o ícone dele na parte de apps abertos. Com essa opção desligada: vai para o meio do Dock.
- [ ] Segundo monitor e escala 150%: a animação sai da janela e chega no ícone sem pular.
- [ ] Minimizar pelo botão, pela tecla Win+↓ e pelo clique no ícone do Dock: todos animam.
- [ ] Restaurar a janela: aparece direto, sem animação (a do Windows está desligada enquanto o efeito está ligado).
- [ ] **Nenhum**: a animação do Windows volta (minimizar vai para a barra de tarefas).
- [ ] Feche o Dockbox com o efeito ligado: a animação do Windows volta. Encerre o Dockbox pelo Gerenciador de Tarefas e abra de novo: volta também.
- [ ] Configurações do Windows → Acessibilidade → Efeitos visuais → **Efeitos de animação** desligado: sem efeito do Dockbox.
- [ ] Minimizar várias janelas rápido (Win+M): não trava; cada uma anima ou some.
### 8a3. Pedidos de atenção e contadores
- [ ] Pulo por atenção: abra o Bloco de Notas, deixe outra janela na frente e rode no PowerShell:
  ```powershell
  Add-Type -Name W -Namespace N -MemberDefinition '[DllImport("user32.dll")] public static extern bool FlashWindow(IntPtr h, bool i);'
  $h = (Get-Process notepad)[0].MainWindowHandle; [N.W]::FlashWindow($h, $true)
  ```
  O ícone do Bloco de Notas pula no Dock sem parar. Com Teams, Discord ou WhatsApp, uma mensagem nova com o app em segundo plano faz o mesmo.
- [ ] Clique no ícone (ou traga a janela pelo Alt+Tab): para de pular.
- [ ] Feche a janela que pedia atenção: para de pular.
- [ ] Comportamento → desligue **Pular quando um app pedir atenção**: não pula mais.
- [ ] "Mostrar animações" desligado no Windows: não pula; o Narrador lê "pedindo atenção" no item.
- [ ] Contador: abra o WhatsApp Web, o Discord ou o Gmail com mensagens não lidas (título "(3) ..."): aparece um círculo vermelho com o número no canto de cima à direita do ícone do navegador ou do app.
- [ ] Número maior que 99 mostra "99+". Ao ler as mensagens (título sem número), o contador some em até 1 s.
- [ ] O contador acompanha a ampliação e o pulo, e some ao arrastar o ícone.
- [ ] Nas 4 posições do Dock o contador fica dentro da tela (com o Dock em cima, fica embaixo à direita do ícone).
- [ ] Alto contraste: o contador usa as cores de destaque do Windows.
- [ ] Narrador: o item lê "Chrome, aberto, 3 não lidas".
- [ ] Comportamento → desligue **Mostrar contadores nos ícones**: os contadores somem em até 1 s.
- [ ] Título com número no meio ("Documento (1).docx") não mostra contador.

### 8a8. Novos widgets
Ligue os cinco em Gerenciar Dock → Widgets. Eles aparecem antes da Lixeira.
- [ ] **Volume**: role o mouse sobre ele: o volume do Windows muda de 2 em 2% e a barra acompanha. No touchpad, a rolagem com dois dedos também funciona. Clique: silencia (ícone riscado, "mudo"); clique de novo: volta. O rótulo mostra o nome do alto-falante. Troque o dispositivo padrão (ex.: fone Bluetooth): em até 1 s, o rótulo muda.
- [ ] Volume sem nenhum dispositivo de saída (desative nas configurações de som): o rótulo mostra "Nenhuma saída de som" e o Dock não trava.
- [ ] **Rede**: com o Wi-Fi ligado, aparecem o ícone de Wi-Fi e o nome da rede no rótulo; durante um download grande, a velocidade ↓ sobe. Com cabo, aparece o ícone de cabo. Com o modo avião ligado, aparece "sem rede". Clique: abre o status de rede do Windows.
- [ ] **Notas rápidas**: clique: abre o bloquinho amarelo perto do ícone. Digite, feche com Esc e reabra: o texto continua. O ícone mostra o começo da nota. Reinicie o Dockbox: a nota continua. Botão direito → **Abrir arquivo das notas** abre o `notas.txt`.
- [ ] **Temporizador**: clique: começa em 25 min e o anel vai enchendo. Clique: pausa (o rótulo diz "pausado"); clique: continua. Botão direito → **5 minutos**: recomeça com 5. Escolha 5 min e espere terminar: toca um som, o ícone fica vermelho e o rótulo diz "Tempo esgotado!". Clique: zera.
- [ ] **Área de transferência**: copie três textos (Ctrl + C) e clique no widget: aparecem os três, o mais novo primeiro. Clique no mais antigo: ele é copiado e sobe para o topo. Botão direito → **Pausar histórico**: o que você copiar depois não entra. **Limpar histórico** esvazia.
- [ ] Copie uma senha num gerenciador de senhas (ex.: Bitwarden, 1Password ou KeePass): ela não aparece no histórico.
- [ ] Reinicie o Dockbox: o histórico da área de transferência começa vazio (nada foi gravado em disco).

### 8a6. Ícones personalizados
- [ ] Botão direito num app → **Trocar ícone…** → escolha um PNG grande (ex.: 1024 px): o ícone muda na hora e fica nítido, sem serrilhado.
- [ ] Escolha um .ico com vários tamanhos: usa o maior.
- [ ] Escolha `C:\Windows\System32\shell32.dll`: abre a janela "Trocar ícone" do Windows; o escolhido aparece no Dock.
- [ ] Apague ou mova o PNG original e reinicie o Dockbox: o ícone continua.
- [ ] Gerenciar Dock → selecione o item: o ícone novo aparece na lista; **Restaurar padrão** fica ativo e volta ao ícone do app.
- [ ] Botão direito → **Restaurar ícone padrão** (só aparece com ícone personalizado).
- [ ] Pasta com "Mostrar no Dock como: Item mais recente" e ícone personalizado: o personalizado vale.
- [ ] Arquivo que não é imagem renomeado para .png: aparece um aviso e o ícone não muda.

### 8a5. Pilhas de pastas
- [ ] Instalação nova (sem settings.json): a pasta **Downloads** aparece no fim dos apps, com o ícone do último download.
- [ ] Clique em Downloads com poucos itens e o Dock embaixo: abre o **leque**, do mais novo para o mais velho, com "Abrir no Explorador" no topo.
- [ ] Botão direito → Ver conteúdo como → **Grade**: abre o painel com ícones; com mais de 30 itens, aparece a rolagem e o painel abre na hora (os ícones vão aparecendo).
- [ ] Pasta com mais de 400 itens: mostra os 400 primeiros e "+N itens: abra no Explorador".
- [ ] Na grade, clique numa subpasta: entra nela; **‹** e Backspace voltam; Esc volta e, na raiz, fecha.
- [ ] **Lista**: nomes longos aparecem cortados com "…", subpastas com "›".
- [ ] Clique num arquivo: abre no app padrão e a pilha fecha. Um .bat pergunta antes de rodar (mesma regra dos itens fixados).
- [ ] Arraste um arquivo da pilha para a área de trabalho ou para um e-mail: o arquivo vai junto.
- [ ] Com a pilha aberta, baixe um arquivo (ou crie um arquivo na pasta): aparece em menos de 1 s.
- [ ] Clique fora ou em outra janela: fecha. Clique de novo na mesma pasta no Dock: fecha. Clique em outro app no Dock: fecha a pilha e abre o app.
- [ ] Ctrl + clique na pasta: abre no Explorador.
- [ ] Ordenar por nome, data de adição, data de modificação e tipo: a ordem muda e fica salva por pasta.
- [ ] Mostrar no Dock como → **Item mais recente**: o ícone da pasta vira o do último arquivo e muda quando chega outro.
- [ ] Solte um arquivo do mesmo disco na pasta do Dock: é movido (janela do Windows se já existir um com o mesmo nome). De um pendrive: é copiado. Com Ctrl: copia. Com Shift: move.
- [ ] Soltar a própria pasta em cima dela: nada acontece.
- [ ] Dock em cima, à esquerda e à direita: grade ou lista encostada no ícone, dentro da tela (leque só com o Dock embaixo).
- [ ] Teclado: Tab e setas andam pelos itens, Enter abre, o Narrador lê o nome de cada item.
- [ ] Com ocultar automaticamente: o Dock não some enquanto a pilha está aberta.

### 8b. Reservar espaço na tela
- [ ] Com a opção ligada (padrão), maximize uma janela: ela para antes do Dock nas 4 posições.
- [ ] Mude o tamanho dos ícones: a área reservada acompanha na hora.
- [ ] Barra de tarefas embaixo, em cima e à esquerda: o Dock e a reserva não ficam por cima dela.
- [ ] Desligue a opção: a janela maximizada volta a ocupar a tela toda (o Dock fica por cima).
- [ ] Saia pelo menu: a área de trabalho volta ao tamanho inteiro.
- [ ] Encerre o `Dockbox.exe` pelo Gerenciador de Tarefas e anote se a área volta sozinha (pode precisar abrir e fechar de novo).
- [ ] Vídeo em tela cheia: o Dock some e não aparece faixa vazia.

### 8c. Ocultar automaticamente
- [ ] Ligue em Comportamento (ou pelo botão direito no Dock): o Dock desliza para fora e a reserva de espaço é liberada.
- [ ] Encoste o mouse na borda, na altura do Dock: ele aparece depois do tempo configurado; tirar o mouse esconde de novo.
- [ ] Passar o mouse rápido pela borda não faz o Dock piscar.
- [ ] Funciona nas 4 posições; com a barra de tarefas na mesma borda, a faixa de ativação fica logo acima dela.
- [ ] Com o menu do botão direito aberto, o Dock não some.
- [ ] Arraste um arquivo do Explorador até a borda: o Dock aparece e dá para soltar num app ou na Lixeira.
- [ ] Desligar: o Dock volta a ficar fixo e a reserva de espaço volta (se estiver ligada).

### 9. Widgets
- [ ] Relógio: ponteiros na hora certa; rótulo com dia da semana; opção 24 h muda o formato.
- [ ] Calendário: dia certo; clique abre o mês; clique fora fecha; vira o dia à meia-noite.
- [ ] Clima com cidade vazia e detecção desligada (padrão): rótulo pede para definir a cidade, nenhuma conexão com ipwho.is.
- [ ] Ligue "detectar pela internet": mostra sua cidade em até 1 min. Com "São Paulo": muda após Aplicar.
      Cidade inexistente: "Clima indisponível". Sem internet (modo avião): não trava, mostra indisponível.
- [ ] Monitor: CPU/RAM batem com o Gerenciador de Tarefas; clique abre o Gerenciador.
- [ ] Bateria: aparece no notebook, some no desktop; tirar o carregador muda o rótulo.
- [ ] Mídia: Spotify ou YouTube tocando mostram título/capa; clique pausa e retoma.
- [ ] Lixeira: desenho muda com itens; Esvaziar pede confirmação do Windows.
- [ ] Desligar e religar cada widget; mudar a ordem.

### 10. Configurações e falhas
- [ ] Com o Dockbox aberto, abra o exe de novo: aparece "O Dockbox já está aberto". **Não**: nada muda. **Sim**: o aberto fecha e este abre.
- [ ] Com uma versão antiga aberta (sem esse aviso), abrir a nova e clicar **Sim**: a antiga é encerrada e a nova abre.
- [ ] Com um Dockbox aberto como administrador, abrir outro normal e clicar **Sim**: aparece a explicação com o `taskkill`.
- [ ] Com "Ocultar a barra de tarefas" ligado, trocar pela versão nova: a barra não fica escondida para sempre.
- [ ] Feche o Dockbox, coloque um erro de digitação no `settings.json` e abra:
      aparece um aviso com o local da cópia, o Dock abre com padrões e o `settings.json.corrompido-...` tem o conteúdo antigo.
- [ ] Faça duas mudanças: `settings.json.bak` tem a penúltima versão.
- [ ] Deixe `settings.json` somente leitura e mude algo: o Dock não fecha, aparece o aviso "Não foi possível salvar" com o motivo
      e o caminho (uma vez só), e o Gerenciar Dock mostra uma faixa vermelha com **Tentar salvar de novo**.
- [ ] Tire o somente leitura e clique em **Tentar salvar de novo**: a faixa some e a mudança está no arquivo.
- [ ] Com o arquivo ainda somente leitura, feche o Dockbox: nada trava (a mudança se perde, como avisado).
- [ ] Iniciar com o Windows: ligue, reinicie, o Dock abre sozinho; desligue, reinicie, não abre.
- [ ] Com o `Dockbox.exe` em Downloads, ligue a opção: aparece o aviso. **Sim** copia para `%LOCALAPPDATA%\Programs\Dockbox`,
      o Dock reabre de lá (sem ficar dois Docks) e o registro (`HKCU\...\Run\Dockbox`) aponta para a cópia, entre aspas.
      **Não** liga usando Downloads; **Cancelar** não liga.
- [ ] Ligue a opção com o exe numa pasta fixa, feche, mova o exe para outra pasta e abra: o registro passa a apontar para o novo lugar.
- [ ] Deixe aberto por 1 h trocando de apps: memória do Dockbox no Gerenciador de Tarefas estável.
- [ ] Após algum erro, existe `erros.log` com detalhes, data/hora e versão em cada registro.
- [ ] Encha o `erros.log` até passar de 1 MB e provoque outro erro: o antigo vira `erros.1.log` e um novo começa.

### 10b. Segurança dos itens
- [ ] Apps, pastas, arquivos comuns e um `.url` de site abrem sem pergunta.
- [ ] Fixe um `.bat` arrastando: aparece o aviso; **Não** não fixa, **Sim** fixa e o primeiro clique já abre sem perguntar.
- [ ] Adicione um `.bat` pelo Gerenciar Dock → Arquivos: o primeiro clique pergunta; depois de **Sim**, não pergunta mais.
- [ ] Mude o caminho ou os argumentos desse item no Gerenciar Dock: pergunta de novo.
- [ ] Item `cmd.exe` com argumentos `/k echo oi`: pergunta. Sem argumentos: abre direto.
- [ ] No `settings.json`, um item com caminho `ms-msdt:/id PCWDiagnostic`: clicar mostra "bloqueado" e nada abre.
- [ ] Item `steam://open/games` (com a Steam instalada): pergunta uma vez.

### 10c. Acessibilidade
- [ ] **Win+Shift+D** com outro app em foco: o Dock ganha foco, o primeiro item fica com contorno e o nome aparece.
- [ ] Setas, Tab/Shift+Tab, Home e End movem o contorno (em qualquer borda); a ampliação acompanha.
- [ ] Enter abre o app; Ctrl+Enter abre nova janela; Enter num app aberto traz a janela para frente.
- [ ] Shift+F10 (e a tecla de menu) abre o menu do item ao lado dele; setas e Enter funcionam no menu; Esc fecha e o contorno continua no item.
- [ ] Esc sai e o foco volta para o app de antes. Apertar o atalho de novo também sai.
- [ ] Com ocultar automaticamente: o atalho mostra o Dock, que só some depois de sair com Esc.
- [ ] Clicar em outra janela durante o modo teclado sai dele.
- [ ] Comportamento → Teclado: trocar para Ctrl+Shift+F12 funciona na hora; Desligado desativa. Com um atalho usado por outro programa aparece o aviso.
- [ ] Narrador (Ctrl+Win+Enter): ao andar com as setas, lê o nome do app e "aberto" quando o app está aberto; widgets leem o que mostram (ex.: hora).
- [ ] No Gerenciar Dock, com Narrador, os controles deslizantes, campos e botões de mover/remover têm nome.
- [ ] Configurações → Acessibilidade → Efeitos visuais → Efeitos de animação desligado: o Dock não amplia, não pula e o auto-ocultar não desliza (aparece/some direto).
- [ ] Alto contraste ligado (Alt+Shift esquerdo+Print Screen): o Dock fica com fundo e texto do tema de contraste, sem transparência.

### 10c2. Ocultar a barra de tarefas
- [ ] Comportamento → **Ocultar a barra de tarefas do Windows**: a barra some em todos os monitores e as janelas maximizadas usam o espaço dela.
- [ ] Tecla Windows abre o Iniciar; ao fechar o Iniciar, a barra some de novo em até 1 segundo.
- [ ] Botão direito no Dock → **Abrir o menu Iniciar** funciona; **Mostrar a barra de tarefas** traz a barra de volta.
- [ ] Sair do Dockbox (menu do Dock): a barra volta.
- [ ] Com a opção ligada, encerre o Dockbox pelo Gerenciador de Tarefas e abra de novo com a opção desligada: a barra volta ao normal.
- [ ] Se a barra já estava com "Ocultar automaticamente" do Windows antes, ela continua assim depois de desligar a opção.
- [ ] Reinicie o Explorer (Gerenciador de Tarefas → Windows Explorer → Reiniciar) com a opção ligada: a barra some de novo sozinha.
- [ ] Conectar um segundo monitor com a opção ligada: a barra dele também some.

### 10d. Idiomas
- [ ] Windows em português, sem `settings.json`: tudo em português; itens padrão com nomes em português.
- [ ] Comportamento → Idioma → **English**: a janela reabre na mesma página em inglês; botão direito no Dock, menu da bandeja, rótulos dos widgets (Relógio, Calendário, Clima, Lixeira) e aviso de Desfazer em inglês.
- [ ] **Español**: o mesmo em espanhol. Data do calendário em espanhol ("Jueves, 29 de octubre de 2026").
- [ ] **Automático**: volta ao idioma do Windows; o botão mostra qual é (ex.: "Automático (Português)").
- [ ] Feche e abra o Dockbox: o idioma escolhido continua.
- [ ] Clima com cidade digitada: em inglês, a descrição fica em inglês (ex.: "Partly cloudy").
- [ ] Narrador lê os itens no idioma escolhido ("Notepad, open").
- [ ] Mensagens: arrastar arquivo para a Lixeira pergunta no idioma escolhido.
- [ ] Opcional: Windows em espanhol ou inglês (Configurações → Hora e idioma) com Idioma em Automático: o Dock segue.

### 11. Monitores
Precisa de dois monitores (dá para usar uma TV ou um monitor emprestado).
- [ ] Padrão: o Dock fica no principal.
- [ ] Aparência → Monitor → **Escolher**: aparece a lista com número e resolução; clicar no secundário leva o Dock para lá.
- [ ] Feche e abra o Dockbox: o Dock continua no secundário.
- [ ] Com o Dock no secundário, desconecte o cabo: o Dock vai para o principal e a dica avisa que o monitor não está conectado. Reconecte: o Dock volta.
- [ ] Monitores com escalas diferentes (ex.: 100% e 150%): o Dock fica com o tamanho certo nos dois, sem cortar ícones.
- [ ] Mude a resolução do monitor do Dock: ele se ajusta sem reiniciar.
- [ ] **Seguir o mouse**: pare o mouse meio segundo na borda de baixo do outro monitor: o Dock vai para lá. Passar rápido pela borda não muda nada. A borda entre os dois monitores não conta.
- [ ] Com **Reservar espaço** ligado, no secundário: janelas maximizadas nele param antes do Dock, e o principal volta a usar a tela toda.
- [ ] Com **Ocultar automaticamente** no secundário: o Dock some e volta ao encostar na borda dele.
- [ ] Dock em outra borda (ex.: esquerda) com Seguir o mouse: a dica fala da borda certa e funciona do mesmo jeito.

### 11b. Escala da tela (DPI)
Em Configurações do Windows → Sistema → Tela → Escala. Dá para testar com um monitor só.
- [ ] A 100%, 125%, 150% e 200%: ícones do Dock nítidos, também ampliados ao passar o mouse.
- [ ] Nas mesmas escalas: ícones da lista do Gerenciar Dock e da janela "Adicionar apps" nítidos, sem borrão.
- [ ] Mudar a escala com o Dockbox aberto: o Dock se ajusta sem reiniciar (os ícones do Gerenciar Dock ficam nítidos ao reabrir a janela).
- [ ] Com dois monitores de escalas diferentes: o Dock tem o mesmo tamanho visual nos dois ao trocar.
