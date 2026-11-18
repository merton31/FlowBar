# FlowBar 2.0 — памятка для разработки

Док для Windows на .NET 10 (WPF), одно приложение `FlowBar.exe`. Пользователь пишет по-русски: отвечать по-русски, простым языком. Прежняя панель 1.x (C# для .NET Framework) лежит в ветке `legacy-1.x`.

## Правила
- Ничего не отправлять на GitHub и не выпускать, пока пользователь сам не скажет «выпускай». Коммиты — локально.
- Каждое заметное изменение записывать в `CHANGELOG.md` (раздел невыпущенной версии). Выпуск — `tools\release.ps1 -Version 2.0.1`.
- Номера версий вида `2.0.0`: тег выпуска `v2.0.0`. Буквы (`2.0.0a`) тоже читаются обновлением: буква = четвёртое число версии. Тег без трёх чисел (`v2.0`) обновление отвергает.
- Не хранить в репозитории снимки экрана с личным содержимым (окна, файлы, переписка). Папка `.gitignore` пропускает только картинки ресурсов и `assets/preview`.
- Все видимые тексты — через `L10n.T/F` и словарь `RussianText` в `Localization.cs`. Перед выпуском: `FlowBar.exe --audit-ui <папка>` пишет `missing-translations.txt` — в нём не должно быть английских подписей, кроме названий сервисов и технических значений.

## Сборка и проверка
- Сборка для разработки: `dotnet build source\FlowBar.csproj -c Release -p:TreatWarningsAsErrors=true`.
- Выпуск одним файлом: команда из `README.md` или `tools\release.ps1`.
- Проверки принимают путь к **существующей пустой** папке и пишут в неё `result.txt` (последняя строка `OK` — успех): `--smoke`, `--audit-ui`, `--check-install`, `--check-dictation`, `--check-places`, `--check-jumplist`, `--preview-glass`, `--check-live`. Живое стекло исключает док из захвата экрана, поэтому снимки дока делайте через PrintWindow или самопроверки.
- `FlowBar.exe` без аргументов, запущенный не из `%LOCALAPPDATA%\Programs\FlowBar`, ставит себя туда, останавливает прежние панели и запускает установленную копию. Для разработки: переменная `FLOWBAR_NO_INSTALL=1`; для проверок установки: `FLOWBAR_INSTALL_DIR`.

## Карта кода (`source\`)
- `Program.cs` — вход, самопроверки; `DockWindow.cs` — док, плитки, увеличение (`MagnifyStep`), миниатюры окон (`ShowThumbnail`), живое стекло (`OnRendering`, `LiveGlass.cs`).
- `Widgets.cs`, `WidgetFaces*.cs` — каталог и лица виджетов; `Surfaces.cs`, `ExpandedSurfaces.cs` — окна и настройки; `Visuals.cs`, `ControlStyles.cs`, `DockControls.xaml` — общие элементы.
- `DockModel.cs` — профиль (`%LOCALAPPDATA%\FlowBar\profile.json`); `AppIdentity.cs` — папка данных, установка, обновление с GitHub.
- `Dictation*.cs`, `LocalVoice.cs`, `VoiceAssistant.cs` — диктовка; `AgentServer.cs` — MCP; `UnreadNotifications.cs` — счётчик уведомлений (WhatsApp).
- `Services\*.png` — логотипы сервисов для превью галереи (вшиты в exe). Пространство имён кода `LumaDock` — прежнее название проекта, оставлено намеренно.
