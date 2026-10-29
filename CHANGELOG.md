# Changelog

Todas as mudanças relevantes do Dockbox ficam registradas aqui.

O formato segue o [Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/)
e o projeto usa [Versionamento Semântico](https://semver.org/lang/pt-BR/).
Enquanto a versão for `0.x`, qualquer versão nova pode mudar o comportamento.

## [Não lançado]

### Adicionado

- Visual estilo macOS: fundo de vidro com gradiente, borda clara e sombra; rótulo com o nome do item.
- Tema automático, claro, escuro ou personalizado; cor de fundo, cor de destaque, opacidade e cantos configuráveis.
- Posição na tela: embaixo, em cima, à esquerda ou à direita.
- Tamanho dos ícones e intensidade da ampliação configuráveis; ampliação pode ser desligada.
- Ponto sob apps abertos; clique traz a janela para frente ou minimiza; Ctrl + clique abre nova janela.
- Animação de pulo ao abrir um app.
- Janela "Gerenciar Dock": adicionar apps do Menu Iniciar, arquivos e pastas; remover, reordenar e editar.
- Widgets: relógio, calendário, clima, CPU/RAM, bateria, mídia tocando agora e Lixeira.
- Soltar arquivos sobre um app abre os arquivos com ele; soltar na Lixeira apaga.
- Opção de iniciar junto com o Windows.
- Reservar espaço na tela: as janelas maximizadas param antes do Dock em qualquer borda; opção em Comportamento (#4).
- Ocultar automaticamente: o Dock sai da tela e volta ao encostar o mouse na borda, com tempo configurável (#5).
- Identidade visual: logo e ícone próprios no `Dockbox.exe`, nas janelas, na bandeja e no README (#40).
- Ícone na área de notificação com atalhos para Gerenciar Dock e Sair (#40).
- Reordenar arrastando um ícone no Dock e remover arrastando para fora, com indicador de onde o item entra, também ao arrastar arquivos de fora (#6).
- Menu próprio de cada app: janelas abertas (clique traz a janela), Nova janela, Mostrar no Explorador, Fechar/Fechar todas, Remover do Dock e submenu Opções do Dock; menus funcionam pelo teclado (#7).
- Apps abertos que não estão fixados aparecem depois dos fixados, com "Manter no Dock"; apps da Microsoft Store ganham o ponto de aberto e podem ser adicionados pelo Gerenciar Dock (#8).
- Desfazer remoção: aviso "Desfazer" no Dock por 6 segundos e Ctrl+Z no Gerenciar Dock; fixar algo que já está no Dock não duplica, o item existente é destacado (#9).
- Aviso quando não dá para salvar as configurações (somente leitura, disco cheio, arquivo em uso), com o motivo, o caminho e "Tentar salvar de novo"; antes de avisar, tenta de novo algumas vezes (#10).
- Vários monitores: o Dock fica no principal, num monitor escolhido ou segue o mouse até a borda de outro monitor; reage a monitor conectado/desconectado e a mudança de resolução ou escala sem reiniciar (#19).
- Ícones nítidos em telas com escala (125%, 150%, 200%): o Gerenciar Dock e a lista de apps pedem o ícone no tamanho certo para a maior escala entre os monitores; quando o Windows não dá o ícone grande, o Dockbox usa o de 48 px em vez do de 32 px (#20).
- Acessibilidade: atalho global (Win+Shift+D, configurável ou desligado) para usar o Dock pelo teclado, com setas, Enter, Shift+F10 e Esc; nomes para leitores de tela em cada item e widget e nos controles do Gerenciar Dock; sem ampliação, pulo nem deslizes com "Mostrar animações" desligado; cores do Windows no alto contraste (#25).
- Idiomas: português, inglês e espanhol, escolhidos em Gerenciar Dock › Comportamento › Idioma; no automático segue o idioma do Windows (outros idiomas ficam em inglês). Datas do relógio e do calendário e o nome da cidade do clima seguem o idioma (#34).

### Segurança

- Scripts, protocolos desconhecidos e interpretadores com argumentos pedem confirmação antes de abrir (uma vez por item); protocolos usados em ataques, como `ms-msdt:` e `search-ms:`, são bloqueados (#13).
- Iniciar com o Windows a partir de Downloads, Temp ou Área de Trabalho oferece copiar o Dockbox para `%LOCALAPPDATA%\Programs\Dockbox`; se o exe foi movido, o início automático é corrigido ao abrir (#14).
- Resposta do serviço de clima limitada a 1 MB e 10 s; `erros.log` com data, hora e versão em cada registro e rotação para `erros.1.log` ao passar de 1 MB (#15).
- GitHub Actions fixadas por hash do commit e Dependabot semanal para Actions e NuGet; o build não guarda mais a credencial do checkout (#17).

### Alterado

- Configurações agora ficam em `%APPDATA%\Dockbox\settings.json`; o `pinned.json` antigo é importado.
- Gravação atômica das configurações; um arquivo corrompido é copiado com data antes de voltar aos padrões.
- Requer Windows 10 versão 1809 ou mais recente.
- Item padrão "Terminal" abre o Windows Terminal quando instalado (senão PowerShell, por último cmd) (#11).
- Setas de reordenar no Gerenciar Dock falam "antes/depois" conforme a posição do Dock (#11).
- Mudar cor, opacidade ou cantos não recarrega mais os ícones no Gerenciar Dock nem recria os ícones do Dock (#11).

## [0.1.0] - 2026-10-29

Primeira versão (Fase 1, MVP).

### Adicionado

- Barra fixa na parte inferior da tela, sem borda, transparente e sempre no topo.
- A janela não rouba o foco e não aparece no Alt+Tab nem na barra de tarefas.
- Ícones de apps fixados; um clique abre o app.
- Ampliação suave ao passar o mouse, com o nome do app acima do ícone.
- Arrastar um `.exe`, atalho `.lnk` ou pasta para o Dock para fixá-lo.
- Menu do botão direito: editar apps fixados, recarregar e sair.
- Apps fixados salvos em `%APPDATA%\Dockbox\pinned.json`.

[Não lançado]: https://github.com/victorgabriel08fs/dockbox/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/victorgabriel08fs/dockbox/releases/tag/v0.1.0
