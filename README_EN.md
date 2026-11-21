# BLCA Admin Manger 1.1.4

Developed by **BLCA Development Team**.

BLCA Admin Manger is a Windows administration application for Arma Reforger servers running a compatible **BLCA News_and_Menu** addon. It combines staff permissions, player management, bans, news, weather, an audit viewer, and reports for administrator activity and player connection hours.

Run **BLCA Admin Manger.exe** alongside this guide. The published Windows x64 release includes its runtime. **Source** contains the editable C# application, tests, configuration examples, and build instructions. The supplied logo retains the original **BLC** lettering.

## Moving existing server data

When upgrading an existing server, first stop the server, manager, and any background agent. Keep a copy of the current administration data folder in the actual server profile, then rename that folder to `BLCAAdminSuite` in the same location. Preserve its complete contents, including `config.json`, `config.rev`, `audit`, `manager-data`, bans, and other files. Run the updated addon together with BLCA Admin Manger 1.1.4, then select the profile or `BLCAAdminSuite` folder in the application. Existing permissions, bans, and history continue loading after this manual folder move.

To retain local folder selections, close the application and rename its current preferences folder under `%LOCALAPPDATA%` to `BLCAAdminManager`, preserving `settings.json`. You can also select the server profile and log folder again in the interface after updating.

## Connect to the server

1. Run BLCA News_and_Menu on the server once so it creates its data folder.
2. Open the manager and select **اختيار مجلد** beside the server-profile field.
3. Select the actual server profile folder or the `BLCAAdminSuite` folder inside it, then select **اتصال**.
4. Wait for the top status line to report that the addon is connected and its configuration is ready.

For a server launched with `-profile D:\Reforger\ServerA`, the actual data folder may be

```text
D:\Reforger\ServerA\profile\BLCAAdminSuite
```

The actual location depends on the server's launch configuration. Select the folder containing a `status.json` file that updates while the addon runs. If the server is on another computer, the selected path needs read/write access to those actual server files.

The manager exchanges local files with the addon. This connection requires no network port or RCON setup. Server permissions and bans use `$profile:BLCAAdminSuite/`, and local folder preferences use `%LOCALAPPDATA%\BLCAAdminManager\settings.json`.

## Pages and controls

The interface uses Arabic labels. This table maps them to their purposes.

| Interface page | Purpose |
| --- | --- |
| نظرة عامة | Connection status, online players, GM slots, active bans, and addon update timings |
| فريق الإدارة | Add staff, edit permissions, and disable accounts |
| اللاعبون | Connected and historical players, search, details, kicks, and bans |
| الحظر | Add, edit, and lift bans |
| نشرة الأخبار | Send official news and follow the server result |
| سجل العمليات | Search recent events, export CSV, and open the archive |
| التقارير | Daily, weekly, and all-time administrator activity and player hours |
| طقس القيم | Select weather and follow server confirmation |
| الإعدادات | GM limit, native log folder, and imports |

## Staff and legacy configuration

In **فريق الإدارة**, select **إضافة إداري**. Enter the correct player UUID, name, and rank, choose the required permissions, and save. You can also select a player in **اللاعبون** and choose **إعطاء صلاحيات**.

Each account has independent Enabled, GM, News, Players, Kick, Ban, and Bypass Limit switches. Rank is descriptive. The enabled switches determine the actual abilities. The ordinary GM limit is configurable from one to three and defaults to three. Staff with Bypass Limit do not consume ordinary slots.

In game, staff open ESC and choose **Admin Panel** to activate their session. The button changes to **Logout from Admin Panel** after activation. An in-game `#news` command requires an active session and News permission.

To import an older configuration, open **الإعدادات** and choose **استيراد ServerAdminTools_Config.json**. Import reads administrators, Game Masters, and bans and skips zero identities. Imported administrators receive GM, News, player-list, kick, ban, and GM-limit bypass permissions. Imported Game Masters receive GM and player-list permissions. Review the switches after import.

The manager saves `config.json` and updates `config.rev` so the addon can load the change. If another window has saved a newer revision, the manager rejects an edit based on the older revision.

## Players and bans

Search by name, UUID, or IP, or enable **المتصلون فقط** to show connected players. **تفاصيل** displays the player's identities, aliases, and first/last seen times. Unavailable metadata remains empty or is identified as unavailable.

Staff appointment and bans can use historical player records. **طرد** and **إنهاء جلسة الإدارة** require a connected player. Server command results appear in the operation log and command results.

When adding a ban, enter the UUID, name, and reason. Select **حظر دائم** for a permanent ban, or choose an expiry in the current computer's local time. The manager stores an absolute UTC expiry, which the addon uses to stop enforcement. Select the row in **الحظر**, then use **تعديل** to edit it or **فك الحظر** to lift it.

Ban requests submitted through the in-game administration window or Game Master require the manager or background agent to remain running so requests can be processed and saved. Staff permissions are checked when the request is processed. Once applied, the addon shows the reason, expiry, and instructions to contact server administration, then enforces its server-owned disconnect deadline.

## News and weather

In **نشرة الأخبار**, enter the message, select the audience and duration, and choose **إرسال خبر رسمي**. Duration is **15–180 seconds** and the message supports up to **600 characters** on one line. Choose everyone, US only, or USSR only.

Official news uses the shared BLC logo and the prefix **Server Admin says:**. Sending displays a command ID. Wait for the **نُفّذ** result to confirm server processing. A broadcast sent during the addon's three-second news cooldown may be rejected.

In **طقس القيم**, select a scenario-supported preset and choose **تطبيق الطقس**. The server applies the change immediately, and the application displays the effective weather and command outcome. Select **تلقائي حسب السيناريو** to resume the scenario's weather progression. Controls are disabled if a fresh addon status or supported weather data is unavailable.

The command queue holds **32 requests**, and the addon processes up to **four per update cycle**. Requests expire after **120 seconds**. Use the command ID and result to follow execution.

## Audit and reports

**سجل العمليات** shows the latest **800 events** with search. Export the view to CSV or open the full archive folder. The archive contains player and administration events, news, kicks, bans, and manager command outcomes.

To include Game Master operations written by the engine, select the actual server `logs` folder in **الإعدادات**. The application reads `script.log` incrementally and associates entries containing a UUID with their actor. Available detail depends on what the engine records.

In **التقارير**, select **نشاط الإداريين** for administrator activity or **ساعات اللاعبين** for player hours. Choose **يومي**, **أسبوعي**, or **كل الفترة** for daily, weekly, or all-time data. Calendar boundaries use the timezone of the computer running the manager, and weeks begin on **Monday**. All-time mode disables the date picker and uses the available archive.

Playtime is displayed as hours, minutes, and seconds. It is calculated from recorded join/leave sessions and a fresh heartbeat for a currently connected player. An old join alone cannot establish continuing connection hours. Reports indicate when the available archive starts later than the requested period.

Administration columns cover session login, GM opening, GM operations, kicks, bans, news, and other successful actions. Windows operator activity is shown separately. Queued-only and rejected requests do not increase successful-action totals.

## Additional player metadata

UUID is the persistent key for permissions and history. The temporary network-session identity is a separate field. IP address, country, external GUID, and platform account ID may need an additional server data source.

Choose **استيراد بيانات لاعب موثوقة CSV** in Settings. Supported columns are

```csv
uuid,name,platform,ip,country,guid,platformid
```

An example is included at `Source\Examples\trusted-player-metadata.csv`. Match each imported row to the player's actual UUID. The manager does not contact external country-lookup services.

## Background agent

Run the agent from the application folder to continue importing audit data and processing in-game ban requests after closing the main window

```powershell
& '.\BLCA Admin Manger.exe' --agent --profile 'D:\Reforger\ServerA\profile' --logs 'D:\Reforger\ServerA\logs'
```

Use your actual server paths. The agent appears in the Windows notification area and can be stopped from its menu. One archive-processing process is sufficient for the same profile. Permission enforcement, ban expiry, and in-game news delivery run inside the addon.

## Backups and source changes

Back up the complete `BLCAAdminSuite` folder inside the server profile, including `config.json`, `config.rev`, `audit`, and `manager-data`. Copying the application executable alone does not preserve server data.

The full source is in **Source**. Read `Source\README_EN.md` for .NET 8 build, test, and publish instructions. Building writes a new executable to its output folder. Use that output to run your changes or update the distributed application.

If the manager keeps waiting for the addon, check the selected profile, confirm BLCA News_and_Menu is running, and locate a fresh `status.json` in the same folder. If configuration is not ready, read the displayed error. If the command queue is full, check the addon connection and follow the existing request results.

**BLCA Development Team**
