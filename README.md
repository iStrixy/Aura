<p align="center"><img src="assets/banner.svg" alt="Aura" width="100%"></p>

<p align="center">
  <a href="https://github.com/iStrixy/Aura/releases/latest"><img src="https://img.shields.io/github/v/release/iStrixy/Aura?label=baixar&color=2E9BD6" alt="Baixar"></a>
  <img src="https://img.shields.io/badge/Windows-10-8A4FD1" alt="Windows 10">
  <img src="https://img.shields.io/badge/.NET%20Framework-4.8-5C6BC0" alt=".NET Framework 4.8">
  <a href="LICENSE"><img src="https://img.shields.io/badge/licen%C3%A7a-MIT-30D158" alt="Licença MIT"></a>
</p>

Widgets leves na área de trabalho do Windows (relógio, CPU, RAM, temperatura da GPU, rede) e um monitor de rede
que **explica** cada conexão, DNS e aparelho da casa: o que é, de quem é, se é suspeito ou não. Tudo em português e sem jargão.

## ✨ O que faz

| | |
|---|---|
| 🖥️ **Widget** | Relógio, CPU, RAM, temperatura da GPU (AMD) e velocidade da internet, fixo na área de trabalho. Para de redesenhar durante jogo em tela cheia. |
| 🌐 **Conexões** | Cada programa que está usando a internet, para onde vai (Google, Riot, Discord…) e se é normal, rastreador ou suspeito. |
| 🔎 **Domínios, portas e DNS** | Quais sites o PC consultou, quais portas estão abertas para a rede e se o seu DNS é privado. |
| 📱 **Dispositivos** | Os aparelhos ligados na sua rede, com o nome que o roteador conhece. |
| 📡 **Roteador** | Sinal da fibra, IP de internet (avisa CGNAT), temperatura do roteador, sinal e consumo de cada aparelho no Wi-Fi. |
| 🛡️ **Sites da casa** | Opcional: DNS da casa com bloqueio de anúncios e rastreadores, ou histórico do NextDNS por aparelho. |

Leve de verdade: **~0,3% de um núcleo e ~75 MB de RAM** só com o widget. A coleta detalhada só roda com a janela de rede aberta.

## ⬇️ Instalar

1. Baixe o `Aura.exe` em [**Releases**](https://github.com/iStrixy/Aura/releases/latest).
2. Abra. Não tem instalador: o .NET Framework 4.8 já vem no Windows 10. Feito e testado no Windows 10 22H2.
3. O widget aparece na área de trabalho. **Clique duplo** nele (ou no ícone da bandeja) para abrir o monitor de rede.
   Clique com o botão direito no widget para as opções (iniciar com o Windows, vidro no Explorador, DNS da casa…).

> [!NOTE]
> O exe ainda não tem assinatura digital, então o Windows mostra "O Windows protegeu o computador".
> Clique em **Mais informações → Executar assim mesmo**, ou compile você mesmo (veja abaixo).

## 📡 Roteadores

O Aura acha o roteador sozinho: ele é o **gateway padrão** da sua rede (o mesmo que aparece no `ipconfig`, geralmente
`192.168.0.1`, `192.168.1.1` ou `10.0.0.1`). Depois ele abre a página inicial do roteador, **sem login**, e reconhece a marca.

| Marca | Reconhece | Lê o painel com login |
|---|:---:|:---:|
| **Nokia** (ONTs GPON de fibra, ex.: G-1425G-A) | ✅ | ✅ |
| ZTE, Huawei, TP-Link, Mercusys, Intelbras, Fiberhome, D-Link, Tenda, MikroTik, ASUS, Xiaomi, Sagemcom, Askey, Multilaser | ✅ | ⏳ ainda não |

Cada fabricante tem um painel diferente, então a leitura com login é feita marca por marca. Nas marcas sem leitura, o Aura
não pede senha e continua mostrando o monitor do PC e a lista de aparelhos da rede.

### Nível do usuário

O que aparece depende de **com qual usuário você entra**, porque o próprio roteador esconde páginas de quem tem menos permissão:

| Usuário | Onde acha | O que costuma ver |
|---|---|---|
| Comum (`user`, `userAdmin`…) | Etiqueta embaixo do roteador | Status básico: internet e aparelhos. Às vezes sem sinal da fibra. |
| Administrador | Etiqueta ou manual (ou você trocou) | Quase tudo |
| Suporte do provedor | Só o provedor tem | Tudo |

Quando falta alguma coisa, a aba Roteador mostra **"Seu usuário não vê tudo"** e lista exatamente o que ficou bloqueado.
Não é erro: é o roteador protegendo as páginas.

**Segurança:** o Aura **só lê**. Ele usa uma lista fixa de páginas de status e nunca abre páginas de configuração, reiniciar ou sair.
A senha fica só no seu PC, protegida pelo Windows (DPAPI, só o seu usuário do Windows consegue abrir). Se o roteador
recusar o login, o Aura **não tenta de novo sozinho**, para não bloquear o painel.

## 🛡️ NextDNS (opcional)

Quer ver **quais sites cada aparelho da casa acessa** (celular, TV), mesmo com o PC desligado? O [NextDNS](https://nextdns.io)
é um DNS na nuvem que guarda esse histórico, e o Aura mostra o histórico na aba **Sites da casa**. Tem plano gratuito.
Se você não quiser usar, é só ignorar: o resto do Aura funciona igual.

1. Crie uma conta em [my.nextdns.io](https://my.nextdns.io). Ela já cria um **perfil**.
2. Na aba **Setup** do perfil, copie o **ID** (6 letras e números, ex.: `abc123`).
3. Em [my.nextdns.io/account](https://my.nextdns.io/account), na parte **API**, gere e copie a **chave da API**.
4. No Aura, abra **Sites da casa**, cole o ID e a chave e clique em **Ligar**.
5. Coloque cada aparelho para usar o NextDNS (as instruções completas estão na aba **Setup** do NextDNS):
   - **iPhone/iPad:** abra `apple.nextdns.io` no Safari e instale o perfil.
   - **Android:** Configurações → Rede → **DNS privado** → `abc123.dns.nextdns.io` (com o seu ID).
   - **Windows:** use o app oficial do NextDNS ou o DoH do Windows 11.

> [!TIP]
> A opção "IP vinculado" do NextDNS não funciona bem se o seu provedor usa **CGNAT** (IP 100.64 a 100.127; a aba Roteador avisa).
> Nesse caso use o perfil no iPhone ou o DNS privado no Android.

A chave da API fica no PC protegida pelo Windows, igual à senha do roteador. O Aura só **lê** o histórico.

## 🔒 Privacidade

- Nada sai do seu PC, exceto as consultas que você ligar: o roteador (rede local) e a API do NextDNS.
- Sem telemetria, sem conta, sem anúncios.
- Os dados ficam em `%LOCALAPPDATA%\Aura\` (consumo por dia, configurações, senhas cifradas).

## 🛠️ Compilar

Não precisa de Visual Studio nem SDK: usa o `csc.exe` que já vem no Windows.

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
.\Aura.exe --selftest   # código de saída = número de falhas
```

| Comando | Faz |
|---|---|
| `Aura.exe --rede` | Abre direto o monitor de rede |
| `Aura.exe --roteador` | Lê o roteador sem abrir janela e grava em `%LOCALAPPDATA%\Aura\roteador\_linhas.txt` |

Quer ensinar ao Aura um site, porta ou programa novo? É tudo em [`src/Kb.cs`](src/Kb.cs).
Quer adicionar a leitura do painel de outra marca de roteador? Veja [`src/Router.cs`](src/Router.cs). Pull requests são bem-vindos.

## 📄 Licença

MIT. Veja [LICENSE](LICENSE).
