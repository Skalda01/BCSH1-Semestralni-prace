# SkalaView https://skalicky-test.cz/



SkalaView je multiplatformní aplikace pro sledování kryptoměnových trhů. Projekt kombinuje sdílené Avalonia UI, desktopovou aplikaci, browser/WASM variantu a React/Vite webový host.

Aplikace je zaměřená na praktický trading dashboard: dynamický watchlist, živá tržní data, svíčkový graf, indikátorový panel a order book pro aktuálně vybraný symbol.

## Klíčové funkce

| Funkce | Popis |
| --- | --- |
| Dynamický watchlist | Uživatel se připojí pomocí API tokenu a aplikace načte jeho serverový watchlist. Tickery lze přidávat a odebírat přímo z aplikace. |
| Ověření API tokenu | Token se posílá na backend jako `Bearer` token. Po úspěšném ověření backend vrátí watchlist navázaný na daný token. |
| Live ticker přehled | Watchlist se pravidelně aktualizuje o cenu, denní změnu, low/high, objem a quote volume z Binance API. |
| Svíčkový graf | Graf načítá OHLCV data z Binance klines endpointu a reaguje na změnu tickeru i timeframe. |
| Indikátorový panel | Live statistiky pro aktivní symbol přicházejí přes Binance WebSocket ticker stream. |
| Order book | Hloubka trhu se načítá přes Binance WebSocket depth stream. |
| Sdílené UI | Jedno Avalonia UI jádro se používá pro desktopovou i browser variantu. |

## Architektura projektu

```text
SkalaView.sln
|-- SkalaView/              Sdílené Avalonia UI, viewmodely a API servisy
|-- SkalaView.Desktop/      Desktopový vstupní projekt
|-- SkalaView.Browser/      Browser/WASM vstupní projekt
|-- ReactWeb/               React + Vite host pro browser verzi
`-- global.json             Konfigurace .NET SDK
```

### Sdílená aplikační vrstva

Projekt `SkalaView` obsahuje hlavní UI, viewmodely a komunikaci s externími API. Sdílený stav aplikace drží `SharedViewModel`, hlavně vybraný ticker a timeframe. Díky tomu se po změně tickeru synchronně přepíná graf, indikátory i order book.

Důležité části:

- `TickerMenuViewModel` řeší API token, serverový watchlist a pravidelný refresh ticker snapshotů.
- `CandlestickChartViewModel` načítá svíčková data pro aktuální symbol a timeframe.
- `IndicatorMenuViewModel` spravuje live ticker stream.
- `OrderBookViewModel` spravuje live order book stream.
- `UserWatchlistApiService` komunikuje s backendem pro token a watchlist.
- `Binance*ApiService` třídy oddělují práci s Binance REST a WebSocket API od UI logiky.

## API token a watchlist

Watchlist není pevně zapsaný v klientovi. Po spuštění aplikace se zobrazí výchozí tickery, ale po zadání API tokenu aplikace zavolá backend a načte watchlist uživatele.

Klient používá backend endpointy:

```text
POST   /api/app/validate-token
POST   /api/app/watchlist
DELETE /api/app/watchlist/{symbol}
```

Token se posílá v HTTP hlavičce:

```http
Authorization: Bearer <api-token>
```

Aktuální base URL je nastavené v `SkalaView/apiService/UserWatchlistApiService.cs`:

```text
https://skalicky-test.cz/backend/api/app
```

Tok v aplikaci:

1. Uživatel zadá API token.
2. Klient zavolá `validate-token`.
3. Backend ověří token a vrátí watchlist.
4. Aplikace nahradí lokální seznam tickerů serverovým watchlistem.
5. Přidání nebo odebrání tickeru se posílá na backend.
6. Backend vrací aktualizovaný watchlist, který se znovu propíše do UI.

Tento přístup drží watchlist per-user/per-token na serveru a klient zůstává bez persistentní lokální databáze watchlistu.

## Tržní data

| Část aplikace | Zdroj dat | Detail |
| --- | --- | --- |
| Watchlist ticker data | Binance REST API | Aktuální cena, změna, low/high, volume a quote volume. |
| Svíčkový graf | Binance REST API `/api/v3/klines` | Posledních 300 svíček pro vybraný symbol a timeframe. |
| Indikátory | Binance WebSocket `@ticker` | Live statistiky aktivního symbolu. |
| Order book | Binance WebSocket `@depth20@100ms` | Live bid/ask hloubka trhu. |

Symboly se normalizují na Binance formát. Pokud uživatel zadá například `BTC`, API servis pracuje s párem `BTCUSDT`.

## Technologie

- .NET 8
- Avalonia UI 11
- LiveChartsCore
- React 19
- Vite 7
- TypeScript
- Binance REST API
- Binance WebSocket API

## Požadavky

- .NET SDK 8.0
- Node.js a npm
- PowerShell pro build script webové Avalonia části
- Visual Studio 2022 nebo JetBrains Rider pro pohodlný vývoj

Projekt obsahuje `global.json` s .NET SDK `8.0.124` a `rollForward` nastaveným na `latestFeature`.

## Instalace

```powershell
git clone https://github.com/Skalda01/BCSH1-Semestralni-prace.git
cd BCSH1-Semestralni-prace

dotnet restore SkalaView.sln

cd ReactWeb
npm install
```

## Spuštění

### Desktopová aplikace

Z kořene repozitáře:

```powershell
dotnet run --project SkalaView.Desktop
```

### Browser verze

```powershell
cd ReactWeb
npm run dev
```

Vite vypíše lokální adresu, typicky:

```text
http://localhost:5173/
```

## Build

Build celého .NET řešení:

```powershell
dotnet build SkalaView.sln
```

Build webové verze:

```powershell
cd ReactWeb
npm run build
```

`npm run build` nejdříve publikuje `SkalaView.Browser`, zkopíruje Avalonia browser bundle do `ReactWeb/public/avalonia` a potom spustí produkční Vite build.

## Poznámky

- API token se neukládá do repozitáře ani do konfiguračních souborů.
- Watchlist se spravuje přes backend, ne přes lokální soubory v klientovi.
- `bin/`, `obj/`, `node_modules/`, `dist/`, `.idea/`, `.vs/` a publish výstupy jsou ignorované.
- `ReactWeb/public/avalonia` obsahuje webový Avalonia bundle, který React host načítá jako statická aktiva.
