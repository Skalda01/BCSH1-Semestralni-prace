# SkalaView

SkalaView je multiplatformni aplikace pro sledovani kryptomenovych trhu. Projekt kombinuje sdilene Avalonia UI, desktopovou aplikaci, browser/WASM variantu a React/Vite webovy host.

Aplikace je zamerena na prakticky trading dashboard: dynamicky watchlist, live trzni data, svickovy graf, indikatorovy panel a order book pro aktualne vybrany symbol.

## Klicove funkce

| Funkce | Popis |
| --- | --- |
| Dynamicky watchlist | Uzivatel se pripoji pomoci API tokenu a aplikace nacte jeho serverovy watchlist. Tickery lze pridavat a odebirat primo z aplikace. |
| Overeni API tokenu | Token se posila na backend jako `Bearer` token. Po uspesnem overeni backend vrati watchlist navazany na dany token. |
| Live ticker prehled | Watchlist se pravidelne aktualizuje o cenu, denni zmenu, low/high, objem a quote volume z Binance API. |
| Svickovy graf | Graf nacita OHLCV data z Binance klines endpointu a reaguje na zmenu tickeru i timeframe. |
| Indikatorovy panel | Live statistiky pro aktivni symbol prichazi pres Binance WebSocket ticker stream. |
| Order book | Hloubka trhu se nacita pres Binance WebSocket depth stream. |
| Sdilene UI | Jedno Avalonia UI jadro se pouziva pro desktopovou i browser variantu. |

## Architektura projektu

```text
SkalaView.sln
|-- SkalaView/              Sdilene Avalonia UI, viewmodely a API servisy
|-- SkalaView.Desktop/      Desktopovy vstupni projekt
|-- SkalaView.Browser/      Browser/WASM vstupni projekt
|-- ReactWeb/               React + Vite host pro browser verzi
`-- global.json             Konfigurace .NET SDK
```

### Sdilena aplikacni vrstva

Projekt `SkalaView` obsahuje hlavni UI, viewmodely a komunikaci s externimi API. Sdileny stav aplikace drzi `SharedViewModel`, hlavne vybrany ticker a timeframe. Diky tomu se po zmene tickeru synchronne prepina graf, indikatory i order book.

Dulezite casti:

- `TickerMenuViewModel` resi API token, serverovy watchlist a pravidelny refresh ticker snapshotu.
- `CandlestickChartViewModel` nacita svickova data pro aktualni symbol a timeframe.
- `IndicatorMenuViewModel` spravuje live ticker stream.
- `OrderBookViewModel` spravuje live order book stream.
- `UserWatchlistApiService` komunikuje s backendem pro token a watchlist.
- `Binance*ApiService` tridy oddeluji praci s Binance REST a WebSocket API od UI logiky.

## API token a watchlist

Watchlist neni pevne zapsany v klientovi. Po spusteni aplikace se zobrazi vychozi tickery, ale po zadani API tokenu aplikace zavola backend a nacte watchlist uzivatele.

Klient pouziva backend endpointy:

```text
POST   /api/app/validate-token
POST   /api/app/watchlist
DELETE /api/app/watchlist/{symbol}
```

Token se posila v HTTP hlavicce:

```http
Authorization: Bearer <api-token>
```

Aktualni base URL je nastavene v `SkalaView/apiService/UserWatchlistApiService.cs`:

```text
https://skalicky-test.cz/backend/api/app
```

Tok v aplikaci:

1. Uzivatel zada API token.
2. Klient zavola `validate-token`.
3. Backend overi token a vrati watchlist.
4. Aplikace nahradi lokalni seznam tickeru serverovym watchlistem.
5. Pridani nebo odebrani tickeru se posila na backend.
6. Backend vraci aktualizovany watchlist, ktery se znovu propise do UI.

Tento pristup drzi watchlist per-user/per-token na serveru a klient zustava bez persistentni lokalni databaze watchlistu.

## Trzni data

| Cast aplikace | Zdroj dat | Detail |
| --- | --- | --- |
| Watchlist ticker data | Binance REST API | Aktualni cena, zmena, low/high, volume a quote volume. |
| Svickovy graf | Binance REST API `/api/v3/klines` | Poslednich 300 svicek pro vybrany symbol a timeframe. |
| Indikatory | Binance WebSocket `@ticker` | Live statistiky aktivniho symbolu. |
| Order book | Binance WebSocket `@depth20@100ms` | Live bid/ask hloubka trhu. |

Symboly se normalizuji na Binance format. Pokud uzivatel zada napr. `BTC`, API servis pracuje s parem `BTCUSDT`.

## Technologie

- .NET 8
- Avalonia UI 11
- LiveChartsCore
- React 19
- Vite 7
- TypeScript
- Binance REST API
- Binance WebSocket API

## Pozadavky

- .NET SDK 8.0
- Node.js a npm
- PowerShell pro build script webove Avalonia casti
- Visual Studio 2022 nebo JetBrains Rider pro pohodlny vyvoj

Projekt obsahuje `global.json` s .NET SDK `8.0.124` a `rollForward` nastavenym na `latestFeature`.

## Instalace

```powershell
git clone https://github.com/Skalda01/BCSH1-Semestralni-prace.git
cd BCSH1-Semestralni-prace

dotnet restore SkalaView.sln

cd ReactWeb
npm install
```

## Spusteni

### Desktopova aplikace

Z korene repozitare:

```powershell
dotnet run --project SkalaView.Desktop
```

### Browser verze

```powershell
cd ReactWeb
npm run dev
```

Vite vypise lokalni adresu, typicky:

```text
http://localhost:5173/
```

## Build

Build celeho .NET reseni:

```powershell
dotnet build SkalaView.sln
```

Build webove verze:

```powershell
cd ReactWeb
npm run build
```

`npm run build` nejdrive publikuje `SkalaView.Browser`, zkopiruje Avalonia browser bundle do `ReactWeb/public/avalonia` a potom spusti produkcni Vite build.

## Poznamky

- API token se neuklada do repozitare ani do konfiguracnich souboru.
- Watchlist se spravuje pres backend, ne pres lokalni soubory v klientovi.
- `bin/`, `obj/`, `node_modules/`, `dist/`, `.idea/`, `.vs/` a publish vystupy jsou ignorovane.
- `ReactWeb/public/avalonia` obsahuje webovy Avalonia bundle, ktery React host nacita jako staticka aktiva.
