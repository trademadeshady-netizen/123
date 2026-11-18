<!-- coderabbit-ai-generated:v1 -->

<!-- coderabbit-ai-generated:v1 -->

<!-- coderabbit-ai-generated-section:v1:e9358e853449e23b97e5b4c94d17ab2a0afb73ea62a913640c48cbc65b94a9da -->

<!-- coderabbit-ai-generated:v1 -->

<!-- coderabbit-ai-generated-section:v1:74623bc2b1345ba03ae7aa86e88638e2cbd498ed88f88d1dfe300c5a6996dd85 -->

# HerbBot (.NET 8)

Ядро травничества, WinForms, консольное меню и адаптеры игры/карт. [Профиль памяти и WinForms](docs/MEMORY_AND_WINFORMS.md). Точное совпадение с WRobot пока не установлено: [исследование](docs/WROBOT_BEHAVIOR.md) содержит источники, неподтверждённые правила и эксперименты. [Формат Gatherer](docs/GATHERER_FORMAT.md) описывает реальные данные файла.

<!-- coderabbit-ai-generated:v1 -->

<!-- coderabbit-ai-generated-section:v1:9e9af2c058d084080f537a1d3f96cc0b1814c3088fa08df74f78c2708eb807a6 -->

## Дополнения: профиль и диагностика

[ClientProfile и OffsetScanner](docs/CLIENT_PROFILE_3.3.5a.md). Полный профиль храните в `profiles/client.json`; подтверждённых адресов в репозитории нет. OffsetScanner проверяет профиль на запущенном Windows-клиенте и сохраняет `offset-report.json`.

WinForms регистрирует R через RegisterHotKey только на время записи (MOD_NOREPEAT). Хоткей действует при фокусе клиента, может изменить обычное действие R в игре; конфликт регистрации выводится ошибкой. Завершение записи/Стоп снимает регистрацию. Console сохраняет прежний polling. Журнал окна дублируется в `logs/herbbot-<время>.log`. Стоп сохраняет маршрут записи и прогресс, освобождает подключение памяти; для новой записи снова подключите процесс. <!-- coderabbit-ai-generated:v1 -->

<!-- coderabbit-ai-generated-section:v1:83e54ed6fdd2a4de0f99583d6284afc52217c5a4bee62a771fca2abfe2b1834a -->

Подключены MemoryGameAdapter/IObjectScanner и проверка заголовков mmaps; реальные команды ввода и backend навигации ещё требуются. Сбор в игре до их подключения завершится явной ошибкой.

<!-- /coderabbit-ai-generated:v1 -->

## Команды

<!-- /coderabbit-ai-generated:v1 -->

```sh
dotnet build
dotnet test
dotnet run --project winforms/HerbBot.WinForms.csproj # Windows
dotnet run --project app/HerbBot.Console.csproj
dotnet run --project app/HerbBot.Console.csproj -- --inspect-gatherer
```

В этой среде SDK доступен через `mise exec dotnet@8 -- dotnet ...`. Команды выполняются из корня репозитория. Конфиг `herbbot.json` создаётся при первом запуске: GameVersion=3.3.5a — версия, указанная пользователем; MapsDirectory=mmaps, GathererFile=Gatherer.lua, RoutesDirectory=routes, SearchRadius=100, LoopRoute=false, FlyingRoute=true. Все пути относительны текущему каталогу.

## Файлы

- `bot/Vector3.cs`, `MathHelper.cs`, `PathFinder.cs`, `MovementManager.cs`: математика и базовая навигация.
- `bot/IObjectManager.cs`, `IPathFinder.cs`, `IMovementManager.cs`, `GameModels.cs`: снимки мира и игровые действия.
- `bot/FarmingState.cs`, `FarmingTask.cs`, `State.cs`, `Conditions.cs`: выбор травы, сбор и конечный автомат.
- `bot/Data/`: парсер таблиц Lua, модель GatherNode и кэш JSON.
- `bot/Routes/`: точки, маршруты, атомарное сохранение, запись через позиционный адаптер и событие R.
- `bot/Runtime/`: IMapProvider, IGameAdapter, конфиг, управление маршрутом и команды меню.
- `app/Program.cs`: консоль и точка подключения конкретных адаптеров.
- `winforms/`: окно Windows с подключением профиля памяти, записью и меню маршрутов.
- `bot/Memory/`: проверяемые цепочки адресов и чтение памяти процесса только для чтения.
- `tests/HerbBot.Tests/`: xUnit, пять реальных фрагментов Gatherer, полный импорт и сценарии на подставных адаптерах.

## Меню и запись

1 — Старт выбранного маршрута; 2 — Стоп с сохранением прогресса; 3 — начало записи с именем; R — добавить текущую координату; 4 — закончить запись и сохранить; 5 — список/выбор маршрута; 6 — переключить зацикливание; 7 — импорт трав из Gatherer по коду зоны; 0 — выход. Сбор идёт в отдельном рабочем цикле, меню остаётся доступным.

Пустая запись завершается без создания файла. R не завершает запись. На Windows PollingRecordHotkey опрашивает GetAsyncKeyState каждые 50 мс в основном цикле меню: переход отпущена→нажата добавляет одну точку; удержание не дублирует запись. Опрос не забирает R у игры, поэтому игровая привязка R тоже сработает. Глобальный системный hook не нужен. На Linux используется событие консольного ввода с фокусом терминала. Во время ввода имени/выбора маршрута через ReadLine опрос приостанавливается; такие диалоги следует завершить перед записью. Очень короткое нажатие между опросами может быть пропущено. Для клиента с ограничением доступа к состоянию клавиш можно заменить IRecordHotkey мостом аддона либо адаптером платформы; на реальной Windows/WoW опрос здесь не проверялся.

Позиция берётся только из IPlayerPositionProvider.GetPosition: world XYZ, mapId, timestamp. IGameAdapter должен предоставлять согласованные снимки позиции, объектов и действий. Initialize должен быть идемпотентным; при одновременной записи/сборе реализация адаптера должна поддерживать чтение из нескольких потоков. Перед движением PrepareTravel(FlyingRoute) через игровой адаптер подготавливает маунта/полёт, в том числе после сбора; реализация должна завершаться за ограниченное время или сообщать об ошибке. Для WinForms добавлено чтение памяти по пользовательскому профилю клиентского x86 Wow.exe. Проверенные адреса и точная сборка клиента пока не предоставлены; запись в реальной игре не проверена. Координат аддона 0–1 без преобразования недостаточно для полёта.

Маршрут: `routes/<name>.json`, schemaVersion=1, name, points=[{x,y,z,mapId,timestamp}]. Это world XYZ. Имя ограничено буквами/цифрами/дефисом/подчёркиванием; Lua не изменяется. Прогресс сохраняется отдельно в `routes/progress/<name>.json`. При старте выбирается ближайшая точка текущей карты по 3D, что различает высоты в полёте. Переходы между картами не поддерживаются. Сохранённый прогресс служит журналом позиции обхода; повторный Старт снова выбирает ближайшую точку.

<!-- coderabbit-ai-generated:v1 -->

<!-- coderabbit-ai-generated-section:v1:841d5ef813696ec95bce8e0c7dad988e992f0e0a708367c84f26a3b27ff6f75a -->

## Карты и запуск

`IMapProvider` сохранён. `MmapMapProvider` читает и проверяет заголовки TrinityCore .mmap/.mmtile и передаёт навигацию `IMapNavigationBackend`; без него FindPath/HasObstacle/ToWorld явно недоступны. Подробности, исходники и конфиг: [формат mmaps](docs/MMAPS_FORMAT.md). Геометрия не выдумывается. По умолчанию maps.provider=mmap, maps.missingData=fail. warn_and_continue оставляет доступной диагностику, но не разрешает путь без данных. StraightLineMapProvider допускается только явным тестовым контекстом; UI и Console его отвергают.

WinForms после подключения JSON-профиля создаёт `MemoryObjectScanner` и `MemoryGameAdapter`. Числовые HERB entry берутся из Gatherer.lua; имя в исходнике отсутствует и не угадывается. Layout объектов, uint32 указатели, GUID, XYZ, flags/dynamicFlags описаны в профиле. [Проверка layout и OffsetScanner](docs/CLIENT_PROFILE_3.3.5a.md). Шаблон не содержит подтверждённых смещений и запрещает сбор до заполнения. Прежний позиционный профиль без objectManager позволяет запись маршрута, но не сбор. Console остаётся диагностическим меню с UnavailableGameAdapter; для подключения игры замените композицию в app/Program.cs.

### Запись → выбор → запуск в Windows

1. Из корня репозитория выполнить `dotnet run --project winforms/HerbBot.WinForms.csproj`.
2. Выбрать Wow.exe и заполненный профиль `profiles/client.json` с точной FileVersion/SHA-256. Ошибки чтения показываются в журнале.
3. Ввести имя, нажать «Запись маршрута». R через RegisterHotKey добавляет world XYZ/mapId/timestamp без остановки записи, в том числе при фокусе игры; конфликт хоткея выводится ошибкой. Нажать «Остановить запись».
4. В выпадающем списке выбрать сохранённый `routes/<name>.json` (schemaVersion=1). При необходимости включить «Зациклить маршрут».
5. После подключения реальных backend команд/навигации нажать «Старт». Worker строит путь к ближайшей точке текущей карты по 3D, обнаруживает траву через сканер в SearchRadius=100 (политика ТЗ, **не подтверждённое значение WRobot**), собирает и продолжает прерванный индекс. Перед возвращением возможен выбор следующей видимой травы. Неполный/недоступный путь — ошибка и остановка. Без backend кнопка сообщает причину отказа.
6. «Стоп» запрашивает прерывание, ждёт завершения worker, сбрасывает движение/застревание, сохраняет прогресс и освобождает память. Для нового сеанса снова подключить процесс. Удаление/замена подключения во время worker/запуска запрещены.

### Действия и ограничения

`IGameAdapter` содержит MoveTo(target, Ground/Flying), StopMovement, TargetObject, InteractObject, IsLootWindowOpen, LootAll, IsMounted, SetMounted, IsFlying. `MemoryGameAdapter` читает игрока/объекты, а команды передаёт **только** `IGameCommandBackend`. Без него выбрасывается NotSupportedException с указанием SendInput/аддона/API. Выбор и реализация реального механизма ввода — отдельный шаг. Прямой записи в память, телепортации или имитации движения в рабочем адаптере нет. Backend также предоставляет каст, инвентарь и обнаружение других игроков; отсутствие этих наблюдений не заменяется false/нулём. IgnoreIfPlayerNear по умолчанию true; отключать только осознанно в herbbot.json.

Адаптеры с маркером IExplicitLootAdapter используют Target → проверка существования → Interact → каст → ожидание окна/автолута → LootAll → подтверждение инвентарём. Старые тестовые адаптеры сохраняют IGameActions. LootTimeoutSeconds=5 — конфигурируемая политика HerbBot, не установленный таймер WRobot. Исчезновение до лута отменяет попытку; после команды лута допустимо дождаться инвентаря. Исключения сбора возвращают поиск, ошибки пути к маршруту останавливают сеанс. Логи ошибок FarmingTask поступают в панель и файл UI.

Файлы: `routes/<name>.json`, `routes/progress/<name>.json`, кэш `routes/cache/gatherer.json`, профиль `profiles/client.json`, карты `mmaps/`, лог `logs/herbbot-<время>.log`, конфиг `herbbot.json`; исходный Gatherer.lua не изменяется. Смещения, конкретная сборка Wow.exe, Windows API/хоткей и реальная игра здесь **не проверены**. mmaps отсутствуют; wManager/robotManager отсутствуют; точное поведение WRobot остаётся предметом экспериментов. World XYZ из нормализованных Gatherer XY без проекции/высоты не выводятся.

### Тесты

`dotnet build` собирает также WinForms с Windows targeting pack на Linux; запуск окна требует Windows. `dotnet test` проверяет парсер реального Gatherer, маршруты/запись R, память на мок-процессе, layout и ошибки ObjectManager, команды со сменой состояния, исчезновение объекта, таймаут лута, застревание, формат заголовков и сценарий команд меню → ближайшая точка/полёт → IObjectScanner → сбор → возврат/цикл. Моки и синтетические бинарники не подтверждают живые клиентские смещения или полноценную навигацию Detour.

<!-- /coderabbit-ai-generated:v1 -->




<!-- /coderabbit-ai-generated:v1 --><!-- /coderabbit-ai-generated:v1 -->