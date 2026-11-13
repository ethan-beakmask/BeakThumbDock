# BeakThumbDock User Manual

Applies to version 0.11.6

BeakThumbDock (the program and its executable are called ThumbDock) is a portable tool for Windows. It turns several windows, including windows on other virtual desktops, into live thumbnails and lines them up in a "thumbnail area" at the edge of the screen. Click a thumbnail and that window comes to the front; the thumbnails stay on screen, which suits watching several windows at once or switching quickly among many windows.

Other languages: [繁體中文](manual.md) | [日本語](manual.ja.md)

## 1. Requirements and download

- Windows 11 (developed and tested on Windows 11; Windows 10 has not been tested).
- No installation and no administrator rights are needed.

There are two release packages with identical features:

| Package | Files | Extra install needed |
|---|---|---|
| Self-contained (recommended) | A single `ThumbDock.exe`, about 50 MB | None |
| Framework-dependent | `ThumbDock.exe` plus a few small files, under 1 MB | .NET 10 Desktop Runtime |

Put the files in a **folder you can write to** (for example `D:\Tools\ThumbDock`) and run it. Settings and logs are stored next to the exe, so do not use a location such as `C:\Program Files` that needs administrator rights to write.

## 2. First start

1. Run `ThumbDock.exe`. A thumbnail area appears on the left of the screen with 8 empty slots showing "Waiting - Click to choose a window".
2. On the first start a tip about Chrome appears (see section 12). Click "Got it"; tick "Don't show again" if you do not want to see it again.
3. A white icon with a black circle appears in the notification area (system tray).

Tray icon:

| Action | Result |
|---|---|
| Double-click | Opens "Select Windows" |
| Right-click | Menu: Choose window…, Settings…, Exit |

To quit ThumbDock, use "Exit" in the tray menu.

## 3. Putting windows into slots

To open "Select Windows": click any empty slot, double-click the tray icon, or right-click the thumbnail area and choose "Choose window…".

"Select Windows" lists every open window, one per row. Each column on the left stands for one slot (the column header is the slot number).

- **Put into a slot**: tick the cell in that window's row under the slot you want.
- **Remove**: click the same cell again.
- **Crosshair guide**: when the mouse is over a tick cell, its row and column are highlighted and the crossing cell is darker, so you can check your choice when there are many windows and slots.
- The list refreshes by itself; there is no need to reopen it.

Other columns:

| Column | Meaning |
|---|---|
| On thumbnail click | Click to cycle: unchanged → maximize → fill work area. Decides how the window is placed after its thumbnail is clicked |
| Original position | The window's position recorded when it was put into the slot |
| Virtual desktop | Whether the window is on the current desktop or another one |
| Privilege | "Administrator (cannot be moved)" means the window runs as administrator (see section 17) |

The three "On thumbnail click" choices:

| Choice | Effect |
|---|---|
| Unchanged | Only brings the window to the front; position and size stay the same |
| Maximized | Maximizes the window (it does not cover a docked thumbnail area) |
| Fill work area | Does not maximize; resizes the window to "the screen minus the thumbnail area" |

The menu bar at the top of "Select Windows": Layout, Settings…, View, Run at startup.

## 4. Using thumbnails

| Action | Result |
|---|---|
| Click a thumbnail | Brings that window to the front and places it according to "On thumbnail click" |
| Drag a thumbnail onto another and release | The two thumbnails swap places (slot numbers and hotkeys follow the position) |
| Right-click a thumbnail | Choose window…, Edit match rule…, Clear slot, Edit slot positions… |
| Right-click an empty part of the thumbnail area | Edit slot positions…, Choose window…, Settings… |

The thumbnail of the window that is currently in front gets an orange frame.

If a source window is minimized, its thumbnail stops updating (a Windows limitation) and the slot shows "Minimized - thumbnail paused (click to restore)". In Settings you can tick "Automatically restore a minimized source window to the bottom of the z-order" so it is restored without covering other windows.

## 5. The settings window

Open it from the tray menu "Settings…" or from "Settings…" in "Select Windows". The settings window can stay open together with "Select Windows"; changes apply when you click OK. While the settings window is open you cannot switch or manage layouts, or edit slot positions.

### 5.1 Thumbnail area position

| Option | Meaning |
|---|---|
| Left / Right / Top / Bottom (docked) | Occupies one edge of the screen like the taskbar; other windows do not cover it when maximized |
| Full screen (thumbnail wall) | Thumbnails cover the whole screen (see section 6) |

- **Width / height**: the thickness of a docked thumbnail area, in pixels at 100% scaling; it grows with the display scaling. The upper limit is 70% of the screen in that direction.
- **Show window title below each slot**: turn it off to make the thumbnails slightly larger.

### 5.2 Columns × rows

The number of slots is "columns × rows", up to 100. Thumbnails are arranged in a grid of equal size, alignment and gap.

- A docked thumbnail area is narrow; 1 column × 8 rows or 2 columns × 8 rows are typical.
- You can set the gap, or tick "Fixed thumbnail size" to give each cell a width and height (scaled down proportionally when it does not fit).
- Slots are rearranged only when these settings (or the thumbnail area position or size) change; positions you adjusted by hand are not overwritten by normal use.
- With 24 slots or more a warning appears first: screen updates and operation become noticeably slower.
- **Adding slots**: new slots go into free space; existing slots stay where they are.
- **Removing slots**: only unbound slots are removed. If there are not enough, right-click a slot and choose "Clear slot" first, or tick "unbind all and rearrange".

### 5.3 Other settings

- **Mode**: single monitor or dual monitor (see section 7).
- **Windows on other virtual desktops**: see section 10.
- **Hotkeys**: see section 9.
- **Launch programs at startup**: see section 11.
- **Language**: see section 14.

## 6. Thumbnail wall (full screen)

In single-monitor mode, set the thumbnail area position to "Full screen" and the thumbnails cover the whole screen as a "thumbnail wall".

- Click a thumbnail: that window is shown on top of the wall.
- Return to the wall: press the "Show / hide thumbnail area" hotkey (default `Ctrl+Alt+D`). When the wall is already on top, pressing it again returns to the previous window.
- Keep "When returning to the thumbnail wall, restore windows that were maximized over the wall to their original size and position" ticked; it keeps covered browsers from freezing.
- **Mouse return** (off by default): when ticked in Settings, resting the mouse at the middle of the left screen edge for about 0.4 seconds returns to the wall. It is not triggered while dragging a window. It works only for a single-monitor wall.

## 7. Dual monitor

Change the mode to "Dual monitor" in Settings and choose:

- **Thumbnail monitor**: the monitor that holds the thumbnail area.
- **Focus monitor**: the monitor where you actually work with the windows. The two must be different.

When a thumbnail is clicked, **a window that was on the thumbnail monitor** is moved to the focus monitor; a window on any other monitor stays where it is and is only brought to the front.

With "Full screen" in dual-monitor mode, thumbnails cover only the thumbnail monitor. If one of the monitors is unplugged, ThumbDock temporarily switches to single-monitor mode and goes back by itself when the monitor returns.

## 8. Editing slot positions

Right-click the thumbnail area and choose "Edit slot positions…" to enter edit mode; slot frames become blue dashed lines. The built-in `default` layout cannot be edited; use "Save as new layout" first.

| Action | Result |
|---|---|
| Drag a slot | Move |
| Drag the right edge, bottom edge or bottom-right corner of a slot | Resize |
| Move near another slot or the edge of the thumbnail area | Edges snap into alignment (same row, same column, adjacent with a gap, same width or height) |
| Hold `Alt` while dragging | No snapping; moves pixel by pixel |
| Click a slot | Select it (the frame becomes a thick solid line) |
| Press `Del` with a slot selected | Deletes the slot (no confirmation) |
| Right-click a slot → "Delete this slot" | Same as above |
| Right-click empty space → "Add slot here" | Adds a slot |

When done, choose "Finish editing" in the right-click menu. Deleting a slot that has a window bound to it also deletes its match rule.

## 9. Hotkeys

Change them in the "Hotkeys" block of the settings window. They are shared by all layouts.

| Function | Default |
|---|---|
| Show / hide thumbnail area (returns to the wall in wall mode) | `Ctrl+Alt+D` |
| Switch to slot 1–9 | `Alt+Shift+1` to `Alt+Shift+9` (the modifier keys can be changed with the three drop-down lists in the settings window) |

If a hotkey is taken by another program, ThumbDock tells you which ones could not be registered. When several ThumbDock instances run at once, only the one started first has hotkeys.

## 10. Windows on other virtual desktops

Windows on other virtual desktops can be put into slots too, and their thumbnails update live just the same. There are two ways to handle a click, chosen in Settings:

| Option | Effect |
|---|---|
| Switch to the desktop the window is on (switch) | Windows switches to that virtual desktop as a whole |
| Move the window to the current desktop (pull) | The desktop does not change; the window is brought over |

pull uses undocumented Windows interfaces. On a Windows version it does not support, it falls back to switch automatically.

## 11. Finding windows again, and launching programs at startup

### 11.1 Match rules

When a window is put into a slot, ThumbDock stores a "match rule" (process name, window class, title). After the window closes, the slot shows "Waiting"; when the same window appears again it is put back into its slot automatically. This also holds after a reboot.

A rule can be changed with "Edit match rule…" in the slot's right-click menu. An empty field means no restriction; the title field is a regular expression (Regex). When several windows match, the first one is bound.

### 11.2 Launching programs at startup

Each layout can have a "launch config" that lists programs to open automatically and their slots. After ThumbDock starts, it opens them in order and puts the windows that appear into the given slots.

1. In Settings, tick "Allow ThumbDock to launch programs from the launch config file at startup" (off by default).
2. "Select Windows" → "Run at startup" → "Edit launch config…", and fill in program path, arguments and slot in the form.
3. It takes effect the next time ThumbDock starts; you can also choose "Start this layout's programs now" in the same menu.

A program is not opened again when its slot already has a window. For the file format see [launch-config.en.md](launch-config.en.md).

## 12. Chrome thumbnails freeze or turn black

When a Chrome window is completely covered by other windows, or is on another virtual desktop, Chrome stops drawing by itself, so the thumbnail freezes or turns black. This is Chrome's power-saving behaviour, not a ThumbDock problem.

The fix is to start Chrome with either of these flags:

```
--disable-features=CalculateNativeWinOcclusion
--disable-backgrounding-occluded-windows
```

Notes:

- The flags only work on **the first Chrome process that starts**. Close every `chrome.exe` completely first (including the background process in the tray), then start Chrome with the flag.
- To check: enter `chrome://version` in the Chrome address bar; the "Command Line" row must show the flag.
- Edge accepts the same flags. They had no effect on Brave in our tests.
- Turning this mechanism off makes Chrome use a little more CPU and power.

**Several Chrome accounts**: to open one window per account you need the account's profile folder name. Switch to that account and enter `chrome://version`; the last part of "Profile Path" (for example `Profile 4`) is the name, used as `--profile-directory="Profile 4"`. A template is in [launch-sample-chrome-9accounts.json](launch-sample-chrome-9accounts.json).

## 13. Layouts

A "layout" stores the position and size of the thumbnail area, and the position and match rule of every slot. You can keep different layouts for different kinds of work.

In the "Layout" menu of "Select Windows": switch layout, New blank layout, Save as new layout, Rename, Delete.

- `default` is a built-in fixed layout (docked left, 200 px wide, 8 slots in one column). Its area settings and slot positions cannot be changed, and it cannot be renamed or deleted. To customize, use "Save as new layout" first.
- Tick "When switching layouts, also get programs ready from that layout's launch config" in Settings to have the programs that the layout needs, and that are not open yet, started when you switch.

## 14. Language

"Language" at the bottom of the settings window selects the interface language; it takes effect after ThumbDock is restarted. "Automatic" follows the Windows language.

- Built in: 繁體中文, English, 日本語, 简体中文.
- **Japanese and Simplified Chinese are machine-translated first drafts** that have not been reviewed by native speakers. Reports and corrections are welcome on the project page.
- Adding or fixing a language does not need a rebuild: put a language file into `config\lang\` next to the exe and restart. See [translating.en.md](translating.en.md).
- Right-to-left languages are not supported.

## 15. Command line and running several instances

| Option | Meaning |
|---|---|
| `--layout <name>` | Start with the given layout |
| `--monitor <number>` | Single-monitor mode with the thumbnail area on the given monitor (numbers are shown in the settings window) |
| `--no-launch` | Do not run the launch config this time |
| `--safe` | Safe start (see below) |
| `--help` | Show help |

**Several instances**: at most one ThumbDock per physical monitor, each using a different layout.

```
ThumbDock.exe --layout layoutname_1 --monitor 1
ThumbDock.exe --layout layoutname_2 --monitor 2
```

A dual-monitor layout occupies all monitors; no second instance can be started while it runs.

**Safe start**: when a setting has gone wrong and the thumbnail area blocks the screen, start with `ThumbDock.exe --safe`. It ignores the last layout, uses the built-in `default` layout and does not launch any programs.

## 16. Corporate environments and endpoint protection

ThumbDock works by enumerating all windows, querying the names and privileges of other programs, and bringing other programs' windows to the front or moving them. Some corporate endpoint protection products (EDR / XDR) may see this as suspicious behaviour and raise an alert or terminate the program.

- The program does not connect to the network, does not read other programs' memory, and does not need administrator rights.
- The only place it reads another program's data: when Chrome or Edge is selected in the launch config form, it reads the browser's profile list (account names) to fill the drop-down. If you do not use that form, nothing is read.
- If it is blocked in a corporate environment, contact your information security administrator. **Code signing usually does not help against this kind of behavioural detection**; what works is for the administrator to add the SHA256 hash of that release file to the allow list. The hash of each version is listed on the release page.
- A hash matches one file only. After updating, or after building from source yourself, the hash differs and a new request is needed.
- "Launch programs at startup" is off by default for the same reason.

To check a file hash yourself (PowerShell):

```
Get-FileHash .\ThumbDock.exe -Algorithm SHA256
```

## 17. Known behaviour and limitations

| Item | Description |
|---|---|
| Maximize | A maximized window does not cover a docked thumbnail area |
| Full-screen programs | Full screen such as `F11` in a browser covers the thumbnail area (it gives way, like the taskbar) |
| Command Prompt `Alt+Enter` | In full screen the thumbnail area stays on top of it |
| Administrator windows | For a window running as administrator, thumbnails and switching work, but ThumbDock cannot move or resize it. A blue shield is shown at the right end of the thumbnail title. To move it, start ThumbDock as administrator |
| Minimized windows | The thumbnail stops updating (a Windows limitation) |
| MobaTextEditor | The text editor built into MobaXterm hangs and closes when ThumbDock maximizes it. Do not put it into a slot set to "Maximized" |
| Microsoft Store apps | Not supported by "Launch programs at startup" |
| Hotkeys | With several instances, only the one started first has hotkeys |
| Other virtual desktops (switch) | The whole Windows desktop is switched, on all monitors together |

## 18. File locations and troubleshooting

All files are in the folder that contains `ThumbDock.exe`:

| Path | Content |
|---|---|
| `config\settings.json` | Global settings (hotkeys, last used layout and so on). With several instances, the others use `settings.DISPLAYn.json` |
| `config\language.txt` | Interface language |
| `config\layouts\` | Layout files |
| `config\launch\` | Launch configs |
| `config\lang\` | Language files you add or override |
| `logs\thumbdock.log` | Log file; please attach it when reporting a problem |

Common problems:

| Situation | What to do |
|---|---|
| The thumbnail area is too large and blocks the screen | Exit the program and start it with `ThumbDock.exe --safe` |
| A thumbnail is black or frozen | Chrome / Edge: see section 12; minimized window: see section 4 |
| The window does not move after clicking its thumbnail | The window may be running as administrator (section 17) |
| After a reboot a slot shows "Waiting" | The program is not open yet, or its title changed. Loosen the rule with "Edit match rule…" |
| A program that minimizes to the system tray, such as LINE, shows "Waiting" in its slot | When such a program goes to the system tray its window is hidden, so there is no picture to show as a thumbnail. The slot is bound again automatically once the window is brought back from the tray. To keep the thumbnail live, turn off the program's "minimize to tray" option and leave the window open (it may be covered by other windows, but do not minimize it) |
| The program is closed when "Select Windows" opens | It may have been blocked by endpoint protection (section 16) |
| Hotkeys do nothing | They are taken by another program, or this is not the first ThumbDock that was started |

## 19. Removal

Exit ThumbDock and delete the whole folder. ThumbDock does not write to the registry and leaves no files anywhere else.
