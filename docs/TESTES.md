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
- [ ] Reordenar funciona com o Dock embaixo, em cima e nas laterais.

### 6. Menus
- [ ] Botão direito num app: cabeçalho com o nome, Abrir/Mostrar, Nova janela (só se aberto),
      Mostrar no Explorador (seleciona o arquivo), Remover do Dock.
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
- [ ] Feche o Dockbox, coloque um erro de digitação no `settings.json` e abra:
      aparece um aviso com o local da cópia, o Dock abre com padrões e o `settings.json.corrompido-...` tem o conteúdo antigo.
- [ ] Faça duas mudanças: `settings.json.bak` tem a penúltima versão.
- [ ] Deixe `settings.json` somente leitura e mude algo: o Dock não fecha (anote se avisa que não salvou).
- [ ] Iniciar com o Windows: ligue, reinicie, o Dock abre sozinho; desligue, reinicie, não abre.
- [ ] Deixe aberto por 1 h trocando de apps: memória do Dockbox no Gerenciador de Tarefas estável.
- [ ] Após algum erro, existe `erros.log` com detalhes.

### 11. Monitores
- [ ] Com dois monitores, o Dock fica no principal (limitação conhecida).
- [ ] Trocar qual é o monitor principal ou desconectar um: anote o comportamento.
