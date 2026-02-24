# DriveFlip Web Demo

An interactive HTML demo of the DriveFlip application, designed to be embedded on the product website via iframe.

## Purpose

This is a **fully functional simulation** of the DriveFlip desktop app UI that lets potential users explore the interface before downloading. It replicates the 3-pane layout, drive selection, health data inspection, SMART attributes, and operation workflow using sample drive data.

## Usage

Embed on a website with:

```html
<iframe src="web-demo/index.html" width="100%" height="680" style="border:none; border-radius:8px;"></iframe>
```

Or open `index.html` directly in a browser to preview.

## What's Interactive

- **Drive list** - Click drives to view details, check/uncheck for operations, filter to external only
- **Drive details** - Info grid, health data, SMART attributes (click "Get SMART Data")
- **Zoom controls** - +/- buttons or Ctrl++/Ctrl+- to scale the detail pane
- **Copy Info** - Copies a formatted drive report to clipboard
- **Operations** - Surface Check, Wipe, Check & Wipe run a simulated 45-second animation with progress bars, phase checklist, visualization canvas, and speed stats
- **Wipe options** - Expandable panel with mode/method/verify toggles
- **Cancel** - Stops a running operation mid-way

## Sample Data

Four demo drives are included:

| Drive | Type | Risk | Purpose |
|-------|------|------|---------|
| Samsung 870 EVO 500GB | SSD / SATA | Good | Shows healthy SSD with wear data |
| WDC WD2003FZEX 2TB | HDD / SATA | Warning | Shows high-hour HDD |
| Kingston DataTraveler 32GB | USB / Removable | Good | Shows external/removable drive |
| Seagate ST3000DM001 3TB | HDD / SATA | Critical | Shows failing drive with bad sectors |

## Styling

All colors, spacing, and typography match the WPF desktop app:

- Background: `#1E1E2E` (window), `#2B2B3D` (cards)
- Accents: Blue `#4A8AC4`, Green `#388E3C`, Red `#D32F2F`, Amber `#FFC107`
- Font: Segoe UI, 13px base
- Dark theme with Mica-style appearance

## Files

- **index.html** - Self-contained demo (HTML + CSS + JS, no dependencies)
- **README.md** - This file

## Notes

- No external dependencies or build step required - single HTML file
- Responsive: hides right pane below 860px, stacks vertically below 640px
- Not connected to any backend - all data is simulated in JavaScript
- Safe for public embedding - no sensitive code or API keys
