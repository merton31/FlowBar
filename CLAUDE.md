# FlowBar — памятка для Claude

Своя панель задач для Windows на C# WPF. Пользователь пишет по-русски, отвечать только по-русски, простым языком.

## Правила
- На GitHub (`merton31/FlowBar`) ничего не отправлять и не выпускать, пока пользователь сам не скажет «выпускай». Коммиты — локально. Про выпуск после каждой задачи не спрашивать.
- Каждое заметное изменение сразу записывать в `CHANGELOG.md` (раздел невыпущенной версии). Он встроен в exe: «Настройки → Общие → Что нового».
- Номера версий: заметные изменения — следующий номер (1.6.8), мелкие правки после выпуска — буква в конце (1.6.7a, 1.6.7b). В `FlowBar.Update.cs` буква = четвёртое число версии (a = 1, b = 2: `AssemblyVersion("1.6.7.1")`), в `CHANGELOG.md` заголовок «## 1.6.7a — …», выпуск — `tools\release.ps1 -Version 1.6.7a`.
- Планы и состояние — в `Планы.md`. Перед работой прочитать раздел «Состояние…».
- Упёрся в лимит — остановиться, не продолжать без команды.
- Не хранить снимки экрана с личным содержимым; делать их только области панели или карточки.

## Сборка и запуск
- `.\build.ps1` — встроенный csc .NET Framework 4, язык **C# 5**: нет `$""`, `?.`, `=>`-членов, `out var`, `nameof`.
- `.\FlowBar.exe --selftest` — самопроверка, должна быть без FAIL.
- `Start-Process .\FlowBar.exe` — сам ставится в `%LOCALAPPDATA%\Programs\FlowBar`, закрывает прежнюю копию и запускается (ждать ~22 с).
- Команды работающей панели: `FlowBar.exe --open <раздел>` (settings, quick, music, calendar, wifi, bluetooth, close, quit, report…), список — `OpenNames` в `FlowBar.Cards.cs`.

## Ловушки
- **Claude Desktop — пакет MSIX.** Мои команды видят подменённый `%APPDATA%`. Настоящая папка данных: `\\localhost\C$\Users\merto\AppData\Roaming\FlowBar` (settings.txt, error.log, backups).
- **Правки кода через python-скрипт в heredoc портят `\\`** (`\0`, `\b` становятся управляющими символами). Скрипт с обратными косыми писать файлом через Write (`tools\patch_*.py`, raw-строки) или использовать Edit.
- Файлы огромные, строки длинные: не читать целиком. Искать Grep, читать по 20–40 строк. Вывод команд обрезать (`cut -c1-200`).
- Панель без прав администратора (по умолчанию). С правами Windows не пускает автоматизацию и перетаскивание.
- Пока панель Windows скрыта, её значки недоступны через UI Automation.

## Карта кода
- `FlowBar.cs` — `N` (WinAPI), `Audio`, `SafeFile`, `Press`; `Bar`: настройки (`cfg`, `C()`, `SaveCfg`), окна программ (`GetWindows`, `RefreshApps`, `AppTile`, `Activate`, миниатюры `ShowPreview`), карточки (`ShowPanel`), резерв места (`Reserve`, `EnsureWorkArea`), `Taskbar`, `Program.Main`, `SelfTest`.
- `FlowBar.Design.cs` — темы и размеры, модули панели (`Tile`, большой `switch` по id модуля), сбор данных раз в секунду (`Collect`, `Tick`), сеть (`ReadConn`), музыка (`UpdateMedia`), раскладка ряда, перетаскивание, `Place`.
- `FlowBar.Cards.cs` — карточки модулей (`ShowMusic`, `ShowQuick`, `ShowWifi`…), настройки (`Settings*`), каталог, уведомления (`Notify`), `OpenByCode`.
- `FlowBar.Tray.cs` — свой трей: окно `Shell_TrayWnd`, пересылка Проводнику.
- `FlowBar.Glass.cs` — стеклянная подложка. `FlowBar.Update.cs` — версия, установка, обновление с GitHub, права администратора.
- Проверка на экране: `. scratchpad\ui.ps1` нет в репозитории — клики и снимки делать через user32 (`SetCursorPos`, `mouse_event`, `CopyFromScreen`) с `SetProcessDPIAware`. Основной монитор 2560×1440, панель внизу, y≈1402.
