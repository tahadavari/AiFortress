# ClaudeGuard for Windows

[فارسی](README.fa.md)

ClaudeGuard is a Windows notification-area app that checks your public IP country and Windows time zone against rules you choose. It can close Claude Desktop and Claude Code and block Claude domains when a check falls outside those rules.

## Install

1. Download `ClaudeGuard-<version>-win-x64.zip` from this repository’s **Releases** and extract it. The package includes the .NET runtime.
2. Right-click `install.bat` and select **Run as administrator**. The installer starts the app and adds a scheduled task for the account running the installer.
3. Double-click the shield in the Windows notification area to open ClaudeGuard. You can also run `app/ClaudeGuard.exe` from the extracted ZIP for a session without installing it.

The app requires administrator privileges for its Windows protection layers. The release targets Windows 10 and 11 on x64.

## Use

- Choose the required country and check interval. The default is the United States with a 30-second interval. Selecting a different country loads its default list of Windows time zones.
- Choose an allowed time zone and select **Set time zone** to apply it to Windows and run a fresh check. To customize the list, enter one Windows time zone ID per line and select **Save list**. Run `tzutil /l` to see the IDs available on your PC.
- Select **Check now** in the window or the tray menu to check immediately. The window shows the IP country, time zone and recent activity.
- Use **Language** to switch between English and Persian. The app saves your preference along with the other settings in `%ProgramData%\ClaudeGuard\config.json`.

The shield is green when both checks match, red when the blocking condition is met, amber when the IP country is unavailable or a protection operation needs attention, and gray before the first check or while protection is paused.

### Protection and privacy

You can independently enable process closing, Windows Firewall rules, hosts entries and Chrome/Edge policies. An IP-country mismatch or a time zone mismatch triggers the enabled layers. **Block if the IP country is unavailable** also triggers them when both location providers fail. Rules are lifted when the blocking condition clears or you pause or exit the app. The activity panel reports Windows operations that fail.

ClaudeGuard queries Country.is and uses IPinfo as an HTTPS fallback to determine the country of the public IP those services see. An app or browser using different network routing can have a different public IP. Firewall enforcement uses dynamically resolved addresses, which can be shared with other sites or change over time. [Country.is API](https://country.is/), [IPinfo API](https://ipinfo.io/developers/ipinfo-api)

## Uninstall

Exit any portable copy, then run the extracted release’s `uninstall.bat` as administrator. It removes the installed executable, scheduled task and rules tracked by ClaudeGuard. The settings file is kept for future installations. If you upgraded from an older build that wrote untracked Chrome or Edge URLBlocklist values, check those browser policies for leftover Claude entries.

## Build and release

Use the .NET 8 SDK and PowerShell on Windows.

```powershell
dotnet build ClaudeGuard.sln -c Release
dotnet run --project tests/ClaudeGuard.Tests -c Release --no-build
./scripts/publish.ps1 -Version 1.0.0
```

The publishing script writes the self-contained ZIP and SHA-256 checksum to `artifacts/release/`. The regression checks exercise configuration migration, Check Now, error recovery, and both UI languages without modifying Windows protection settings.

The GitHub workflow builds and tests pushes and pull requests. Push a version tag such as `v1.0.0` to publish the ZIP and checksum to GitHub Releases. Tags with a suffix such as `v1.0.0-rc.1` create prereleases. [Release workflow](.github/workflows/release.yml)

| Directory | Contents |
| --- | --- |
| `src/ClaudeGuard/Core/` | Checks, settings and localization loader |
| `src/ClaudeGuard/Ui/` | Windows Forms and tray controls |
| `src/ClaudeGuard/Windows/` | Windows protection and time zone integration |
| `src/ClaudeGuard/Localization/` | Embedded language catalogs |
| `tests/ClaudeGuard.Tests/` | Windows regression checks |
| `scripts/`, `packaging/` | Release packaging and installation |

### Add a language

Copy `src/ClaudeGuard/Localization/en.json` to a UTF-8 file such as `de.json` in the same directory. Set `Name` to the language’s name as it should appear in the picker, set `RightToLeft` for its writing direction, and translate the values in `Messages`. Preserve the message keys and placeholders such as `{0}`. The next build discovers the new language automatically and uses English for any missing keys.
