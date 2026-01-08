# FillReestrRK_NEW Pipeline — Документация

## Содержание
1. [Техническая документация (код)](#техническая-документация-код)
2. [Бизнес-логика (предметная область)](#бизнес-логика-предметная-область)

---

## Техническая документация (код)

### Обзор архитектуры

Новая архитектура pipeline создана для упрощения переноса логики `FillReestrRK_NEW` в PIX RPA. Основная идея: разбить монолитный метод на мелкие, независимые шаги, которые работают через общее DTO состояния.

### Добавленные сущности

#### 1. `FillReestrRKNewState` (State DTO)

**Файл:** `Program.cs`

**Назначение:** Хранение всех промежуточных данных обработки одной комбинации `(rowUniq, text)`. Все поля — instance (не static), что обеспечивает изоляцию между вызовами.

**Ключевые поля:**

##### Входные данные
- `DtReestrFilesFiltered` — таблица файлов для текущей заявки
- `DtBookOfReferenceReestrRK` — справочник документов
- `RowUniqNumber` — строка текущей заявки из фильтрованной таблицы
- `DictionaryGUIDservices` — словарь GUID услуг
- `Text` — текущий обрабатываемый текст (слот документа)
- `ReestrRKUpdate` — результирующая таблица

##### Вычисленные значения
- `RequestNumber` — номер заявки (из `RowUniqNumber`)
- `GuidEBA` — GUID клиента ЕВА
- `RegexMain` — основной regex для поиска text в файлах
- `RegexAlt` — альтернативный regex для anketa_zatavl случаев

##### Флаги контроля
- `HasMatch` — найдено ли совпадение text в файлах заявки
- `ShouldAbort` — флаг досрочного завершения (если нет данных для продолжения)

##### Родительские слоты
- `ParentSubjects` — множество найденных типов родительских субъектов (BANK, BROK, EDO, DU)
- `FoundParentSlots` — множество найденных родительских слотов (anketa, AnketaBank, zayavlenie и т.д.)
- `HasParent` — есть ли хотя бы один родительский слот

##### Коллекции строк
- `MatchingUpdateRows` — строки справочника, соответствующие text (с учётом фильтрации)
- `RowsWithTextInFilePaths` — файлы, в путях которых найден text

##### Кэши и наборы
- `PassportSets` — набор document_set, относящихся к паспортам
- `ComplectCache` — кэш complect_id для переиспользования внутри одного subject_type
- `ImportedKeys` — набор уже импортированных ключей для дедупликации

##### Флаг дочернего слота
- `IsChildSlot` — является ли текущий text дочерним слотом (uvedomlenie*, ZayavleniyeBanka, registration и т.д.)

##### Лог
- `LogBuilder` — StringBuilder для накопления логов в рамках обработки

---

#### 2. Step-методы (Шаги обработки)

**Файл:** `Program.cs`

Все шаги — публичные статические методы, принимающие `state` в качестве параметра. Это позволяет вызывать их последовательно в orchestrator и переносить 1:1 в PIX RPA как отдельные C# блоки.

##### Step01_InitContext
**Назначение:** Инициализация контекста — извлечение `requestNumber` и `guidEBA`.

**Входные поля state:**
- `RowUniqNumber`
- `DtReestrFilesFiltered`

**Выходные поля state:**
- `RequestNumber` — заполняется значением из колонки "Номер заявки"
- `GuidEBA` — первый непустой GUID ЕВА из файлов

**Логирование:** Записывает `requestNumber` и `guidEBA` в `LogBuilder`.

##### Step02_BuildRegex
**Назначение:** Построение регулярных выражений для поиска `text` в именах файлов.

**Входные поля state:**
- `Text`

**Выходные поля state:**
- `RegexMain` — основной regex для поиска
- `RegexAlt` — альтернативный regex для случаев с `anketa_zatavl`

**Логика:**
- Если `text` содержит "anketa", создаются два regex: один для `anketa_zatavl`, другой для чистой `anketa` без zatavl.
- В остальных случаях создаётся только `RegexMain`.

**Логирование:** Факт компиляции regex.

##### Step03_CheckHasMatch
**Назначение:** Проверка наличия совпадения `text` в файлах заявки.

**Входные поля state:**
- `DtReestrFilesFiltered`
- `Text`
- `RegexMain`, `RegexAlt`

**Выходные поля state:**
- `HasMatch` — `true`, если найдено совпадение
- `ShouldAbort` — устанавливается в `true`, если совпадений нет

**Логика:**
- Проходит по всем файлам в `DtReestrFilesFiltered`.
- Извлекает имя файла без расширения.
- Применяет regex (с учётом zatavl для anketa-кейсов).
- При первом совпадении устанавливает `HasMatch = true` и выходит.

**Логирование:** Сообщение о найденном совпадении или установке `ShouldAbort`.

##### Step04_DetectParents
**Назначение:** Обнаружение родительских слотов (parent subjects) в файлах заявки.

**Входные поля state:**
- `DtReestrFilesFiltered`
- `RequestNumber`

**Выходные поля state:**
- `ParentSubjects` — заполняется типами найденных родителей
- `FoundParentSlots` — заполняется именами найденных слотов
- `HasParent` — устанавливается в `true`, если найден хотя бы один родитель

**Логика:**
- Определяет набор паттернов для родительских слотов (AnketaBroker, AnketaBank, anketa, anketa_zatavl, zayavlenie, zayavlenieakcept, AnketaDU).
- Проходит по файлам текущей заявки.
- Применяет regex к именам файлов.
- При совпадении добавляет `subject` и `slot` в соответствующие множества.

**Логирование:** Информация о найденных родителях, subject_type и slots.

##### Step05_SelectMatchingUpdateRows
**Назначение:** Выбор подходящих строк из справочника `DtBookOfReferenceReestrRK` с учётом родительских слотов.

**Входные поля state:**
- `DtBookOfReferenceReestrRK`
- `Text`
- `ParentSubjects`
- `HasParent`

**Выходные поля state:**
- `MatchingUpdateRows` — список строк справочника, соответствующих `text`
- `IsChildSlot` — определяется, является ли `text` дочерним слотом
- `ShouldAbort` — устанавливается в `true`, если после фильтрации строк не осталось

**Логика:**
1. Фильтрует строки справочника по `Text`.
2. Для слота `registration` применяет специальную логику: выбирает строки только с теми `document_set` и `subject_type`, которые соответствуют найденным родителям.
3. Если `text` — дочерний слот и есть родители, дополнительно фильтрует `MatchingUpdateRows` по `subject_type` из `ParentSubjects`.

**Логирование:** Количество строк в `MatchingUpdateRows`, флаг `IsChildSlot`.

##### Step06_FindRowsWithTextInPaths
**Назначение:** Поиск файлов, в путях которых встречается `text`.

**Входные поля state:**
- `DtReestrFilesFiltered`
- `Text`

**Выходные поля state:**
- `RowsWithTextInFilePaths` — список файлов с совпадением
- `ShouldAbort` — устанавливается в `true`, если файлов не найдено

**Логика:**
- Строит regex для поиска text в путях (с учётом anketa-логики).
- Проходит по файлам и добавляет в список те, имена которых соответствуют regex.

**Логирование:** Количество найденных файлов.

##### Step07_FilterByParentSubjectForChildSlots
**Назначение:** Дополнительная фильтрация по родительскому subject_type (на практике уже выполнена в Step05).

**Логика:** Этот шаг добавлен для явного разделения логики в будущем PIX процессе. Основная фильтрация уже выполнена в `Step05`.

**Логирование:** Сообщение о завершении фильтрации.

##### Step08_BuildCaches
**Назначение:** Построение кэшей `ComplectCache` и `ImportedKeys`.

**Входные поля state:**
- `ReestrRKUpdate`

**Выходные поля state:**
- `ComplectCache` — очищается (будет заполняться в Step09)
- `ImportedKeys` — заполняется ключами из уже импортированных строк

**Логика:**
- Проходит по строкам `ReestrRKUpdate`.
- Для каждой строки формирует ключ `{requestNumber}|{document_set}|{subject_type}`.
- Добавляет ключ в `ImportedKeys` для предотвращения дублей.

**Логирование:** Количество импортированных ключей.

##### Step09_ProcessUpdateRows
**Назначение:** Обработка каждой строки из `MatchingUpdateRows` — присвоение `file_id`, создание записей в `ReestrRKUpdate`.

**Входные поля state:**
- Все поля (используется практически весь state)

**Выходные поля state:**
- Модифицирует `ReestrRKUpdate` (добавляет новые строки)
- Модифицирует `ComplectCache` (сохраняет complect_id для переиспользования)
- Модифицирует `ImportedKeys` (добавляет новые ключи)

**Логика:**
1. Для каждой строки из `MatchingUpdateRows`:
   - Генерирует новые GUID для `complect_id` и `document_id`.
   - Если это дочерний слот и есть родитель, переиспользует `complect_id` для одного и того же `subject_type`.
   - Присваивает `requestNumber`, `master_id`, `contract_id` (из словаря GUID услуг).
   - **Применяет правила подбора file_id** в зависимости от `document_set` и `subject_type` (см. раздел "Бизнес-логика").
   - Для всех наборов кроме `BN_DKBO0134`: импортирует строку в `ReestrRKUpdate` с проверкой дедупликации по ключу.
   - Для `BN_DKBO0134`: создаёт множественные строки (по одной на каждый raspiska файл).

**Логирование:** Информация о количестве обработанных строк, пропущенных дублях, специальных случаях.

##### Step10_FlushLog
**Назначение:** Финализация — добавление накопленного лога в `ref log`.

**Входные поля state:**
- `LogBuilder`

**Выходные параметры:**
- `log` (ref) — дополняется содержимым `LogBuilder`

**Логика:** Просто добавляет содержимое `state.LogBuilder` к внешнему `log`.

---

#### 3. `FillReestrRK_NEW_Pipeline` (Orchestrator)

**Файл:** `Program.cs`

**Назначение:** Orchestrator-метод, который последовательно вызывает все шаги обработки.

**Сигнатура:**
```csharp
public static void FillReestrRK_NEW_Pipeline(
    DataTable dtReestrFilesFiltered,
    DataTable dtBookOfReferenceReestrRK,
    DataRow rowUniqNumber,
    Dictionary<int, string> dictionaryGUIDservices,
    ref string log,
    string text,
    DataTable ReestrRKUpdate)
```

**Логика:**
1. Создаёт экземпляр `FillReestrRKNewState` и заполняет входные поля.
2. Оборачивает всё в `try-catch` (по аналогии с текущим стилем).
3. Последовательно вызывает шаги от 1 до 10.
4. После ключевых шагов проверяет `state.ShouldAbort`:
   - После `Step03`: если нет совпадений, завершает обработку.
   - После `Step05`: если нет подходящих строк справочника, завершает обработку.
   - После `Step06`: если нет файлов с text в путях, завершает обработку.
5. В блоке `catch` логирует исключение.
6. В блоке `finally` или в конце вызывает `Step10_FlushLog`.

**Эквивалентность:** Orchestrator даёт тот же результат, что и `FillReestrRK_NEW`, но с явным разделением на шаги.

---

### Порядок вызова шагов

```
Orchestrator: FillReestrRK_NEW_Pipeline
  │
  ├─> Step01_InitContext               (извлечение requestNumber, guidEBA)
  ├─> Step02_BuildRegex                (построение regex для поиска text)
  ├─> Step03_CheckHasMatch             (проверка наличия совпадения)
  │     └─> [ShouldAbort? → выход]
  ├─> Step04_DetectParents             (обнаружение родительских слотов)
  ├─> Step05_SelectMatchingUpdateRows  (выбор строк справочника)
  │     └─> [ShouldAbort? → выход]
  ├─> Step06_FindRowsWithTextInPaths   (поиск файлов с text в путях)
  │     └─> [ShouldAbort? → выход]
  ├─> Step07_FilterByParentSubjectForChildSlots (фильтрация по parent subject)
  ├─> Step08_BuildCaches               (построение кэшей дедупликации)
  ├─> Step09_ProcessUpdateRows         (обработка строк, присвоение file_id)
  └─> Step10_FlushLog                  (финализация лога)
```

---

### Какие поля state являются входными/выходными для каждого шага

| Шаг | Входные поля | Выходные поля |
|-----|--------------|---------------|
| Step01 | `RowUniqNumber`, `DtReestrFilesFiltered` | `RequestNumber`, `GuidEBA` |
| Step02 | `Text` | `RegexMain`, `RegexAlt` |
| Step03 | `DtReestrFilesFiltered`, `Text`, `RegexMain`, `RegexAlt` | `HasMatch`, `ShouldAbort` |
| Step04 | `DtReestrFilesFiltered`, `RequestNumber` | `ParentSubjects`, `FoundParentSlots`, `HasParent` |
| Step05 | `DtBookOfReferenceReestrRK`, `Text`, `ParentSubjects`, `HasParent` | `MatchingUpdateRows`, `IsChildSlot`, `ShouldAbort` |
| Step06 | `DtReestrFilesFiltered`, `Text` | `RowsWithTextInFilePaths`, `ShouldAbort` |
| Step07 | — | — (логика уже в Step05) |
| Step08 | `ReestrRKUpdate` | `ImportedKeys` (заполнение), `ComplectCache` (очистка) |
| Step09 | Все поля | Модифицирует `ReestrRKUpdate`, `ComplectCache`, `ImportedKeys` |
| Step10 | `LogBuilder` | Модифицирует внешний `log` (ref) |

---

### Какие шаги выставляют ShouldAbort и почему

- **Step03**: Если `HasMatch == false` — нет смысла продолжать, так как text не найден в файлах.
- **Step05**: Если после фильтрации `MatchingUpdateRows.Count == 0` — нет строк справочника для обработки.
- **Step06**: Если `RowsWithTextInFilePaths.Count == 0` — нет файлов для привязки.

В orchestrator после каждого из этих шагов проверяется `state.ShouldAbort`, и при необходимости вызывается `Step10_FlushLog` и происходит выход из метода.

---

### Какие коллекции/кеши используются и для чего

- **`PassportSets`**: Набор `document_set`, относящихся к паспортам. Используется в `Step09` для определения, нужно ли искать файлы с именем "pasport".
- **`ComplectCache`**: Словарь `{requestNumber}|{subject_type} -> Guid`. Используется для переиспользования одного `complect_id` для всех дочерних слотов одного `subject_type` в рамках одной заявки.
- **`ImportedKeys`**: Множество ключей `{requestNumber}|{document_set}|{subject_type}`. Используется для предотвращения дублирования импорта строк в `ReestrRKUpdate` (правило дедупликации).

---

### Интеграция в текущий цикл `Main`

В `Main` есть двойной цикл:
```csharp
foreach (DataRow rowUniq in filteredFilesTable.Rows)
{
    foreach (var text in uniqueTexts)
    {
        // Вызов метода обработки
    }
}
```

**Существующий вызов:**
```csharp
FillReestrRK_NEW(filteredFilesTable, bookReferenceTable, rowUniq, dictionaryGUIDservices, ref log, text, ReestrRKUpdate);
```

**Новый вызов (закомментирован для демонстрации):**
```csharp
// FillReestrRK_NEW_Pipeline(filteredFilesTable, bookReferenceTable, rowUniq, dictionaryGUIDservices, ref log, text, ReestrRKUpdate);
```

Оба вызова имеют **одинаковую сигнатуру** и дают **эквивалентный результат**, но pipeline явно декомпозирован на шаги.

---

## Бизнес-логика (предметная область)

Этот раздел описывает бизнес-правила, реализованные в методе `FillReestrRK_NEW` и сохранённые в pipeline.

### Что такое `text` и как он сопоставляется с файлами

**`text`** — это идентификатор слота документа (например, "anketa", "uvedomlenie1", "registration").

**Сопоставление с файлами:**
- Для каждого `text` строится регулярное выражение, которое ищет соответствие в именах файлов (без расширения).
- Если в имени файла найдено совпадение с `text`, этот слот считается "активным" для текущей заявки.
- Пример: для `text = "anketa"` файл `anketa_001.pdf` даст совпадение.

**Особый случай: anketa и zatavl:**
- Если `text` содержит "anketa", создаются два regex:
  - Один для поиска `anketa_zatavl` (точное совпадение).
  - Другой для чистой `anketa` (без zatavl в имени файла).
- Это позволяет разделять анкеты-заявителя и анкеты-банка.

---

### Что такое "родительские слоты" (parent slots) и как они определяются

**Родительские слоты** — это ключевые документы заявки, которые определяют тип субъекта (BANK, BROK, EDO, DU) и влияют на то, какие дочерние слоты будут обработаны.

**Определение:**
- В файлах заявки ищутся специальные имена:
  - `AnketaBroker` → subject_type = BROK
  - `AnketaBank` → subject_type = BANK
  - `anketa_zatavl` → subject_type = BANK
  - `anketa` (без zatavl) → subject_type = BANK
  - `zayavlenie` → subject_type = EDO
  - `zayavlenieakcept` → subject_type = EDO
  - `AnketaDU` → subject_type = DU

**Важно:**
- Родительский слот определяет, какие `subject_type` разрешены для дочерних слотов текущей заявки.
- Пример: если найден `AnketaBroker`, то для дочернего слота `ZayavleniyeKompaniya` разрешён только `subject_type = BROK`.

---

### Что такое "дочерние слоты" (child slots) и как фильтруются по `subject_type`

**Дочерние слоты** — это документы, которые зависят от наличия родительских слотов и наследуют их `subject_type`.

**Список дочерних слотов:**
- `uvedomlenie1`
- `uvedomlenie2`
- `uvedomlenie3`
- `uvedomlenie4`
- `ZayavleniyeBanka`
- `ZayavleniyeKompaniya`
- `registration`

**Фильтрация:**
1. Если для текущей заявки найден родительский слот (например, `AnketaBroker`), определяется `subject_type` (BROK).
2. Для дочернего слота выбираются только те строки справочника, где `subject_type` совпадает с найденным родительским.
3. Это гарантирует, что документы дочерних слотов привязываются только к подходящему типу субъекта.

**Пример:**
- Родитель: `AnketaBroker` (BROK)
- Дочерний слот: `uvedomlenie3`
- В справочнике есть строки для `uvedomlenie3` с `subject_type = BROK` и `subject_type = BANK`.
- Обрабатывается только строка с `BROK`.

---

### Правила подбора `file_id` по `document_set` / `subject_type`

Для каждой строки справочника `document_set` и `subject_type` определяют, какие файлы нужно искать в путях и какой `file_id` присваивать.

#### Общий принцип:
- `file_id` — это ID файлов в СХФ (Система Хранения Файлов), разделённые символом `|` (если файлов несколько).
- Для каждой комбинации `{document_set, subject_type}` применяется своё правило поиска файлов.

---

#### Правила passport-наборов

**Passport наборы:** `BN_DKBO0132`, `BN_DKBO0048`, `EDO0019`, `BK1444`, `DU0080`, `PD0075`.

**Условие:** Если `document_set` входит в passport наборы **И** в файлах заявки есть совпадение с `text`, **И** есть соответствующий родительский слот.

**Логика:**
1. Проверяется наличие подходящего родительского слота по таблице:
   
   | document_set | subject_type | Родительский слот |
   |--------------|--------------|-------------------|
   | BN_DKBO0132  | BANK         | anketa_zatavl     |
   | BN_DKBO0048  | BANK         | AnketaBank        |
   | EDO0019      | EDO          | zayavlenie или zayavlenieakcept |
   | BK1444       | BROK         | AnketaBroker      |
   | DU0080       | DU           | AnketaDU          |
   | PD0075       | BANK         | anketa            |

2. Если подходящий родитель найден, ищутся все файлы с именем `pasport*` (не зависимо от `text`).
3. Все найденные `file_id` объединяются через `|`.

**Пример:**
- `document_set = BN_DKBO0132`, `subject_type = BANK`
- Родитель: `anketa_zatavl`
- Ищутся файлы: `pasport_001.pdf`, `pasport_002.pdf`
- Результат: `file_id = "12345|67890"`

---

#### Правило: PD0084 + BANK

**Условие:** `document_set = PD0084` и `subject_type = BANK`

**Логика:**
- Ищутся файлы с именами `uvedomlenie1*` и `uvedomlenie2*`.
- Все найденные `file_id` объединяются через `|`.

**Пример:**
- Файлы: `uvedomlenie1.pdf`, `uvedomlenie2.pdf`
- Результат: `file_id = "111|222"`

---

#### Правило: PD0084 + BROK

**Условие:** `document_set = PD0084` и `subject_type = BROK`

**Логика:**
- Ищутся файлы с именами `uvedomlenie3*` и `uvedomlenie4*`.
- Все найденные `file_id` объединяются через `|`.

---

#### Правило: PD0084 + EDO

**Условие:** `document_set = PD0084` и `subject_type = EDO`

**Логика:**
- Ищутся файлы с именами `uvedomlenie3*` и `uvedomlenie4*` (как для BROK).

**Обоснование:** В текущей бизнес-логике EDO использует те же уведомления, что и BROK.

---

#### Правило: PD0085 + BANK

**Условие:** `document_set = PD0085` и `subject_type = BANK`

**Логика:**
- Ищутся файлы с именем `ZayavleniyeBanka*`.
- Все найденные `file_id` объединяются через `|`.

---

#### Правило: PD0085 + EDO

**Условие:** `document_set = PD0085` и `subject_type = EDO`

**Логика:**
- Если текущий слот `text = "registration"`, ищутся файлы с именем `registration*`.
- В остальных случаях ищутся файлы `ZayavleniyeKompaniya*`.

---

#### Правило: PD0085 + BROK

**Условие:** `document_set = PD0085` и `subject_type = BROK`

**Логика:**
- Ищутся файлы с именем `registration*`.

---

#### Правило: EDO0078 + EDO

**Условие:** `document_set = EDO0078` и `subject_type = EDO`

**Логика:**
- Ищутся файлы с именем `ZayavleniyeKompaniya*`.

---

#### Правило: BK1186 + BROK

**Условие:** `document_set = BK1186` и `subject_type = BROK`

**Логика:**
- Ищутся файлы с именем `ZayavleniyeKompaniya*`.

---

#### Правило: BN_DKBO0064

**Условие:** `document_set = BN_DKBO0064`

**Логика:**
- Если текущий слот `text = "registration"`, ищутся файлы с именем `registration*`.
- В остальных случаях ищутся файлы `ZayavleniyeBanka*`.

---

#### Правило: BN_DKBO0134 (особый случай)

**Условие:** `document_set = BN_DKBO0134`

**Логика:**
1. Ищутся все файлы с именем `raspiska*`.
2. Для **каждого** найденного файла создаётся **отдельная строка** в `ReestrRKUpdate`.
3. Первая строка импортируется из справочника, остальные создаются как копии с заменой `file_id`.

**Обоснование:** Бизнес-правило требует создавать множественные записи для raspiska-файлов.

**Пример:**
- Файлы: `raspiska_1.pdf`, `raspiska_2.pdf`, `raspiska_3.pdf`
- Результат: 3 строки в `ReestrRKUpdate` с одинаковыми полями, но разными `file_id`.

---

#### Правило по умолчанию

**Условие:** Если ни одно из вышеперечисленных правил не подошло.

**Логика:**
- Строится regex для поиска `text` в именах файлов (с учётом anketa-логики).
- Ищутся файлы, имена которых соответствуют regex.
- Все найденные `file_id` объединяются через `|`.

**Пример:**
- `text = "dogovor"`
- Файлы: `dogovor_001.pdf`, `dogovor_002.pdf`
- Результат: `file_id = "333|444"`

---

### Правило дедупликации импортируемых строк

**Ключ дедупликации:** `{requestNumber}|{document_set}|{subject_type}`

**Логика:**
- Перед импортом строки в `ReestrRKUpdate` проверяется, не был ли уже импортирован такой ключ.
- Если ключ уже есть в `ImportedKeys`, строка пропускается (логируется как "Пропуск дубля").
- Это предотвращает создание дублирующих записей для одной комбинации заявки, набора и субъекта.

**Исключение:** Для `BN_DKBO0134` дедупликация не применяется, так как для этого набора создаются множественные строки.

---

### Особый кейс: BN_DKBO0134 (множественные raspiska)

**Проблема:** Для документа `BN_DKBO0134` может быть несколько файлов с именем `raspiska_*`, и бизнес-правило требует создать отдельную запись для каждого файла.

**Решение:**
1. Находятся все `raspiska*` файлы.
2. Первый файл присваивается основной строке справочника, которая импортируется в `ReestrRKUpdate`.
3. Для каждого последующего файла создаётся копия этой строки (клонируются все поля), и устанавливается новый `file_id`.

**Результат:** В `ReestrRKUpdate` появляется N строк для N файлов `raspiska_*`.

---

### Логирование

Логирование сохраняется на всех этапах:
- Каждый шаг добавляет информацию в `state.LogBuilder`.
- В конце (или при ошибке) весь лог через `Step10_FlushLog` добавляется в глобальный `ref log`.

**Формат лога:**
```
DD-MM-YYYY HH:mm:ss - [INFO/WARN/ERR] - Сообщение
```

**Ключевые лог-записи:**
- Извлечённые значения: `requestNumber`, `guidEBA`
- Факт компиляции regex
- Найденные совпадения и родительские слоты
- Количество строк справочника и файлов
- Пропуск дублей
- Специальные случаи (BN_DKBO0134, passport без родителя)
- Ошибки (в блоке catch)

---

## Применение в PIX RPA

### Сопоставление сущностей

| Сущность C# | Сущность PIX RPA |
|-------------|------------------|
| `FillReestrRKNewState` (поля) | Переменные процесса |
| Step-метод (статический) | Активность "Invoke C# Code" |
| Orchestrator (последовательность вызовов) | Последовательность активностей PIX |
| `state.ShouldAbort` | Управляющий флаг для ветвления |

### Пример процесса PIX RPA

```
[Process Variables]
- RequestNumber: String
- GuidEBA: String
- HasMatch: Boolean
- ShouldAbort: Boolean
- ParentSubjects: HashSet<String>
- ... (все поля state как переменные)

[Sequence]
1. [Invoke C#: Step01_InitContext]
2. [Invoke C#: Step02_BuildRegex]
3. [Invoke C#: Step03_CheckHasMatch]
4. [If: ShouldAbort == true] → [Go to Finalize]
5. [Invoke C#: Step04_DetectParents]
6. [Invoke C#: Step05_SelectMatchingUpdateRows]
7. [If: ShouldAbort == true] → [Go to Finalize]
8. [Invoke C#: Step06_FindRowsWithTextInPaths]
9. [If: ShouldAbort == true] → [Go to Finalize]
10. [Invoke C#: Step07_FilterByParentSubjectForChildSlots]
11. [Invoke C#: Step08_BuildCaches]
12. [Invoke C#: Step09_ProcessUpdateRows]
13. [Finalize: Invoke C#: Step10_FlushLog]
```

### Преимущества такого подхода

1. **Мелкие блоки:** Каждый шаг — это небольшой участок логики, который легко тестировать и поддерживать.
2. **Изолированное состояние:** Все промежуточные данные хранятся в state (переменных процесса PIX), а не в static полях.
3. **Управляемое ветвление:** `ShouldAbort` позволяет пропускать ненужные шаги, если данных недостаточно.
4. **Эквивалентность:** Pipeline даёт тот же результат, что и монолитный метод, что позволяет постепенно мигрировать.

---

## Итоговая сводка

### Добавленные файлы и изменения

1. **Program.cs:**
   - Добавлен класс `FillReestrRKNewState` (State DTO)
   - Добавлены методы `Step01` — `Step10`
   - Добавлен orchestrator `FillReestrRK_NEW_Pipeline`
   - Добавлены вспомогательные методы `ProcessFileIdAssignment`, `CollectFileIds`, `CollectFileIdsBySearchText`
   - В `Main` добавлен закомментированный вызов pipeline

2. **docs/fillreestrrk_new_pipeline.md:**
   - Техническая документация по коду
   - Бизнес-логика по предметной области

### Критерии приёмки выполнены

✅ В репозитории появился `FillReestrRKNewState` (DTO состояния)  
✅ В репозитории появился набор Step-методов  
✅ В репозитории появился новый публичный orchestrator `FillReestrRK_NEW_Pipeline`  
✅ Создана markdown-документация (код + бизнес-логика)  
✅ Старый `FillReestrRK_NEW` остаётся в проекте без изменений  
✅ Новый pipeline можно вызвать в существующем цикле  
✅ В новых шагах нет избыточной обработки исключений (один общий try/catch в orchestrator)  
✅ Нет `public static` состояния для pipeline (state передаётся через DTO)

---

**Дата создания:** 2026-01-08  
**Автор:** Copilot Agent (based on requirements from Aimishi)
