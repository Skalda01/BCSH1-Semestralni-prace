# SkalaView

Semestralni prace pro BCSH1. SkalaView je aplikace pro sledovani trznich dat s desktopovym i webovym rozhranim. Jadro UI je postavene v Avalonia UI, grafy vykresluje LiveCharts a data aplikace nacita pres servisni vrstvu pro Binance API.

## Funkce

- prehled tickeru a watchlist vybranych symbolu
- svickovy graf pro sledovani vyvoje ceny
- menu technickych indikatoru
- order book pohled
- spolecne Avalonia UI pro desktopovou i browser variantu
- React/Vite host pro webovou verzi aplikace

## Struktura projektu

```text
SkalaView/
  SkalaView/              Sdilene Avalonia UI, viewmodely a API servisy
  SkalaView.Desktop/      Desktopovy spoustec aplikace
  SkalaView.Browser/      Browser/WASM spoustec aplikace
  ReactWeb/               React + Vite host pro webovou verzi
  SkalaView.sln           Visual Studio/Rider solution
```

## Pozadavky

- .NET SDK 8.0
- Node.js a npm pro webovou cast
- PowerShell pro build skript webove Avalonia casti
- Visual Studio 2022 nebo JetBrains Rider volitelne pro pohodlny vyvoj

Projekt pouziva `global.json` s .NET SDK `8.0.124` a povolenym `rollForward` na nejnovejsi feature verzi.

## Instalace

```powershell
git clone https://github.com/Skalda01/BCSH1-Semestralni-prace.git
cd BCSH1-Semestralni-prace
dotnet restore SkalaView.sln
cd ReactWeb
npm install
```

## Spusteni desktopove aplikace

Z korenove slozky repozitare:

```powershell
dotnet run --project SkalaView.Desktop
```

Desktopova aplikace pouziva `SkalaView.Desktop` jako vstupni projekt a sdilene UI z projektu `SkalaView`.

## Spusteni webove verze

Z korenove slozky repozitare:

```powershell
cd ReactWeb
npm run dev
```

Vite vypise lokalni adresu, typicky `http://localhost:5173/`.

## Build

Build celeho .NET reseni:

```powershell
dotnet build SkalaView.sln
```

Build webove verze vcetne publikovani Avalonia browser bundle:

```powershell
cd ReactWeb
npm run build
```

Skript `npm run build` nejdrive spusti `scripts/build-avalonia-web.ps1`, ktery publikuje `SkalaView.Browser` a zkopiruje vystup do `ReactWeb/public/avalonia`. Potom se spusti `vite build`.

## Poznamky k repozitari

Do repozitare se neukladaji lokalni IDE soubory, `bin/`, `obj/`, `node_modules/`, `dist/` ani publish vystupy. Webovy Avalonia bundle v `ReactWeb/public/avalonia` je soucasti projektu, protoze ho React host nacita jako staticka aktiva.
