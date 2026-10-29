"""Gera os arquivos Strings*.resx do Dockbox a partir de tools/i18n/strings.py."""
import os, sys
from xml.sax.saxutils import escape

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from strings import S  # noqa: E402

OUT = os.path.join(HERE, "..", "..", "src", "Dockbox")
HEADER = """<?xml version="1.0" encoding="utf-8"?>
<!-- Gerado por tools/i18n/build_resx.py a partir de tools/i18n/strings.py. Não edite à mão. -->
<root>
  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
  <resheader name="version"><value>2.0</value></resheader>
  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
"""

keys = [k for k, *_ in S]
dupes = {k for k in keys if keys.count(k) > 1}
if dupes:
    sys.exit(f"Chaves repetidas: {sorted(dupes)}")

for suffix, idx in (("", 2), (".pt", 1), (".es", 3)):
    lines = [HEADER]
    for row in S:
        lines.append(f'  <data name="{row[0]}" xml:space="preserve"><value>{escape(row[idx])}</value></data>\n')
    lines.append("</root>\n")
    with open(os.path.join(OUT, f"Strings{suffix}.resx"), "w", encoding="utf-8", newline="\n") as f:
        f.write("".join(lines))
print(f"{len(S)} textos em 3 idiomas.")
