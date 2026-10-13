# CDSP Helpers — создание новой фичи

Три способа получить одну и ту же тройку, как в `Samples~/Calendar`
(`<Name>Context / <Name>Domain / <Name>System / <Name>Presentation` с уже
связанными дженериками). Неймспейс везде считается как в Rider:
**ближайший `.asmdef` `rootNamespace` + относительный путь папки**,
с фолбэком на путь относительно `Assets/` (так `Calendar` и получает
`Logic.Features.Calendar`).

| Способ | Где ставится | Когда использовать |
|---|---|---|
| **A. Rider File Template** (`Rider/`) | Локально, у каждого (2 мин) | Основная работа в Rider, один файл |
| **B. Python-скаффолд** (`Scaffold/`) + Rider External Tool | Локально, у каждого (3 мин) | Нужны сразу 4 файла, Unity можно не открывать |
| **C. Unity Editor** (`Editor/`) | Нигде — уже работает из коробки | Для всей команды, из `Project window` |

`Editor/` специально лежит в папке с именем `Editor`: Unity считает такой код
только редакторным, в билд он не попадает. Остальное Unity просто хранит как текст.

---

## A. Rider File Template (рекомендуется для Rider)

Даёт пункт в том самом окне `New in This Directory`. Ограничение Rider:
**1 шаблон = 1 файл**, поэтому основной шаблон — один файл со всей четвёркой
(как `MinimalCounter`), а раскладка в 4 файла делается рефакторингом.

1. Скопируйте текст из `Rider/CDSP-Feature-SingleFile.cs.txt`.
2. Rider: `Settings (Ctrl+Alt+S) → Editor → File Templates → + → File Template`.
3. `Name: CDSP Feature`, `Extension: cs`, вставьте текст, `Apply`.
4. Использование: ПКМ по папке → `New → CDSP Feature` → введите `Inventory` →
   получите `InventoryDomain / InventorySystem / InventoryPresentation / InventoryContext`
   в `${NAMESPACE}` этой папки (Rider строит его из `asmdef` + пути сам).
5. Если нужна раскладка как `Calendar`: выделите файл →
   `Refactor → Move Types Into Matching Files` (получится 4 файла).

Дополнительно, для любителей создавать 4 файла вручную, есть
`Rider/CDSP-Context.cs.txt`, `CDSP-Domain.cs.txt`, `CDSP-System.cs.txt`,
`CDSP-Presentation.cs.txt`. Там два поля: имя файла (`InventoryContext`…)
и параметр `FEATURE` (`Inventory` — один на все 4 файла).

---

## B. Python-скаффолд + Rider External Tool

Работает без открытого Unity, неймспейс считает сам (asmdef → Assets → имя папки).

```bash
python Helpers/Scaffold/create_cdsp_feature.py Inventory --dir Assets/Logic/Features
python Helpers/Scaffold/create_cdsp_feature.py Inventory --dir Assets/Logic/Features --single-file
python Helpers/Scaffold/create_cdsp_feature.py Inventory --dir Assets/Logic/Features --namespace My.Game --force
python Helpers/Scaffold/create_cdsp_feature.py Inventory --dir Assets/Logic/Features --dry-run
```

По умолчанию создаются `Assets/Logic/Features/Inventory/` + 4 файла;
с `--single-file` — один `Assets/Logic/Features/Inventory.cs`.
Без `--force` существующие файлы не перезаписываются (выход с кодом 2).
`.meta` писать не нужно — Unity сгенерирует их при Refresh.

Чтобы вызывать из Rider (будет в контекстном меню папки):

1. `Settings → Tools → External Tools → +`.
2. `Name: CDSP Feature`, `Program: python`,
   `Arguments: "<путь-к-пакету>/Helpers/Scaffold/create_cdsp_feature.py" "$Prompt$" --dir "$FileDir$"`,
   `Working directory: $ProjectFileDir$`, включить `Open console for tool output`.
3. Использование: ПКМ по **родительской** папке (например `Features`) →
   `External Tools → CDSP Feature` → ввести имя.

Под Windows `python` обычно есть (проверено `python --version`);
нужен только Python 3.8+ из стандартной библиотеки, зависимостей нет.

---

## C. Unity Editor (без установки)

Меню уже есть после установки пакета:

1. В `Project window` выделите папку (например `Assets/Logic/Features`).
2. ПКМ → `Create → CDSP → CDSP Feature (4 files, like Calendar)...`
   или `CDSP Feature (single file)...`.
3. Введите имя (регистр первой буквы и суффиксы поправятся сами),
   неймспейс виден в превью → `Create`.

Файлы создаются через `AssetDatabase`, `.meta` и подсветка — автоматически.
