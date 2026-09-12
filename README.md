# FsBank

A Windows application for scanning items in **Fellowship** and exporting their data as **JSON**. It uses screen capture and **Tesseract OCR** to recognize item information directly from the game.

## Features

- Capture item information from the game screen.
- Recognize item text with Tesseract OCR.
- Export scanned items in JSON format.

## Run

Requires Windows and the .NET 10 SDK.

```sh
dotnet run --project src/FsBank.Scanner
```
