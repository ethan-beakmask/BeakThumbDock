# Launch config (launching programs at startup)

Each layout can have a launch config that lists programs to open. After ThumbDock starts, it runs them in order and binds the windows that appear to the given slots.

Other languages: [繁體中文](launch-config.md) | [日本語](launch-config.ja.md)

## Turning it on

1. Tray icon → "Settings…" → tick "Allow ThumbDock to launch programs from the launch config file at startup" (**off by default**).
2. "Select Windows" → "Run at startup" → "Edit launch config…" opens a form. To edit the JSON directly, click "Edit in Notepad (advanced)…" in the form; it creates a template and opens it in Notepad.
3. Save. After that:
   - When ThumbDock starts, it runs the config of the current layout.
   - Or by hand: "Select Windows" → "Run at startup" → "Start this layout's programs now".
4. The command line option `--no-launch` forces it off and takes priority over the setting.

File location: `config\launch\<layout name>.json`.

### Switching between work setups

One work setup (office work, development, monitoring…) is one layout with its own launch config. Tick "When switching layouts, also get programs ready from that layout's launch config" in Settings (off by default; "Allow…" must be on too). Switching layouts from the menu then:

1. Loads the new layout first and lets auto-binding reconnect windows that are **already open**.
2. Launches only the entries whose slot **has no window yet**. Programs already open are not opened again, and **programs of the previous layout are not closed**.
3. Launches nothing for a layout without a config, or for a slot that the config does not list.

For an open program to be recognized by the new layout, its slot needs a match rule. The rule is created and stored in the layout the first time a launch binds the window.

## The form

The left side lists the entries (the order is the launch order; add, duplicate, delete, move up, move down). The right side shows fields by "Program type", and the bottom shows the actual path and arguments as you type.

| Program type | Fields |
|---|---|
| Chrome / Edge | Profile (drop-down read from the browser's Local State; type it in if it cannot be read), URL, tick boxes for the two occlusion flags, other flags |
| Command Prompt (CMD) | Mode (`/k` keep the window open, `/c` close after running), command |
| Program / shortcut / batch file | Arguments |

Common fields: slot, program path (with Browse), working directory, delay after the entry in milliseconds, seconds to wait for the window, window match rule, note, and "Test this entry" (starts the program only; does not bind a slot).

- "Program type" is not stored in the file; it is worked out from `path` and `args` when the file is opened. Unknown forms (for example Brave, or `cmd.exe /d /k`) are treated as a general program with a raw arguments field.
- **Saving rewrites the whole JSON file.** Hand-written `//` comments are lost; the old file is copied to `<layout>.json.bak` first. Put comments you want to keep into each entry's `note` field.
- The form cannot be saved when a slot is used twice or a program path is missing. A slot number beyond the layout's slot count, or an invalid title regex, only gives a warning.

## Format

```json
{
  "launch": [
    {
      "slot": 1,
      "path": "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
      "args": "--profile-directory=\"Profile 3\" https://example.com",
      "workDir": "",
      "delayMs": 1500,
      "waitSec": 15,
      "match": { "process": "chrome.exe", "titleRegex": "part of the window title" }
    },
    {
      "slot": 2,
      "path": "cmd.exe",
      "args": "/k cd /d D:\\work && python run.py"
    }
  ]
}
```

| Field | Required | Meaning |
|---|---|---|
| `slot` | yes | Slot number, starting at 1. One entry per slot; duplicates are skipped |
| `path` | yes | A program, shortcut (.lnk) or batch file. Environment variables such as `%ProgramFiles%` and names found on PATH (such as `cmd.exe`) are allowed |
| `args` | no | Arguments |
| `workDir` | no | Working directory |
| `delayMs` | no | How long to wait after this entry before launching the next. Default 1000, maximum 60000 |
| `waitSec` | no | How many seconds to wait for the program's window. Default 15, range 1–120 |
| `match` | no | Match rule (`process`, `class`, `titleRegex`); see below |
| `note` | no | A note, ignored at run time and kept when the form saves |

JSON notes:

- **Write each backslash in a path twice** (`C:\\Program Files\\...`) and write a double quote inside arguments as `\"`. On an error, the line number is shown.
- `//` comments and a trailing comma after the last entry are accepted.

## How a window is bound to a slot

| `match` given? | What happens |
|---|---|
| No (default) | The existing windows are recorded before the launch, and **the first new window** afterwards is bound to the slot (a window whose process name equals `path` is preferred). A rule is generated and stored in the layout |
| Yes | The rule is written to the slot first, then ThumbDock waits for a window that matches it |

Write a `match` when many copies of the same program are launched (several Chrome accounts, for example), where the first new window may not be the one you want, or when another program happens to open a window at the same time. Use "Select Windows" to see the real titles.

## Browser stops drawing on another virtual desktop or when covered

Chromium browsers (Chrome, Edge, Brave…) have window occlusion detection: a window on another virtual desktop, or completely covered by other windows, stops drawing, so its thumbnail freezes or turns black.

This entry was tested and **works** (Chrome, a video on another virtual desktop keeps playing):

```json
{
  "slot": 3,
  "path": "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
  "args": "--disable-features=CalculateNativeWinOcclusion --profile-directory=\"Profile 10\" https://www.youtube.com/",
  "match": { "process": "chrome.exe", "titleRegex": "YouTube" }
}
```

- Either flag works: `--disable-features=CalculateNativeWinOcclusion` or `--disable-backgrounding-occluded-windows`. Incognito mode and disabling extensions make no difference.
- The flag is only needed on **the first Chrome process that starts**; windows opened afterwards with other `--profile-directory` values in the same user data folder inherit it. The other way round: if Chrome is already running (including its background process), a flag given later is ignored. Put the entry with the flag first, or close every chrome.exe beforehand.
- To check, run this in PowerShell; the main process's command line must show the flag: `Get-CimInstance Win32_Process -Filter "Name='chrome.exe'" | ? {$_.CommandLine -notmatch '--type='} | select ProcessId,CommandLine`
- A separate `--user-data-dir` is not needed. It also works, but it is a brand-new profile: no account chooser, no bookmarks, and you have to sign in again.
- These flags are a community-reported workaround that is **not in the official Chromium documentation** and may stop working in a future browser version. Turning occlusion detection off increases CPU and power use.
- The flags **do not work** on Brave in our tests; thumbnails still turn black.

A template for one Chrome window per account is in [launch-sample-chrome-9accounts.json](launch-sample-chrome-9accounts.json).

## Behaviour and limits

- An entry is **not launched** when its slot already has a window (including one just bound by a rule), to avoid opening things twice.
- Programs are started as the current user without elevation. Do not put passwords in the config.
- CMD: use `cmd.exe` with `/k command` (stays open after running). On Windows 11 the CMD window may be hosted by `WindowsTerminal.exe`; this is normal, and binding goes by window.
- Store / UWP apps are not supported.
- Every launch is written to `logs\thumbdock.log` (path, arguments, binding result, timeouts). Please attach it when reporting a problem.
- An entry whose slot does not exist (for example `slot: 6` with only 4 slots) is skipped and logged; change the slot count in Settings first.
