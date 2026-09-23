# FsBank

A Windows application for scanning items in **Fellowship** and exporting their data as **JSON**. It uses screen capture and **Tesseract OCR** to recognize item information directly from the game.

## Features

- Capture item information from the game screen.
- Recognize item text with Tesseract OCR.
- Export scanned items in JSON format.
- Browse each export in a standalone [`items.html`](https://oleg-st.github.io/FsBank/item-browser.html) with search, filters, sorting,
  and item groups by slot and name. The page embeds the same data as `items.json`.

## Download

Download `FsBank-win-x64.zip` from GitHub Releases, extract the entire archive,
and run `FsBank.Scanner.exe`. Requires Windows x64; no .NET installation is needed.

## Run from source

Requires Windows and the .NET 10 SDK.

```sh
dotnet run --project src/FsBank.Scanner
```
