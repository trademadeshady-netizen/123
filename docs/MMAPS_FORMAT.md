<!-- coderabbit-ai-generated:v1 -->

# TrinityCore mmaps: заголовки и границы реализации

## Источники и отличие от упрощённого описания

Проверено по исходникам ветки TrinityCore **3.3.5**, прочитанным 2026-11-18:

- [MapDefines.h](https://github.com/TrinityCore/TrinityCore/blob/3.3.5/src/common/Collision/Maps/MapDefines.h): `MmapTileHeader`, MMAP_VERSION=15. SHA-256 прочитанного файла: `0d7514d9fd8d596269845b179b240e8cf8d7b44870e40611130277dde37d7dd4`.
- [MMapManager.cpp](https://github.com/TrinityCore/TrinityCore/blob/3.3.5/src/common/Collision/Management/MMapManager.cpp): loadMapData читает dtNavMeshParams из .mmap; loadMap — wrapper и Detour payload из .mmtile. SHA-256 `90c0cbd3a7d2a7c21a4728d17c5d677e8e39d6307660b0ebbab7b3837cfb3189`.
- [DetourNavMesh.h](https://github.com/TrinityCore/TrinityCore/blob/3.3.5/dep/recastnavigation/Detour/Include/DetourNavMesh.h): dtNavMeshParams, dtMeshHeader, DT_NAVMESH_VERSION=7. SHA-256 `201732481a638843164065b8fde938621c3e9d44fe1e067dc1cc5f602c4fbf18`.

Ссылки на ветку могут измениться; версии и хеши фиксируют прочитанную структуру. Пользовательских файлов/ревизии генератора нет, совместимость с ними **НЕ ПОДТВЕРЖДЕНА**.

В отличие от сокращённого требования «заголовок MMAP в .mmap», реальный `.mmap` содержит **28 байт dtNavMeshParams без magic/version**. Заголовок MMAP относится к `.mmtile`. Проверять выдуманную сигнатуру в .mmap означало бы отвергать файлы этого формата; реализована структура из исходника.

## Читаемые поля

Все значения little endian, float32/int32/uint32:

| Файл | Поля |
|---|---|
| `<mapId:D3>.mmap` | origin[3], tileWidth, tileHeight, maxTiles, maxPolys; ровно 28 байт |
| `<mapId:D3><x:D2><y:D2>.mmtile` | wrapper 20 байт: magic=0x4d4d4150, dtVersion, mmapVersion, payload size, usesLiquids (1 байт), padding[3] |
| Начало payload | dtMeshHeader 100 байт: magic=0x444e4156, version, x/y/layer, userId, counts полигонов/вершин/деталей/связей, offMeshBase, walkableHeight/Radius/Climb, bmin/bmax, bvQuantFactor |

Проверяются magic/версии, точная длина wrapper+payload, конечность и порядок bounds, положительные размеры/ёмкости, counts и минимальные размеры секций вершин (12 байт) и dtPoly (32 байта), дубликаты ключей тайлов/ёмкости карты. В память читается ограниченный заголовок. Секции полигонов/вершин не подменяются синтетическими; их полный разбор, индексы, связи, проекция zone XY и поиск пути делегированы `IMapNavigationBackend.LoadValidated/FindPath/HasObstacle/ToWorld`. Заголовки не являются доказательством корректности всего Detour payload. Backend должен заново проверить файл при чтении (файл мог измениться после проверки).

## Конфиг и ошибки

```json
{
  "gameVersion": "3.3.5a",
  "mapsDirectory": "mmaps",
  "maps": {
    "provider": "mmap",
    "missingData": "fail",
    "mmapVersion": 15,
    "detourVersion": 7
  }
}
```

`MapProviderFactory` применяется и WinForms, и Console. По умолчанию `fail`: отсутствие .mmap или всех .mmtile вызывает `MapDataMissingException` с полным путём, версиями и указанием mmaps_generator. `warn_and_continue` допускает открытое меню/диагностику после предупреждения; запрос навигации всё равно выдаёт ошибку. Он **не разрешает движение без карты**. Ошибки формата не скрываются политикой missingData.

При корректных заголовках и отсутствии backend поиск пути/коллизии/проекция вызывают `NotSupportedException`. Это загрузчик и точка расширения, ещё не движок Detour. mmaps описывают навигацию поверхности; безопасный полёт требует собственного источника пространственных коллизий (например совместимого vmaps backend), что нельзя вывести из отсутствия наземного полигона.

`StraightLineMapProvider` помечен «ТОЛЬКО ТЕСТЫ» и требует явного isTestEnvironment=true в коде. Конфиг `straight_line_test` без этого разрешения отклоняется фабрикой; приложения не передают разрешение и не включают его через JSON. Прямой путь не проверяет коллизии и не предназначен для игры.

## Проверки

`MapProviderTests` использует минимальные **синтетические заголовки реального layout**, а не заявленный реальный навигационный тайл. Проверяются версии, длины, counts, отсутствие файлов, политика предупреждения, фабрика и отказ тестового провайдера в проде. Совместимость с реальным набором карт требует файлов, ревизии генератора и подключённого backend; пока **ТРЕБУЕТ ЭКСПЕРИМЕНТА**.
