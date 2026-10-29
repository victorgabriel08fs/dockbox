# Identidade visual do Dockbox

<p align="center"><img src="png/dockbox-256.png" width="128" alt="Ícone do Dockbox"></p>

O símbolo é um Dock visto de frente: uma prateleira de vidro com três ícones, o do meio
ampliado como acontece no macOS quando o mouse passa por cima, e o ponto que indica app aberto.

## Arquivos

| Arquivo | Uso |
|---|---|
| `dockbox-icon.svg` | Ícone completo (fonte principal). Usado de 40 px para cima. |
| `dockbox-icon-small.svg` | Versão simplificada, alinhada à grade de 16 px. Usada de 16 a 32 px (bandeja, Explorador). |
| `dockbox-mark.svg` | Só o símbolo, sem fundo, numa cor. Para fundos claros; troque o `fill` por branco em fundos escuros. |
| `Dockbox.ico` | Ícone do Windows com 16, 20, 24, 32, 40, 48, 64, 128 e 256 px. |
| `png/` | PNG de 64 a 1024 px para README, site e redes. |

O app usa as cópias em `src/Dockbox/Assets/`. Não edite os PNG nem o `.ico` à mão:
mude o SVG e rode o script.

## Regerar os ícones

Precisa do Node.js. Na raiz do repositório:

```powershell
npm install --no-save playwright
npx playwright install chromium
node tools/build-icons.js
```

O script atualiza `assets/brand/Dockbox.ico`, `assets/brand/png/` e `src/Dockbox/Assets/`.

## Cores

| Nome | Hex | Uso |
|---|---|---|
| Azul claro | `#3D9BFF` | Início do gradiente do ícone |
| Azul Dockbox | `#2F6BF5` | Cor principal, símbolo e links |
| Violeta | `#5B3FD9` | Fim do gradiente do ícone |
| Azul destaque | `#0A84FF` | Cor de destaque padrão da interface (igual ao macOS) |
| Branco gelo | `#E4ECFF` | Ícones dentro do símbolo |

Gradiente do ícone: diagonal, do canto superior esquerdo (`#3D9BFF`) passando por `#2F6BF5`
(55%) até o inferior direito (`#5B3FD9`).

## Regras de uso

- Deixe uma margem livre de pelo menos 1/8 da largura do ícone em volta dele.
- Não distorça, gire nem troque as cores do gradiente.
- Abaixo de 40 px use sempre a versão simplificada (o `.ico` já faz isso).
- O nome se escreve **Dockbox**, com D maiúsculo e o resto minúsculo.
