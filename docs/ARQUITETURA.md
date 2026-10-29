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
| `Views/` | `DockWindow`, `SettingsWindow`, `AppPickerWindow` | Sim |
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

## Desfoque

`Views/BlurBackdrop` é uma janela vazia, sempre logo abaixo do Dock e do tamanho do fundo, com
`SetWindowCompositionAttribute(ACCENT_ENABLE_BLURBEHIND)` e uma região de cantos arredondados (`SetWindowRgn`).
O Dock continua com a janela transparente e desenha a cor e a borda por cima (com a cor 30% mais transparente
enquanto o desfoque está ativo). `DockWindow.PlaceBlur` reposiciona o desfoque a cada `Layout` e no deslize do
auto-ocultar; `UpdateBlur` liga/desliga conforme a opção, "Efeitos de transparência", economia de bateria e alto
contraste (conferidos também no timer lento).

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
