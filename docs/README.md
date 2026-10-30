# Screenshots

Drop the following files here so `README.md` renders correctly.

| File | What it should show | Suggested size |
|---|---|---|
| `panel.png` | The panel with two or three devices, at least one asleep | 1412 × 860 (150% scaling) |
| `tray-menu.png` | The tray context menu, one row hovered | 270 × N, as captured |
| `empty.png` | The empty state with the designer credit | 1412 × 860 |

Take them with the panel open and real hardware attached. Do not scale them up;
the interface is drawn at native resolution and looks best unmodified.

Recommended capture method:

```powershell
# Panel
powershell -ExecutionPolicy Bypass -File tools\shot.ps1
```

The tray menu has to be captured manually — Win+Shift+S, then crop to the popup.
