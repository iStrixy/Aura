# Aura

Widgets leves na área de trabalho do Windows 10 (relógio, CPU, RAM, temperatura da GPU, rede) e um monitor de rede
que **explica** cada conexão, DNS e aparelho da casa: o que é, de quem é, se é suspeito ou não.

- WPF / .NET Framework 4.8, sem dependências e sem instalador: ~0,3% de um núcleo e ~75 MB de RAM só com o widget.
- Aba Rede: conexões por aplicativo, domínios, dispositivos da rede, consumo por dia.
- Opcional: leitura (só leitura) do painel de roteadores Nokia GPON, DNS da casa com bloqueio de anúncios, histórico do NextDNS.
  Senhas e chaves ficam só no PC, protegidas pelo Windows (DPAPI).

## Compilar

Não precisa de Visual Studio nem SDK: usa o `csc.exe` que já vem no Windows.

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
.\Aura.exe --selftest   # código de saída = número de falhas
```

`Aura.exe --rede` abre direto o monitor de rede.

## Licença

MIT — veja [LICENSE](LICENSE).
