# FillReestrRK_NEW → PIX RPA (low-code) reference split

Эталонное разбиение метода `FillReestrRK_NEW` (файл `Program.cs`) на шаги для PIX RPA **без DTO и без конструкторов**. Исходная логика остаётся в коде, здесь — инструкция, как перенести в цепочку активностей типа «Вызов C#», опираясь только на переменные процесса.

> **Важно про `splitCode.cs`:** файл использует `static`-состояние и служебные «PartX» методы. Это *не* целевая форма для PIX RPA. Для low-code переноса используйте шаги и переменные из этого документа.

---

## A) Техническая документация

### Контракт переменных PIX RPA

| Имя переменной | Тип | Scope/жизнь | Инициализация («Присвоить значение») | Используется в шагах | Сбрасывать |
| --- | --- | --- | --- | --- | --- |
| `dtReestrFilesFiltered` | DataTable | весь процесс | уже есть (вход) | Step01–09 | нет |
| `dtBookOfReferenceReestrRK` | DataTable | весь процесс | уже есть (вход) | Step05–09 | нет |
| `rowUniqNumber` | DataRow | на каждую запись `rowUniq` | из внешнего цикла | Step01 | да, на каждую заявку |
| `dictionaryGUIDservices` | Dictionary<int,string> | весь процесс | уже есть (вход) | Step09 | нет |
| `text` | string | на каждый `text` | из внутреннего цикла | Step02–09 | да, на каждый `text` |
| `ReestrRKUpdate` | DataTable | весь прогон | уже есть (вход) | Step08–09 | **нет** |
| `log` | string | весь прогон | уже есть (вход) | Step10 | нет |
| `requestNumber` | string | на каждую заявку (`rowUniqNumber`) | Step01 | Step03–09 | да, на каждую заявку |
| `guidEBA` | string | на каждую заявку (`rowUniqNumber`) | Step01 | Step08–09 | да, на каждую заявку |
| `regexMain` | Regex | на каждый `text` | Step02 | Step03 | да, на каждый `text` |
| `regexAlt` | Regex | на каждый `text` | Step02 | Step03 | да, на каждый `text` |
| `regexFilePath` | Regex | на каждый `text` (опц.) | Step06 | Step06 | да, на каждый `text` |
| `regexSearchPassport` | Regex | на каждый `text` (опц.) | Step09 | Step09 | да, на каждый `text` |
| `hasMatch` | bool | на каждый `text` | Step03 | Step03 | да, на каждый `text` |
| `shouldAbort` | bool | на каждый `text` | Step01/03/06/07 | Step03–09 | да, на каждый `text` |
| `isChildSlot` | bool | на каждый `text` | Step07 | Step07–09 | да, на каждый `text` |
| `hasParent` | bool | на каждый `text` | Step04 | Step07–09 | да, на каждый `text` |
| `parentSubjects` | HashSet<string> | на каждый `text` | Step01/04 | Step04/07/09 | да, на каждый `text` |
| `foundParentSlots` | HashSet<string> | на каждый `text` | Step01/04 | Step04/09 | да, на каждый `text` |
| `matchingUpdateRows` | List<DataRow> | на каждый `text` | Step01/05 | Step05/07/09 | да, на каждый `text` |
| `rowsWithTextInFilePaths` | List<DataRow> | на каждый `text` | Step01/06 | Step06/07 | да, на каждый `text` |
| `passportSets` | HashSet<string> | на каждый `text` (можно держать общим) | Step08 (или заранее) | Step09 | да, на каждый `text` (как в исходной логике) |
| `complectCache` | Dictionary<string, Guid> | на каждый `text` | Step08 | Step09 | да, на каждый `text` |
| `importedKeys` | HashSet<string> | на каждый `text` | Step08 (строится из `ReestrRKUpdate`) | Step09 | да, на каждый `text` |
| `logBuilder` | StringBuilder | на каждый `text` | Step01 | Step03–10 | да, на каждый `text` |

**Правила сброса:** почти все промежуточные переменные сбрасываются на каждую итерацию `text`. `requestNumber/guidEBA` сбрасываются на каждую заявку (`rowUniqNumber`). `ReestrRKUpdate` никогда не очищается. `importedKeys` воссоздаётся из `ReestrRKUpdate` при каждом вызове (поведение исходного кода сохраняется).

### Порядок шагов и ранние выходы
1. **Step01_InitContext** — подготовка контекста и обнуление коллекций.
2. **Step02_InitRegexForText** — сбор regex по `text`.
3. **Step03_CheckHasMatch** — проверка наличия файла с `text`; если нет → `shouldAbort = true`.
4. **Step04_DetectParents** — поиск родительских слотов; заполняет `parentSubjects`/`foundParentSlots`.
5. **Step05_SelectMatchingUpdateRows** — выбор строк справочника по `text`, спец-логика `registration`.
6. **Step06_FindRowsWithTextInFilePaths** — проверка наличия файлов по regex; если нет → `shouldAbort = true`.
7. **Step07_DetectIsChildSlot_And_FilterByParentSubject** — отметка дочернего слота и фильтр по родителю; если пусто → `shouldAbort = true`.
8. **Step08_PrepareCaches** — подготовка `passportSets`, `complectCache`, `importedKeys`.
9. **Step09_ProcessMatchingUpdateRows** — основная обработка: GUID, file_id по правилам, дедупликация, спец-кейс `BN_DKBO0134`.
10. **Step10_FlushLog** — перенос буфера лога в `log`.

Точки раннего выхода: `shouldAbort` после Step03/06/07, а также внутри Step09 при отсутствии файлов или дублях.

### Шаблон процесса (где вызывать шаги)
- Внешний цикл: `foreach (DataRow rowUniq in dtReestrFilesFiltered.Rows)` → задаёт `rowUniqNumber`.
- Внутренний цикл: `foreach (var text in uniqueTexts)` → задаёт текущий слот `text`.
- Для каждого `text` вызвать **последовательно** Step01 → Step10 (без вложенных вызовов и без DTO). Проверять `shouldAbort` после Step07 и внутри Step09.

---

## B) Бизнес-документация (правила предметной области)
- `text` — имя слота/документа, ищется в имени файла (`Path.GetFileNameWithoutExtension`) регулярным выражением, допускающим суффиксы `\d{1,3}` и, для `anketa*`, альтернативную форму `_zatavl`.
- **Родительские слоты** (ищутся в файлах заявки): `AnketaBroker`→BROK, `AnketaBank`→BANK, `anketa_zatavl`→BANK, `anketa` (строго без `_something`)→BANK, `zayavlenieakcept`→EDO, `zayavlenie`→EDO, `AnketaDU`→DU. Все совпадения собираются в `parentSubjects` и `foundParentSlots`.
- **Дочерние слоты**: `uvedomlenie1/2/3/4`, `ZayavleniyeBanka`, `ZayavleniyeKompaniya`, `registration`. При наличии родителя — фильтруются по `subject_type ∈ parentSubjects`.
- **registration c родителем**: BANK → `BN_DKBO0064/BANK`; BROK → `PD0085/BROK`; EDO (`zayavlenie`|`zayavlenieakcept`) → `PD0085/EDO`. Другие комбинации не допускаются.
- **Правила поиска `file_id`** (приоритеты сохранены):
  - Passport-наборы (`BN_DKBO0132`, `BN_DKBO0048`, `EDO0019`, `BK1444`, `DU0080`, `PD0075`) — только если есть файл с `text` и найден соответствующий родительский слот: `anketa_zatavl`→`BN_DKBO0132/BANK`, `AnketaBank`→`BN_DKBO0048/BANK`, `zayavlenie|zayavlenieakcept`→`EDO0019/EDO`, `AnketaBroker`→`BK1444/BROK`, `AnketaDU`→`DU0080/DU`, `anketa`→`PD0075/BANK`.
  - `PD0084/BANK` → файлы `uvedomlenie1` + `uvedomlenie2`; `PD0084/BROK` и `PD0084/EDO` → `uvedomlenie3` + `uvedomlenie4`.
  - `PD0085/BANK` → `ZayavleniyeBanka`; `PD0085/BROK` → `registration`; `PD0085/EDO` → `registration` (или `ZayavleniyeKompaniya` при вызове не с registration).
  - `EDO0078/EDO` → `ZayavleniyeKompaniya`; `BK1186/BROK` → `ZayavleniyeKompaniya`; `BN_DKBO0064/BANK` → `registration`/`ZayavleniyeBanka`.
  - `BN_DKBO0134` (расписка) — MultipleRows: отдельная строка на каждый `raspiska*` файл.
  - Остальные — regex по `text` (с анкетным исключением `_zatavl`).
- Дедупликация в `ReestrRKUpdate` по ключу `{requestNumber}|{document_set}|{subject_type}`. Ключи собираются из текущего `ReestrRKUpdate` перед обработкой каждого `text`.

---

## Step-блоки для PIX («Вызов C#»)

### Step01_InitContext
### Входные переменные
`dtReestrFilesFiltered`, `rowUniqNumber`, `log` (строка), все промежуточные переменные из контракта.
### Выходные переменные
`requestNumber`, `guidEBA`, `logBuilder`, `hasMatch`, `shouldAbort`, `isChildSlot`, `hasParent`, коллекции очищены.
### Код (вставляется в активность "Вызов C#")
```csharp
// STEP01_InitContext
logBuilder = new StringBuilder();
shouldAbort = false;
hasMatch = false;
isChildSlot = false;
hasParent = false;

requestNumber = rowUniqNumber?["Номер заявки"]?.ToString();
guidEBA = null;

if (dtReestrFilesFiltered != null)
{
    foreach (DataRow row in dtReestrFilesFiltered.Rows)
    {
        var g = row["GUID ЕВА клиента"]?.ToString();
        if (!string.IsNullOrEmpty(g))
        {
            guidEBA = g;
            break;
        }
    }
}

parentSubjects = parentSubjects ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
parentSubjects.Clear();
foundParentSlots = foundParentSlots ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
foundParentSlots.Clear();

matchingUpdateRows = matchingUpdateRows ?? new List<DataRow>();
matchingUpdateRows.Clear();
rowsWithTextInFilePaths = rowsWithTextInFilePaths ?? new List<DataRow>();
rowsWithTextInFilePaths.Clear();
```

### Step02_InitRegexForText
### Входные переменные
`text`, `regexMain`, `regexAlt`, `shouldAbort`.
### Выходные переменные
`regexMain`, `regexAlt`, `shouldAbort`.
### Код (вставляется в активность "Вызов C#")
```csharp
// STEP02_InitRegexForText
regexMain = null;
regexAlt = null;

if (string.IsNullOrEmpty(text))
{
    shouldAbort = true;
}
else if (text.Contains("anketa", StringComparison.Ordinal))
{
    regexAlt = new Regex($@"(^|[_\s])({Regex.Escape(text)}_zatavl)(\d{{1,3}})?($|[_\s])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    regexMain = new Regex($@"(^|[_\s])(?!.*\bzatavl\b)({Regex.Escape(text)})(\d{{1,3}})?($|[_\s])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
}
else
{
    regexMain = new Regex($@"(^|[_\s])({Regex.Escape(text)})(\d{{1,3}})?($|[_\s])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
}
```

### Step03_CheckHasMatch
### Входные переменные
`dtReestrFilesFiltered`, `text`, `regexMain`, `regexAlt`, `shouldAbort`, `logBuilder`.
### Выходные переменные
`hasMatch`, `shouldAbort`, `logBuilder`.
### Код (вставляется в активность "Вызов C#")
```csharp
// STEP03_CheckHasMatch
hasMatch = false;
if (shouldAbort || dtReestrFilesFiltered == null || string.IsNullOrEmpty(text))
{
    shouldAbort = true;
}
else
{
    foreach (DataRow fileRow in dtReestrFilesFiltered.Rows)
    {
        var path = fileRow["Путь к файлу"]?.ToString();
        if (string.IsNullOrEmpty(path)) continue;
        var fileName = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(fileName)) continue;

        bool isMatch = false;
        if (regexMain != null && regexAlt != null)
        {
            isMatch = fileName.IndexOf("zatavl", StringComparison.OrdinalIgnoreCase) >= 0
                ? regexAlt.IsMatch(fileName)
                : regexMain.IsMatch(fileName);
        }
        else if (regexMain != null)
        {
            isMatch = regexMain.IsMatch(fileName);
        }

        if (isMatch)
        {
            hasMatch = true;
            logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - hasMatch: '{text}' найден в '{fileName}'");
            break;
        }
    }

    if (!hasMatch) shouldAbort = true;
}
```

### Step04_DetectParents
### Входные переменные
`dtReestrFilesFiltered`, `requestNumber`, `parentSubjects`, `foundParentSlots`, `shouldAbort`.
### Выходные переменные
`parentSubjects`, `foundParentSlots`, `hasParent`, `logBuilder`.
### Код (вставляется в активность "Вызов C#")
> Шаблон Anchored оставлен внутри шага, чтобы Step был самодостаточным; при желании можно вынести его в отдельную переменную процесса.
```csharp
// STEP04_DetectParents
if (shouldAbort) return;

parentSubjects.Clear();
foundParentSlots.Clear();

// Локальная Anchored оставлена в шаге, чтобы блок был самодостаточным в PIX.
Regex Anchored(string token, bool excludeZatavl = false)
{
    var safe = Regex.Escape(token);
    var negative = excludeZatavl ? "(?!.*zatavl\\w*)" : string.Empty;
    return new Regex($@"(^|[_\s]){negative}{safe}(\d{{1,3}})?($|[_\s])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
}

var parentPatterns = new (string subject, string slot, Regex pattern)[]
{
    ("BROK", "AnketaBroker",    Anchored("AnketaBroker")),
    ("BANK", "AnketaBank",      Anchored("AnketaBank")),
    ("BANK", "anketa_zatavl",   Anchored("anketa_zatavl")),
    ("BANK", "anketa",          new Regex($@"(^|[_\s])anketa(?![_a-zA-Z])(\d{{1,3}})?($|[_\s])", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
    ("EDO",  "zayavlenieakcept",Anchored("zayavlenieakcept")),
    ("EDO",  "zayavlenie",      Anchored("zayavlenie")),
    ("DU",   "AnketaDU",        Anchored("AnketaDU")),
};

foreach (DataRow r in dtReestrFilesFiltered.Rows)
{
    if (!string.Equals(r["Номер заявки"]?.ToString(), requestNumber, StringComparison.Ordinal)) continue;
    var name = Path.GetFileNameWithoutExtension(r["Путь к файлу"]?.ToString() ?? string.Empty);
    if (string.IsNullOrEmpty(name)) continue;

    foreach (var (subject, slot, pattern) in parentPatterns)
    {
        if (pattern.IsMatch(name))
        {
            parentSubjects.Add(subject);
            foundParentSlots.Add(slot);
        }
    }
}

hasParent = parentSubjects.Count > 0;
logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Родитель найден: {hasParent}, subject_type={string.Join(',', parentSubjects.DefaultIfEmpty("-"))}, slots={string.Join(',', foundParentSlots.DefaultIfEmpty("-"))}");
```

### Step05_SelectMatchingUpdateRows
### Входные переменные
`dtBookOfReferenceReestrRK`, `text`, `parentSubjects`, `matchingUpdateRows`, `shouldAbort`.
### Выходные переменные
`matchingUpdateRows`.
### Код (вставляется в активность "Вызов C#")
```csharp
// STEP05_SelectMatchingUpdateRows
if (shouldAbort) return;

matchingUpdateRows.Clear();

foreach (DataRow updateRow in dtBookOfReferenceReestrRK.Rows)
{
    var rowText = updateRow["Текст"]?.ToString();
    if (string.IsNullOrEmpty(rowText) || !rowText.Equals(text, StringComparison.OrdinalIgnoreCase))
        continue;

    if (text.Equals("registration", StringComparison.OrdinalIgnoreCase))
    {
        var docSet = updateRow["document_set"]?.ToString()?.Trim();
        var subject = updateRow["subject_type"]?.ToString()?.Trim();

        var isBankParent = parentSubjects.Contains("BANK");
        var isBrokParent = parentSubjects.Contains("BROK");
        var isEdoParent  = parentSubjects.Contains("EDO");

        if (isBankParent && docSet == "BN_DKBO0064" && subject == "BANK") { matchingUpdateRows.Add(updateRow); continue; }
        if (isBrokParent && docSet == "PD0085" && subject == "BROK")      { matchingUpdateRows.Add(updateRow); continue; }
        if (isEdoParent  && docSet == "PD0085" && subject == "EDO")       { matchingUpdateRows.Add(updateRow); continue; }

        continue; // нет подходящего родителя — пропустить
    }

    matchingUpdateRows.Add(updateRow);
}
```

### Step06_FindRowsWithTextInFilePaths
### Входные переменные
`dtReestrFilesFiltered`, `text`, `rowsWithTextInFilePaths`, `shouldAbort`.
### Выходные переменные
`rowsWithTextInFilePaths`, `shouldAbort`.
### Код (вставляется в активность "Вызов C#")
```csharp
// STEP06_FindRowsWithTextInFilePaths
if (shouldAbort) return;

rowsWithTextInFilePaths.Clear();

Regex regexFilePath = text.Contains("anketa")
    ? new Regex($@"(?!.*zatavl\w*)({Regex.Escape(text)})(\d{{1,3}})?", RegexOptions.IgnoreCase | RegexOptions.Compiled)
    : new Regex($@"(^|[_\s])({Regex.Escape(text)})(\d{{1,3}})?($|[_\s])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

foreach (DataRow filteredRow in dtReestrFilesFiltered.Rows)
{
    var rowText = filteredRow["Путь к файлу"]?.ToString();
    if (string.IsNullOrEmpty(rowText)) continue;
    string fileName = Path.GetFileNameWithoutExtension(rowText);
    if (regexFilePath.IsMatch(fileName))
        rowsWithTextInFilePaths.Add(filteredRow);
}

if (rowsWithTextInFilePaths.Count == 0) shouldAbort = true;
```

### Step07_DetectIsChildSlot_And_FilterByParentSubject
### Входные переменные
`text`, `matchingUpdateRows`, `parentSubjects`, `hasParent`, `shouldAbort`.
### Выходные переменные
`isChildSlot`, `matchingUpdateRows`, `shouldAbort`, `logBuilder`.
### Код (вставляется в активность "Вызов C#")
```csharp
// STEP07_DetectIsChildSlot_And_FilterByParentSubject
if (shouldAbort) return;

isChildSlot =
    text.Equals("uvedomlenie1", StringComparison.OrdinalIgnoreCase) ||
    text.Equals("uvedomlenie2", StringComparison.OrdinalIgnoreCase) ||
    text.Equals("uvedomlenie3", StringComparison.OrdinalIgnoreCase) ||
    text.Equals("uvedomlenie4", StringComparison.OrdinalIgnoreCase) ||
    text.Equals("ZayavleniyeBanka", StringComparison.OrdinalIgnoreCase) ||
    text.Equals("ZayavleniyeKompaniya", StringComparison.OrdinalIgnoreCase) ||
    text.Equals("registration", StringComparison.OrdinalIgnoreCase);

if (isChildSlot && hasParent)
{
    matchingUpdateRows = matchingUpdateRows
        .Where(row => parentSubjects.Contains(row["subject_type"]?.ToString()?.Trim() ?? string.Empty))
        .ToList();

    logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - matchingUpdateRows filtered by parent subject, rows={matchingUpdateRows.Count}");

    if (matchingUpdateRows.Count == 0) shouldAbort = true;
}
```

### Step08_PrepareCaches
### Входные переменные
`ReestrRKUpdate`, `passportSets`, `complectCache`, `importedKeys`, `shouldAbort`.
### Выходные переменные
`passportSets`, `complectCache`, `importedKeys`.
### Код (вставляется в активность "Вызов C#")
```csharp
// STEP08_PrepareCaches
if (shouldAbort) return;

passportSets = passportSets ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
passportSets.Clear();
foreach (var code in new[] { "BN_DKBO0132", "BN_DKBO0048", "EDO0019", "BK1444", "DU0080", "PD0075" })
    passportSets.Add(code);

if (complectCache == null) complectCache = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
else complectCache.Clear();

importedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
if (ReestrRKUpdate != null)
{
    foreach (DataRow ex in ReestrRKUpdate.Rows)
    {
        var req = ex["Номер заявки"]?.ToString()?.Trim() ?? string.Empty;
        var ds = ex["document_set"]?.ToString()?.Trim() ?? string.Empty;
        var st = ex["subject_type"]?.ToString()?.Trim() ?? string.Empty;
        importedKeys.Add($"{req}|{ds}|{st}");
    }
}
```

### Step09_ProcessMatchingUpdateRows
### Входные переменные
Все процессные переменные: `dtReestrFilesFiltered`, `dictionaryGUIDservices`, `matchingUpdateRows`, `requestNumber`, `guidEBA`, `text`, `parentSubjects`, `foundParentSlots`, `hasParent`, `isChildSlot`, `passportSets`, `complectCache`, `importedKeys`, `ReestrRKUpdate`, `shouldAbort`, `logBuilder`.
### Выходные переменные
Заполненные поля в `matchingUpdateRows`, новые строки в `ReestrRKUpdate`, обновлённые `complectCache`/`importedKeys`, дополненный `logBuilder`.
### Код (вставляется в активность "Вызов C#")
> Обратите внимание: строка поиска `pasport` сохранена в точной орфографии исходных файлов и кода.
```csharp
// STEP09_ProcessMatchingUpdateRows
if (shouldAbort) return;
// Нет подходящих строк справочника — дальше делать нечего.
if (matchingUpdateRows == null || matchingUpdateRows.Count == 0) return;

foreach (var updRow in matchingUpdateRows)
{
    var complectId = Guid.NewGuid();
    var documentId = Guid.NewGuid();

    updRow["Номер заявки"] = requestNumber;

    var documentSet = updRow["document_set"]?.ToString();
    var subjectType = updRow["subject_type"]?.ToString();
    var normalizedSubject = subjectType?.Trim();

    if (isChildSlot && hasParent && !string.IsNullOrEmpty(normalizedSubject) && parentSubjects.Contains(normalizedSubject))
    {
        var complectKey = $"{requestNumber}|{normalizedSubject}";
        if (!complectCache.TryGetValue(complectKey, out var existing))
            complectCache[complectKey] = complectId;
        else
            complectId = existing;
    }

    updRow["complect_id"] = complectId;
    updRow["document_id"] = documentId;
    updRow["master_id"] = guidEBA;

    var guidServiceNumber = updRow["GUID услуги"]?.ToString();
    if (int.TryParse(guidServiceNumber, out var serviceNumber) && dictionaryGUIDservices.TryGetValue(serviceNumber, out var guidService))
        updRow["contract_id"] = guidService;

    Regex regexSearchPassport = text.Contains("anketa")
        ? new Regex($@"(?!.*zatavl\w*)({Regex.Escape(text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])", RegexOptions.IgnoreCase | RegexOptions.Compiled)
        : new Regex($@"(^|[_\s])({Regex.Escape(text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    bool hasTextFile = dtReestrFilesFiltered.AsEnumerable().Any(r =>
        r["Номер заявки"]?.ToString() == requestNumber &&
        !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
        regexSearchPassport.IsMatch(Path.GetFileNameWithoutExtension(r["Путь к файлу"].ToString())));

    List<string> fileIds = null;
    string searchText = null;

    if (passportSets.Contains(documentSet) && hasTextFile)
    {
        // Жёстко задано в шаге, чтобы не требовать внешней конфигурации в PIX.
        var passportParentRules = new (string ParentSlot, string DocumentSet, string SubjectType)[]
        {
            ("anketa_zatavl", "BN_DKBO0132", "BANK"),
            ("AnketaBank",    "BN_DKBO0048", "BANK"),
            ("zayavlenie",    "EDO0019",     "EDO"),
            ("zayavlenieakcept","EDO0019",   "EDO"),
            ("AnketaBroker",  "BK1444",      "BROK"),
            ("AnketaDU",      "DU0080",      "DU"),
            ("anketa",        "PD0075",      "BANK"),
        };

        var rule = passportParentRules.FirstOrDefault(r =>
            r.DocumentSet.Equals(documentSet?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrEmpty(r.SubjectType) || r.SubjectType.Equals(normalizedSubject, StringComparison.OrdinalIgnoreCase)) &&
            foundParentSlots.Contains(r.ParentSlot));

        if (rule.DocumentSet == null)
        {
            logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Пропуск {documentSet} ({subjectType}) для {requestNumber}: нет родительского слота для passport.");
        }
        else
        {
            // Орфография searchText соответствует исходным именам файлов.
            searchText = "pasport";
            fileIds = new List<string>();
            foreach (DataRow r in dtReestrFilesFiltered.Rows)
            {
                if (r["Номер заявки"]?.ToString() == requestNumber &&
                    !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                    r["Путь к файлу"].ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    r["ID файла в СХФ"]?.ToString() != "error")
                {
                    var id = r["ID файла в СХФ"]?.ToString();
                    if (!string.IsNullOrEmpty(id) && !fileIds.Contains(id))
                        fileIds.Add(id);
                }
            }
            if (fileIds.Count > 0)
                updRow["file_id"] = string.Join("|", fileIds);
        }
    }
    else if (documentSet?.Trim() == "PD0084" && normalizedSubject == "BANK")
    {
        var files1 = new List<string>();
        var files2 = new List<string>();
        foreach (DataRow r in dtReestrFilesFiltered.Rows)
        {
            if (r["Номер заявки"]?.ToString() == requestNumber &&
                !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                r["ID файла в СХФ"]?.ToString() != "error")
            {
                var path = r["Путь к файлу"].ToString();
                var id = r["ID файла в СХФ"]?.ToString();
                if (!string.IsNullOrEmpty(id))
                {
                    if (path.IndexOf("uvedomlenie1", StringComparison.OrdinalIgnoreCase) >= 0 && !files1.Contains(id)) files1.Add(id);
                    if (path.IndexOf("uvedomlenie2", StringComparison.OrdinalIgnoreCase) >= 0 && !files2.Contains(id)) files2.Add(id);
                }
            }
        }
        var allFiles = files1.Union(files2).ToList();
        if (allFiles.Count > 0) updRow["file_id"] = string.Join("|", allFiles);
    }
    else if (documentSet?.Trim() == "PD0084" && (normalizedSubject == "BROK" || normalizedSubject == "EDO"))
    {
        var files3 = new List<string>();
        var files4 = new List<string>();
        foreach (DataRow r in dtReestrFilesFiltered.Rows)
        {
            if (r["Номер заявки"]?.ToString() == requestNumber &&
                !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                r["ID файла в СХФ"]?.ToString() != "error")
            {
                var path = r["Путь к файлу"].ToString();
                var id = r["ID файла в СХФ"]?.ToString();
                if (!string.IsNullOrEmpty(id))
                {
                    if (path.IndexOf("uvedomlenie3", StringComparison.OrdinalIgnoreCase) >= 0 && !files3.Contains(id)) files3.Add(id);
                    if (path.IndexOf("uvedomlenie4", StringComparison.OrdinalIgnoreCase) >= 0 && !files4.Contains(id)) files4.Add(id);
                }
            }
        }
        var allFiles = files3.Union(files4).ToList();
        if (allFiles.Count > 0) updRow["file_id"] = string.Join("|", allFiles);
    }
    else if (documentSet?.Trim() == "PD0085" && normalizedSubject == "BANK")
    {
        searchText = "ZayavleniyeBanka";
        fileIds = new List<string>();
        foreach (DataRow r in dtReestrFilesFiltered.Rows)
        {
            if (r["Номер заявки"]?.ToString() == requestNumber &&
                !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                r["Путь к файлу"].ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 &&
                r["ID файла в СХФ"]?.ToString() != "error")
            {
                var id = r["ID файла в СХФ"]?.ToString();
                if (!string.IsNullOrEmpty(id) && !fileIds.Contains(id))
                    fileIds.Add(id);
            }
        }
        if (fileIds.Count > 0) updRow["file_id"] = string.Join("|", fileIds);
    }
    else if (documentSet?.Trim() == "PD0085" && normalizedSubject == "EDO")
    {
        searchText = text.Equals("registration", StringComparison.OrdinalIgnoreCase) ? "registration" : "ZayavleniyeKompaniya";
        fileIds = new List<string>();
        foreach (DataRow r in dtReestrFilesFiltered.Rows)
        {
            if (r["Номер заявки"]?.ToString() == requestNumber &&
                !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                r["Путь к файлу"].ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 &&
                r["ID файла в СХФ"]?.ToString() != "error")
            {
                var id = r["ID файла в СХФ"]?.ToString();
                if (!string.IsNullOrEmpty(id) && !fileIds.Contains(id))
                    fileIds.Add(id);
            }
        }
        if (fileIds.Count > 0) updRow["file_id"] = string.Join("|", fileIds);
    }
    else if (documentSet?.Trim() == "PD0085" && normalizedSubject == "BROK")
    {
        searchText = "registration";
        fileIds = new List<string>();
        foreach (DataRow r in dtReestrFilesFiltered.Rows)
        {
            if (r["Номер заявки"]?.ToString() == requestNumber &&
                !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                r["Путь к файлу"].ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 &&
                r["ID файла в СХФ"]?.ToString() != "error")
            {
                var id = r["ID файла в СХФ"]?.ToString();
                if (!string.IsNullOrEmpty(id) && !fileIds.Contains(id))
                    fileIds.Add(id);
            }
        }
        if (fileIds.Count > 0) updRow["file_id"] = string.Join("|", fileIds);
    }
    else if (documentSet?.Trim() == "EDO0078" && normalizedSubject == "EDO")
    {
        searchText = "ZayavleniyeKompaniya";
        fileIds = new List<string>();
        foreach (DataRow r in dtReestrFilesFiltered.Rows)
        {
            if (r["Номер заявки"]?.ToString() == requestNumber &&
                !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                r["Путь к файлу"].ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 &&
                r["ID файла в СХФ"]?.ToString() != "error")
            {
                var id = r["ID файла в СХФ"]?.ToString();
                if (!string.IsNullOrEmpty(id) && !fileIds.Contains(id))
                    fileIds.Add(id);
            }
        }
        if (fileIds.Count > 0) updRow["file_id"] = string.Join("|", fileIds);
    }
    else if (documentSet?.Trim() == "BK1186" && normalizedSubject == "BROK")
    {
        searchText = "ZayavleniyeKompaniya";
        fileIds = new List<string>();
        foreach (DataRow r in dtReestrFilesFiltered.Rows)
        {
            if (r["Номер заявки"]?.ToString() == requestNumber &&
                !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                r["Путь к файлу"].ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 &&
                r["ID файла в СХФ"]?.ToString() != "error")
            {
                var id = r["ID файла в СХФ"]?.ToString();
                if (!string.IsNullOrEmpty(id) && !fileIds.Contains(id))
                    fileIds.Add(id);
            }
        }
        if (fileIds.Count > 0) updRow["file_id"] = string.Join("|", fileIds);
    }
    else if (documentSet?.Trim() == "BN_DKBO0064")
    {
        searchText = text.Equals("registration", StringComparison.OrdinalIgnoreCase) ? "registration" : "ZayavleniyeBanka";
        fileIds = new List<string>();
        foreach (DataRow r in dtReestrFilesFiltered.Rows)
        {
            if (r["Номер заявки"]?.ToString() == requestNumber &&
                !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                r["Путь к файлу"].ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 &&
                r["ID файла в СХФ"]?.ToString() != "error")
            {
                var id = r["ID файла в СХФ"]?.ToString();
                if (!string.IsNullOrEmpty(id) && !fileIds.Contains(id))
                    fileIds.Add(id);
            }
        }
        if (fileIds.Count > 0) updRow["file_id"] = string.Join("|", fileIds);
    }
    else if (documentSet?.Trim() == "BN_DKBO0134")
    {
        logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - ОБРАБОТКА documentSet == BN_DKBO0134");
        var raspiskaFileIds = new List<string>();
        foreach (DataRow r in dtReestrFilesFiltered.Rows)
        {
            if (r["Номер заявки"]?.ToString() == requestNumber &&
                !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                r["Путь к файлу"].ToString().IndexOf("raspiska", StringComparison.OrdinalIgnoreCase) >= 0 &&
                r["ID файла в СХФ"]?.ToString() != "error")
            {
                var id = r["ID файла в СХФ"]?.ToString();
                if (!string.IsNullOrEmpty(id) && !raspiskaFileIds.Contains(id))
                    raspiskaFileIds.Add(id);
            }
        }

        if (raspiskaFileIds.Count > 0)
        {
            updRow["file_id"] = raspiskaFileIds[0];
            ReestrRKUpdate.ImportRow(updRow);

            for (int i = 1; i < raspiskaFileIds.Count; i++)
            {
                var newRow = ReestrRKUpdate.NewRow();
                foreach (DataColumn col in updRow.Table.Columns)
                {
                    if (ReestrRKUpdate.Columns.Contains(col.ColumnName))
                        newRow[col.ColumnName] = updRow[col.ColumnName];
                }
                newRow["file_id"] = raspiskaFileIds[i];
                ReestrRKUpdate.Rows.Add(newRow);
            }
        }
        continue;
    }
    else
    {
        var regexSearch = text.Contains("anketa")
            ? new Regex($@"(?!.*zatavl\w*)({Regex.Escape(text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])", RegexOptions.IgnoreCase | RegexOptions.Compiled)
            : new Regex($@"(^|[_\s])({Regex.Escape(text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        fileIds = new List<string>();
        foreach (DataRow r in dtReestrFilesFiltered.Rows)
        {
            if (r["Номер заявки"]?.ToString() != requestNumber ||
                string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) ||
                r["ID файла в СХФ"]?.ToString() == "error")
                continue;

            var fileName = Path.GetFileNameWithoutExtension(r["Путь к файлу"].ToString());
            if (regexSearch.IsMatch(fileName))
            {
                var id = r["ID файла в СХФ"]?.ToString();
                if (!string.IsNullOrEmpty(id) && !fileIds.Contains(id))
                    fileIds.Add(id);
            }
        }
        if (fileIds.Count > 0) updRow["file_id"] = string.Join("|", fileIds);
    }

    if (documentSet != "BN_DKBO0134" && !string.IsNullOrEmpty(updRow["file_id"]?.ToString()))
    {
        var key = $"{requestNumber}|{documentSet?.Trim()}|{subjectType?.Trim()}";
        if (importedKeys.Add(key))
        {
            ReestrRKUpdate.ImportRow(updRow);
        }
        else
        {
            logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Пропуск дубля для ключа {key}");
        }
    }
}
```

### Step10_FlushLog
### Входные переменные
`logBuilder`, `log`.
### Выходные переменные
`log`.
### Код (вставляется в активность "Вызов C#")
```csharp
// STEP10_FlushLog
log = log + Environment.NewLine + logBuilder.ToString();
```

---

## Минимальные напоминания для переноса
- Не создавайте DTO/классов состояния и не вызывайте шаги друг из друга — каждый Step самостоятельный.
- Держите порядок вызовов и проверки `shouldAbort`.
- `ReestrRKUpdate` не очищается между итерациями; `importedKeys` всегда строится заново из него.
- При необходимости инициализировать Regex/HashSet/Dictionary в PIX — используйте «Присвоить значение» перед Step.
- Шаблон Anchored и массив `passportParentRules` оставлены прямо в шагах для самодостаточности; при необходимости их можно вынести в общие переменные процесса.
- Regex для anketa/обычных слотов повторяется в Step02/Step06/Step09; при изменении обновляйте все три или вынесите единый helper (например, переменные процесса `regexAnketaPattern` и `regexDefaultPattern`), если это допустимо в вашем процессе PIX.
