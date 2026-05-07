# SkalaView

SkalaView je multiplatformni aplikace pro sledovani kryptomenovych trhu. Kombinuje desktopove Avalonia UI, browser/WASM build, React/Vite webovy host a samostatny API backend pro praci s uzivatelskymi API tokeny a dynamickym watchlistem.

Projekt vznikl jako semestralni prace pro BCSH1.

## Hlavni vlastnosti

| Oblast | Popis |
| --- | --- |
| Market dashboard | Prehled tickeru, svickovy graf, indikatorovy panel a order book pro vybrane symboly. |
| Live data | Integrace s Binance REST API pro historicke svicky a ticker snapshoty. WebSocket streamy se pouzivaji pro live indikatory a order book. |
| API token | Aplikace overuje API token pres backend endpoint a uklada jen aktivni token v runtime klienta. Token se posila jako `Bearer` authorization header. |
| Dynamicky watchlist | Watchlist je navazany na overeny API token. Uzivatel muze tickery pridavat a odebirat, backend vraci aktualni serverovy stav watchlistu. |
| Sdilene UI | Zaklad aplikace je v projektu `SkalaView`, ktery sdili views, viewmodely a servisni vrstvu mezi desktopovou a browser variantou. |
| Web host | `ReactWeb` hostuje Avalonia browser bundle pres Vite a umoznuje spusteni webove verze aplikace. |

## Architektura

```text
SkalaView.sln
|-- SkalaView/                 Sdilene Avalonia UI, viewmodely a API servisy
|-- SkalaView.Desktop/         Desktopovy vstupni projekt
|-- SkalaView.Browser/         Browser/WASM vstupni projekt
|-- ReactWeb/                  React + Vite host pro webovou aplikaci
`-- SkalaView/server js/       Backend pro auth, API klice a serverovy watchlist
```

### Klient

Klient je postaveny nad Avalonia UI. `SharedViewModel` drzi spolecny stav aplikace, napriklad vybrany ticker a timeframe. Jednotlive casti UI reaguji na zmenu vybraneho tickeru a automaticky prepinaji datove streamy.

- `TickerMenuViewModel` nacita serverovy watchlist, pridava/odebira tickery a pravidelne obnovuje ticker snapshoty.
- `CandlestickChartViewModel` nacita svickova data z Binance klines API podle tickeru a timeframe.
- `IndicatorMenuViewModel` sleduje Binance ticker WebSocket stream.
- `OrderBookViewModel` sleduje Binance depth stream pro aktualni ticker.

### API backend

Repo obsahuje dve backendove casti:

- Node.js server v `SkalaView/server js/server.js` resi registraci, login, session cookie, spravu API klicu a proxy smerem na dashboard API.
- .NET minimal API v `SkalaView/server js/Program.cs` resi endpointy pro desktop/browser aplikaci: overeni API tokenu a persistentni watchlist.

Klient vola `UserWatchlistApiService`, ktery komunikuje s:

```text
POST   /api/app/validate-token
GET    /api/app/watchlist
POST   /api/app/watchlist
DELETE /api/app/watchlist/{symbol}
```

Aktualni klientsky base URL je nastavene v `SkalaView/apiService/UserWatchlistApiService.cs`:

```text
https://skalicky-test.cz/backend/api/app
```

Pri lokalnim testovani vlastniho backendu je potreba tuto hodnotu zmenit na lokalni URL nebo nasadit reverse proxy, ktera zachova stejnou cestu API.

Token se posila v hlavicce:

```http
Authorization: Bearer <api-token>
```

Backend token neporovnava v plaintextu. Token se hashuje pomoci SHA-256 a overuje proti tabulce `api_keys`. Po uspesnem overeni se aktualizuje `last_used_at`. Watchlist je ulozeny oddelene v SQLite tabulce `watchlist_tickers` a je vazany na hash tokenu.

## Datove zdroje

| Data | Zdroj | Pouziti |
| --- | --- | --- |
| Ticker snapshots | Binance REST API | Cena, zmena, low/high, objem a quote volume ve watchlistu. |
| Candles | Binance REST API `/api/v3/klines` | Svickovy graf, standardne poslednich 300 svicek. |
| Ticker stats | Binance WebSocket `@ticker` | Live indikatory pro aktivni symbol. |
| Order book | Binance WebSocket `@depth20@100ms` | Live bid/ask hloubka trhu. |
| Watchlist | Vlastni API backend + SQLite | Per-token serverovy seznam sledovanych symbolu. |

## Pozadavky

- .NET SDK 8.0
- Node.js a npm pro webovy host
- PowerShell pro build script `ReactWeb/scripts/build-avalonia-web.ps1`
- Visual Studio 2022 nebo JetBrains Rider pro vyvoj v IDE

Projekt obsahuje `global.json` s .NET SDK `8.0.124` a `rollForward` nastavenym na `latestFeature`.

## Instalace

```powershell
git clone https://github.com/Skalda01/BCSH1-Semestralni-prace.git
cd BCSH1-Semestralni-prace

dotnet restore SkalaView.sln

cd ReactWeb
npm install
```

## Spusteni aplikace

### Desktop

Z korene repozitare:

```powershell
dotnet run --project SkalaView.Desktop
```

### Browser / web host

```powershell
cd ReactWeb
npm run dev
```

Vite vypise lokalni adresu, typicky:

```text
http://localhost:5173/
```

### .NET API backend pro token a watchlist

Backendovy projekt je v adresari `SkalaView/server js`. Pri lokalnim spusteni posloucha na portu `5050`.

```powershell
dotnet run --project "SkalaView/server js/Server.csproj"
```

Volitelne promenne prostredi:

| Promenna | Vychozi hodnota | Ucel |
| --- | --- | --- |
| `WEB_DATABASE_PATH` | `/var/www/html/app.sqlite` | SQLite databaze s uzivateli a API klici. |
| `DATABASE_PATH` | pouzije se jako fallback pro `WEB_DATABASE_PATH` | Alternativni cesta k webove databazi. |
| `WATCHLIST_DATABASE_PATH` | `watchlist.sqlite` | SQLite databaze pro per-token watchlist. |

### Node auth/API server

Node server obsluhuje registraci, prihlaseni, session cookie, spravu API klicu a staticke soubory.

```powershell
node "SkalaView/server js/server.js"
```

Volitelne promenne prostredi:

| Promenna | Vychozi hodnota | Ucel |
| --- | --- | --- |
| `PORT` | `8787` | Port Node serveru. |
| `APP_URL` | automaticky z requestu | Verejna URL aplikace za proxy. |
| `DASHBOARD_API_URL` | `https://skalicky-test.cz` | Cil pro proxy API pozadavky. |
| `DATABASE_PATH` | `app.sqlite` | SQLite databaze pro auth a API klice. |

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

`npm run build` nejdrive spusti `npm run build:avalonia`, ktery publikuje projekt `SkalaView.Browser` a zkopiruje vysledny Avalonia bundle do `ReactWeb/public/avalonia`. Nasledne Vite vytvori produkcni build React hostu.

## Bezpecnostni poznamky

- API token se overuje na backendu, ne v klientovi.
- Backend uklada hash tokenu, ne samotny token.
- Zrusene API klice se ignoruji pres `revoked_at`.
- Watchlist je oddeleny podle hashe tokenu, takze jeden API token vidi jen vlastni symboly.
- Session cookie Node serveru je `HttpOnly` a pouziva `SameSite=Lax`.

## Git a generovane soubory

V repozitari nejsou ukladane lokalni ani build artefakty jako `.idea/`, `.vs/`, `bin/`, `obj/`, `node_modules/`, `dist/` a publish vystupy. Webovy Avalonia bundle v `ReactWeb/public/avalonia` je soucasti projektu, protoze ho webovy host nacita jako staticka aktiva.
