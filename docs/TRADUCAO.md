# Traduções do Dockbox

O Dockbox está em **português**, **inglês** e **espanhol**. O inglês é o idioma de reserva: um texto sem tradução
aparece em inglês, e um Windows em outro idioma (francês, alemão...) mostra o Dockbox em inglês.

## Onde ficam os textos

Todos os textos estão numa tabela só, [`tools/i18n/strings.py`](../tools/i18n/strings.py):

```python
("Menu_Quit", "Sair do Dockbox", "Quit Dockbox", "Salir de Dockbox"),
#  chave       português          inglês          espanhol
```

Os arquivos `src/Dockbox/Strings.resx` (inglês), `Strings.pt.resx` e `Strings.es.resx` são gerados dessa tabela.
Não edite os `.resx` à mão.

## Corrigir ou acrescentar um texto

1. Edite a linha em `tools/i18n/strings.py` (ou acrescente uma linha com chave nova).
2. Rode `python3 tools/i18n/build_resx.py`.
3. No código use `Loc.T("Chave")`, ou `Loc.F("Chave", valor)` para textos com `{0}`. No XAML use `Text="{l:T Chave}"`.
4. Rode os testes: `LocTests` falha se faltar uma chave em algum idioma ou se os `{0}`, `{1}` não forem os mesmos.

Dicas:
- Mantenha `{0}`, `{1}`... em todas as línguas; a ordem pode mudar dentro da frase.
- `\n` é quebra de linha.
- Siga os nomes do Windows em cada idioma: "Lixeira" / "Recycle Bin" / "Papelera de reciclaje",
  "Explorador de Arquivos" / "File Explorer" / "Explorador de archivos".

## Acrescentar um idioma

1. Em `tools/i18n/strings.py`, acrescente uma coluna em cada linha.
2. Em `tools/i18n/build_resx.py`, acrescente o sufixo e o número da coluna (ex.: `(".fr", 4)`).
3. Em `src/Dockbox/Core/Loc.cs`: acrescente o idioma em `Languages` (código e nome no próprio idioma) e em
   `Normalize` e `Resolve`.
4. Em `tests/Dockbox.Tests/LocTests.cs`, acrescente o idioma no teste das chaves.
5. Atualize o CHANGELOG e este arquivo.
