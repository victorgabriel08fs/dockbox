# Arquitetura do Dockbox

App de desktop em C# + WPF (.NET 8, `net8.0-windows10.0.19041.0`), um único processo e uma janela principal.
Compila no Linux (`EnableWindowsTargeting`), mas só roda no Windows 10 1809+ e 11.

## Visão geral

```
App.xaml.cs ── carrega SettingsStore, garante instância única, registra erros em erros.log (Core/ErrorLog, com rotação em 1 MB)
   │
   ├── Views/DockWindow ──────── janela transparente, topmost, sem foco
   │     ├── tiles de app ─────── IconLoader, ShellLinks, RunningApps, AppLauncher
   │     ├── tiles de widget ──── Widgets/* (DockWidget) → Services/*
   │     └── menus e drag-drop
   │
   ├── Views/SettingsWindow ──── "Gerenciar Dock": edita SettingsStore.Current e chama Commit()
   │     └── Views/AppPickerWindow ── StartMenuApps
   │
   └── Core/SettingsStore ────── settings.json  ⇄  evento Changed  →  DockWindow.ApplySettings()
```

O fluxo de dados é simples: `SettingsStore.Current` é o único estado persistido. Quem muda algo
altera `Current` e chama `SettingsStore.Commit()`, que normaliza, grava em disco e dispara `Changed`.
O Dock escuta `Changed` e se redesenha (`ApplyTheme` + `RebuildTiles` + `PositionWindow`).

## Pastas

| Pasta | Conteúdo | Depende de WPF? |
|---|---|---|
| `Core/` | `DockSettings` + `SettingsStore` (modelo e persistência), `DockTheme` (cores derivadas), `ColorUtil` | `ColorUtil`/`DockTheme` usam tipos de mídia |
| `Services/` | Integração com o Windows: ícone da bandeja (`TrayIcon`, via WinForms `NotifyIcon`), ícones, atalhos `.lnk`, janelas abertas, abrir apps, clima, mídia, lixeira, CPU/RAM, autostart | Alguns (ícones, mídia) |
| `Widgets/` | `DockWidget` (classe base) e um arquivo por widget; `WidgetFactory` cria por nome | Sim |
| `Views/` | `DockWindow` (+ `DockWindow.Previews.cs`), `SettingsWindow`, `AppPickerWindow`, `BlurBackdrop`, `WindowPreview` | Sim |
| `Interop/` | `NativeMethods`: P/Invoke de user32, gdi32, shell32, dwmapi, kernel32 | Não |
| `Assets/` | `Dockbox.ico` (exe, janelas, bandeja) e `Dockbox-128.png`; gerados de `assets/brand/` por `tools/build-icons.js` | — |
| `tests/Dockbox.Tests` | Testes xUnit | — |

## Janela do Dock

- **Sem foco**: `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` e resposta `MA_NOACTIVATE` a `WM_MOUSEACTIVATE`.
  Clicar no Dock não tira o foco do app atual e o Dock não aparece no Alt+Tab.
- **Tamanho**: a janela ocupa a largura (ou altura) inteira da área de trabalho do monitor do Dock, com
  espessura suficiente para o ícone ampliado, o pulo e o rótulo. As partes transparentes deixam o clique passar.
- **Monitores**: `Monitors.All()` lista os monitores (`EnumDisplayMonitors`, `GetDpiForMonitor`) e `Screens.Pick`
  escolhe o do Dock (principal, escolhido ou o último para onde o mouse levou), voltando ao principal se o
  monitor sumir. A janela é posicionada em pixels físicos com `SetWindowPos`, porque cada monitor pode ter
  outra escala; em `DpiChanged`, `WM_DISPLAYCHANGE` e `DisplaySettingsChanged` a posição é refeita.
- **DPI**: o app é `PerMonitorV2` (`app.manifest`). Os ícones do Dock são extraídos a 256 px; nas janelas,
  `IconLoader.LoadForDisplay` pede o menor tamanho padrão que cubra o tamanho em DIPs vezes a maior escala
  entre os monitores. Se a fábrica de imagens do Shell falhar, usa o ícone de 48 px da lista do sistema.
- **Hover**: `HitArea` ganha alfa 1 enquanto o mouse está no Dock, para o mouse não "cair" entre ícones ampliados.
- **Animação**: parado, um `DispatcherTimer` de 50 ms só verifica o mouse. Com o mouse no Dock, a animação
  roda em `CompositionTarget.Rendering` e para sozinha quando tudo assenta.
- **Ampliação**: escala alvo `1 + (Max-1)·(cos(π·d)+1)/2`, com `d` medido nas posições em repouso
  (não oscila) e suavização exponencial por frame.
- **Coordenadas**: todo o layout é calculado num eixo "principal" (ao longo do Dock) e num eixo "cruzado"
  (distância da borda); `PlaceRect`/`PlacePos` convertem para o Canvas conforme a borda escolhida.
- **Tela cheia**: se `SHQueryUserNotificationState` indica app em tela cheia, jogo D3D ou apresentação,
  o Dock se esconde e para de reafirmar o topmost.
- **Timer lento (1 s)**: reafirma `HWND_TOPMOST` a cada 3 s, atualiza o ponto de app aberto
  (`RunningApps.Snapshot`) e chama `UpdateAsync` dos widgets cujo `Interval` venceu.

## Apps abertos

`RunningApps.Snapshot()` enumera janelas visíveis, sem dono, com título, que não são toolwindow nem
"cloaked", e agrupa pelo caminho do executável (`QueryFullProcessImageName`). Cada item do Dock tem uma
`ExeKey` (caminho do `.exe` em minúsculas, resolvendo `.lnk` com `ShellLinks`). Apps UWP/Store e itens que
não apontam para `.exe` não ganham indicador.

## Widgets

Um widget herda de `DockWidget`, desenha num `Grid` de 64×64 unidades de design (o `Viewbox` do Dock
escala junto com a ampliação) e pode sobrescrever:

| Membro | Uso |
|---|---|
| `Build()` | Monta o desenho uma vez |
| `Interval` / `UpdateAsync()` | Atualização periódica (o Dock chama no timer lento) |
| `Label` | Texto do rótulo ao passar o mouse |
| `OnClick`, `MenuItems()` | Clique e itens do menu de botão direito |
| `AcceptsFiles`, `OnFilesDropped` | Receber arquivos arrastados (Lixeira) |
| `IsAvailable`, `AvailabilityChanged` | Esconder quando não se aplica (Bateria em desktop) |
| `OnThemeChanged`, `Dispose` | Tema e limpeza |

Para criar um widget novo: crie a classe em `Widgets/`, registre o nome em `WidgetFactory.Create`,
`DisplayName`, `Description` e `Glyph`, e acrescente o nome em `SettingsStore.AllWidgetKinds`.

## Atalhos globais e busca

`Core/ShortcutPlan` lê as configurações e monta a lista de atalhos a registrar, cada um com um id fixo: foco
(`KeyboardShortcut`), mostrar/ocultar (`ToggleShortcut`), busca (`SearchShortcut`) e itens 1 a 9
(`ItemShortcutModifiers` + número). Um atalho repetido fica só na primeira função e vai para `Duplicates`.
`DockWindow.RegisterShortcut` registra tudo com `RegisterHotKey` (só quando a `Signature` muda) e junta os que
falharam em `ShortcutError`, que o Gerenciar Dock mostra. `WM_HOTKEY` chama `OnShortcut`. Mostrar/ocultar inverte
`AutoHide`. O item N abre o N-ésimo app fixado sem olhar Ctrl/Shift, que fazem parte do próprio atalho.
`Core/Hotkey.Format` monta o texto a partir das teclas apertadas no editor (`Views/SettingsWindow.Shortcuts.cs`).

`Views/SearchWindow` é a busca: janela sem borda no monitor do mouse. Os itens do Dock são lidos na hora. Os apps
(`StartMenuApps`) e os arquivos (Recentes, Área de Trabalho, Documentos e Downloads, até 500 por pasta, só o
primeiro nível) ficam em cache por 2 min e carregam numa `Task`. `Core/SearchRanker` dá pontos sem acentos e sem
maiúsculas: igual > começo > começo de palavra > iniciais > contém > letras em ordem. Desempata por Dock, app,
arquivo e nome mais curto, e junta itens repetidos. Abrir passa por `AppLauncher.Launch`, com as mesmas
confirmações do Dock.

## Desfoque

`Views/BlurBackdrop` é uma janela vazia, sempre logo abaixo do Dock e do tamanho do fundo, com
`SetWindowCompositionAttribute(ACCENT_ENABLE_BLURBEHIND)` e uma região de cantos arredondados (`SetWindowRgn`).
O Dock continua com a janela transparente e desenha a cor e a borda por cima (com a cor 30% mais transparente
enquanto o desfoque está ativo). `DockWindow.PlaceBlur` reposiciona o desfoque a cada `Layout` e no deslize do
auto-ocultar; `UpdateBlur` liga/desliga conforme a opção, "Efeitos de transparência", economia de bateria e alto
contraste (conferidos também no timer lento).

## Miniaturas das janelas

`Views/WindowPreview` é uma janela sem foco (`WS_EX_NOACTIVATE`, `MA_NOACTIVATE`) com um cartão por janela do app:
título, botão de fechar e uma caixa vazia. O próprio Windows desenha a miniatura ao vivo sobre essa caixa
(`DwmRegisterThumbnail` + `DwmUpdateThumbnailProperties`, em pixels físicos, mantendo a proporção da janela).
A janela não é transparente, porque miniaturas do DWM não aparecem em janelas em camadas (`AllowsTransparency`);
os cantos arredondados vêm de `SetWindowRgn`, e as cores vêm do rótulo do Dock (sem alfa). Janelas minimizadas
mostram o ícone do app, porque o Windows não tem a imagem atual delas.

`DockWindow.Previews.cs` decide quando abrir: a cada frame (e por um timer de 50 ms enquanto o painel está
aberto, já que o mouse no painel faz o Dock parar de animar) olha o ícone sob o mouse. Depois de
`Appearance.PreviewDelay` no mesmo app com pelo menos `PreviewLayout.MinWindows` janelas, abre; outro app troca
na hora; fora do ícone e do painel, fecha depois de 0,35 s. O ponto de apoio é o centro do ícone na altura máxima
da ampliação, onde o Dock deixa de segurar o mouse, para não haver vão no caminho. `Core/PreviewLayout` calcula
quantas miniaturas cabem na tela (encolhe até 60% antes de esconder as que sobram) e o encaixe de cada uma, sem
chamadas ao Windows, para os testes.

## Ícones personalizados

`DockEntry.Icon` guarda só o nome de um PNG em `%APPDATA%\Dockbox\icons`. `Services/CustomIcons` importa
imagens (`BitmapDecoder`, maior quadro de um .ico) ou um ícone de .exe/.dll (`PickIconDlg` para escolher e
`PrivateExtractIcons` em 256 px), reduz com `BitmapScalingMode.HighQuality` até 256 px e grava com nome novo
(GUID). O Dock e o Gerenciar Dock usam o ícone personalizado antes do ícone do app. Restaurar apaga a cópia se
nenhum outro item a usa.

## Temas e backup

`Core/ThemeFiles` lê e grava dois arquivos JSON com `format` e `version` no topo: `.dockbox-theme` (só
`ThemeData`: cores, opacidade, desfoque, cantos, tamanho e ampliação) e `.dockbox-backup` (o `DockSettings`
inteiro mais os PNGs de `icons` em base64). Tudo é lido e validado antes de aplicar: JSON quebrado, formato
errado, versão mais nova ou valores que não convertem viram `ThemeFileException` com a mensagem para o usuário;
valores fora do intervalo passam pelo mesmo `SettingsStore.Normalize` das configurações. No backup, `Trusted` volta
a `false` em todos os itens (scripts e links pedem confirmação de novo), nomes de ícone viram só o nome do arquivo
(`.png`) e ícones citados mas ausentes voltam ao do app. Os temas prontos ficam em `ThemeFiles.BuiltIn` e não mudam
tamanho nem ampliação. A interface está em `Views/SettingsWindow.Themes.cs`: importar tema aplica, salva e pergunta
se mantém (desfaz com os valores de antes); importar backup grava os ícones primeiro e só então troca as
configurações.

## Pilhas de pastas

Clicar num item fixado que é uma pasta abre `Views/StackWindow` (em vez do Explorador). É uma janela WPF
transparente, ativada (para Esc e teclado) e fechada ao perder o foco. `Core/StackContent` lista a pasta (sem
ocultos e arquivos de sistema), ordena, escolhe o jeito de mostrar (leque só com o Dock embaixo e até 10 itens)
e decide mover ou copiar ao soltar. Os ícones carregam aos poucos com prioridade baixa no Dispatcher, para a
pilha abrir na hora; mostra até 400 itens. Um `FileSystemWatcher` recarrega o conteúdo (com 300 ms de espera).
Abrir um item passa pelo `AppLauncher` (mesmas confirmações dos itens fixados). Arrastar usa
`DragDrop.DoDragDrop` com `FileDrop`.

As opções ficam no próprio `DockEntry` (`StackView`, `StackSort`, `StackShowsLatest`). Com
`StackShowsLatest`, o tile mostra o ícone do item mais recente e `DockWindow.Stacks.cs` acompanha a pasta com
um `FileSystemWatcher` para trocar o ícone. Soltar arquivos numa pasta do Dock usa
`Microsoft.VisualBasic.FileIO.FileSystem` com as janelas de progresso e conflito do Windows.

## Animação ao minimizar

`DockWindow.Minimize.cs` registra `SetWinEventHook(EVENT_SYSTEM_MINIMIZESTART)` (fora do processo, sem as
janelas do próprio Dockbox). Para cada janela que minimiza, calcula de onde ela sai (o retângulo atual ou, se
já minimizou, `GetWindowPlacement` convertido de coordenadas da área de trabalho) e para onde vai (o ícone do
app no Dock, pelas chaves de `RunningApps`; sem ícone, o meio do Dock). `Views/MinimizeEffectWindow` é uma
janela temporária sem foco e sem cliques coberta pela miniatura ao vivo da janela (`DwmRegisterThumbnail`),
redimensionada a cada frame conforme `Core/MinimizeMath` (Escala: tudo junto com aceleração; Gênio: primeiro o
eixo ao longo do Dock, depois o eixo até o Dock).

Para a animação do Windows não aparecer junto, `Services/SystemMinimizeAnimation` desliga `MinAnimate`
(`SPI_SETANIMATION`, sem gravar no registro) enquanto o efeito está ligado, com um arquivo de recuperação
como o da barra de tarefas: religa ao escolher Nenhum, ao fechar e, depois de uma queda, na próxima abertura.
## Pedidos de atenção e contadores

O Dock se registra com `RegisterShellHookWindow` e recebe a mensagem `SHELLHOOK`. `HSHELL_FLASH` diz que uma
janela piscou (FlashWindowEx); a janela principal dela (`GetAncestor(GA_ROOTOWNER)`) entra em
`Core/AttentionTracker`, que tira as janelas que fecharam e as do app que foi para o primeiro plano
(conferido no timer lento e quando chega `HSHELL_WINDOWACTIVATED`/`DESTROYED`). Ícones com atenção pulam de
novo a cada segundo (o pulo dura 1,4 s), menos com animações desligadas no Windows.

Os contadores vêm do título das janelas (`Core/Badges.ParseCount`: "(N) Nome" no começo do título), lidos no
timer lento. O contador e o progresso que os apps mandam com `ITaskbarList3` vão direto para a janela da barra
de tarefas do Explorer; não há API para outro programa ler isso sem substituir o Explorer como shell ou
injetar código nele, por isso ficaram de fora.

## Barra de tarefas do Windows

`Services/Taskbar` esconde `Shell_TrayWnd` e `Shell_SecondaryTrayWnd` (`ShowWindow`) e liga a ocultação
automática da barra (`ABM_SETSTATE`) para liberar a área de trabalho. Antes, grava o estado original em
`barra-de-tarefas-oculta.txt` na pasta de configurações; `Restore` volta esse estado e apaga o arquivo. O `App`
restaura na saída (`OnExit`, `ProcessExit`, exceção não tratada) e, na abertura, se o arquivo ainda existir
(`RecoverAfterCrash`). O Dock esconde de novo quando o Explorer reinicia (`TaskbarCreated`) e no timer lento, se a
barra reapareceu e a janela em foco não é o Iniciar ou a pesquisa.

## Idiomas

Os textos ficam em `src/Dockbox/Strings.resx` (inglês, usado quando falta tradução), `Strings.pt.resx` e
`Strings.es.resx`, gerados por `tools/i18n/build_resx.py` a partir de `tools/i18n/strings.py` (não edite os `.resx`
à mão). No código, `Loc.T("Chave")` e `Loc.F("Chave", valores)`; no XAML, `Text="{l:T Chave}"` com
`xmlns:l="clr-namespace:Dockbox.Views"`.

`Loc.Apply` (chamado pelo `App` ao abrir e quando `DockSettings.Language` muda) escolhe a cultura com `Loc.Resolve`
(automático = idioma do Windows, mantendo o país para datas) e dispara `Loc.Changed`: a bandeja refaz o menu, o Dock
pede rótulos novos aos widgets e o Gerenciar Dock reabre. Menus do Dock são criados a cada clique, então já saem no
idioma novo. Os testes em `LocTests` conferem que toda chave existe nas três línguas, com os mesmos `{0}`, e que toda
chave usada no código existe. Como acrescentar um idioma: [TRADUCAO.md](TRADUCAO.md).

## Persistência

`%APPDATA%\Dockbox\settings.json`, via `System.Text.Json` (sem tipos polimórficos, então sem risco de
desserialização de tipos arbitrários).

- **Leitura**: JSON inválido (inclusive enum desconhecido ou numérico) gera uma cópia
  `settings.json.corrompido-<data>`, preenche `LoadWarning` (mostrado pelo `App`) e volta aos padrões.
  Sem `settings.json`, importa o `pinned.json` do MVP, ou usa apps padrão que existam na máquina.
- **Normalização** (`Normalize`): remove itens sem caminho, limpa widgets desconhecidos/duplicados,
  adiciona os que faltam, limita tamanhos/opacidade e valida cores.
- **Gravação**: escreve em `.tmp` e troca com `File.Replace`, guardando `.bak`. Erros de escrita são ignorados.
- **Testes**: `SettingsStore.UseFolder(pasta)` redireciona para outra pasta.

## Rede

Só o widget de Clima usa a internet, sempre por HTTPS e com timeout de 10 s:

| Serviço | Quando | Envia |
|---|---|---|
| `ipwho.is` | Cidade vazia **e** `WeatherAutoLocate` ligado (desligado por padrão) | O IP do usuário (implícito na conexão) |
| `geocoding-api.open-meteo.com` | Cidade preenchida | Nome da cidade |
| `api.open-meteo.com` | Sempre | Latitude/longitude |

Atualiza a cada 15 min (1 min enquanto não houver dado).

## Build e CI

- `dotnet run --project src/Dockbox` para desenvolver.
- `.github/workflows/build.yml` (windows-latest): roda os testes e publica um `Dockbox.exe` único,
  self-contained, como artefato.
- Para compilar no Linux é preciso o SDK da Microsoft com `Microsoft.NET.Sdk.WindowsDesktop`;
  os testes que usam WPF só rodam no Windows.
