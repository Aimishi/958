using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace _958
{
    internal class Class2
    {
        public static void FillReestrRK_NEW(
    DataTable dtReestrFilesFiltered,
    DataTable dtBookOfReferenceReestrRK,
    DataRow rowUniqNumber,
    Dictionary<int, string> dictionaryGUIDservices,
    ref string log,
    string text,
    DataTable ReestrRKUpdate)
        {
            var logBuilder = new System.Text.StringBuilder();
            try
            {
                string requestNumber = rowUniqNumber["Номер заявки"].ToString();
                string guidEBA = GetGuidEba(dtReestrFilesFiltered);

                // 1) Предподготовка регулярных выражений для первичного матчинга по имени файла
                var (regexMain, regexAlt) = BuildPrimaryRegex(text);

                // 2) Проверка наличия хотя бы одного файла, соответствующего текущему `text`
                if (!HasAnyMatchingFile(dtReestrFilesFiltered, text, regexMain, regexAlt, logBuilder))
                    return;

                // 3) Поиск родительских слотов/тем по всему реестру текущей заявки
                var (parentSubjects, foundParentSlots) = DetectParentSlots(dtReestrFilesFiltered, requestNumber, logBuilder);

                // 4) Получение кандидатов строк из справочника для текущего `text` с учётом спец. кейсов (registration)
                var matchingUpdateRows = GetMatchingUpdateRows(dtBookOfReferenceReestrRK, text, parentSubjects);

                // 5) Список строк реестра, где в путях файлов присутствует `text` (для статуса наличия)
                var rowsWithTextInFilePaths = GetRowsWithTextInFilePaths(dtReestrFilesFiltered, text);

                logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Количество строк в matchingUpdateRows для {text} = {matchingUpdateRows.Count}");
                logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Количество строк в rowsWithTextInFilePaths для {text} = {rowsWithTextInFilePaths.Count}");

                bool isChildSlot = IsChildSlot(text);
                bool hasParent = parentSubjects.Count > 0;
                var parentSubjectsForLog = hasParent ? string.Join(",", parentSubjects) : "-";

                // 6) Если слот дочерний — фильтруем по subject_type родителей
                if (isChildSlot && hasParent)
                {
                    matchingUpdateRows = matchingUpdateRows
                        .Where(row =>
                        {
                            var rowSubject = row["subject_type"]?.ToString()?.Trim();
                            return !string.IsNullOrEmpty(rowSubject) && parentSubjects.Contains(rowSubject);
                        })
                        .ToList();

                    logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - matchingUpdateRows отфильтрован по subject_type={parentSubjectsForLog}, rows={matchingUpdateRows.Count}");

                    if (matchingUpdateRows.Count == 0)
                    {
                        logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - WARN - Для '{text}' не найдено строк справочника c subject_type={parentSubjectsForLog}. Пропуск.");
                        return;
                    }
                }

                if (rowsWithTextInFilePaths.Count == 0) return;

                // 7) Кэш для комплектов, чтобы фиксировать один complect_id на (заявка+subject_type)
                var complectCache = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

                // 8) Множество импортированных ключей (защита от дублей)
                var importedKeys = BuildImportedKeys(ReestrRKUpdate);

                // 9) Основной цикл обработки строк-обновлений
                foreach (var updRow in matchingUpdateRows)
                {
                    PrepareUpdateRowIdentifiers(updRow, requestNumber, guidEBA, dictionaryGUIDservices, isChildSlot, hasParent, parentSubjects, complectCache);

                    string documentSet = updRow["document_set"]?.ToString();
                    string subjectType = updRow["subject_type"]?.ToString();
                    var normalizedSubjectType = subjectType?.Trim();

                    // 10) Собираем file_id по правилам
                    CollectFileIdsForUpdateRow(
                        dtReestrFilesFiltered,
                        requestNumber,
                        text,
                        documentSet,
                        normalizedSubjectType,
                        parentSubjects,
                        foundParentSlots,
                        updRow,
                        logBuilder
                    );

                    // 11) Импорт строки (кроме BN_DKBO0134, где импорт происходит внутри специальной обработки)
                    if (documentSet != "BN_DKBO0134" && !string.IsNullOrEmpty(updRow["file_id"]?.ToString()))
                    {
                        TryImportUnique(ReestrRKUpdate, updRow, importedKeys, requestNumber, documentSet, subjectType, logBuilder);
                    }
                }
            }
            catch (Exception ex)
            {
                logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - ERR - {ex.Message}  {ex.StackTrace}  {ex.Data}  {ex.Source}");
            }
            finally
            {
                log = log + Environment.NewLine + logBuilder.ToString();
            }
        }

        private static string GetGuidEba(DataTable dtReestrFilesFiltered)
        {
            foreach (DataRow row in dtReestrFilesFiltered.Rows)
            {
                var guidEBA = row["GUID ЕВА клиента"]?.ToString();
                if (!string.IsNullOrEmpty(guidEBA)) return guidEBA;
            }
            return null;
        }

        private static (Regex regexMain, Regex regexAlt) BuildPrimaryRegex(string text)
        {
            System.Text.RegularExpressions.Regex regexMain = null, regexAlt = null;
            if (!string.IsNullOrEmpty(text))
            {
                if (text.Contains("anketa", StringComparison.Ordinal))
                {
                    regexAlt = new Regex($@"(^|[_\s])({Regex.Escape(text)}_zatavl)(\d{{1,3}})?($|[_\s])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
                    regexMain = new Regex($@"(^|[_\s])(?!.*\bzatavl\b)({Regex.Escape(text)})(\d{{1,3}})?($|[_\s])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
                }
                else
                {
                    regexMain = new Regex($@"(^|[_\s])({Regex.Escape(text)})(\d{{1,3}})?($|[_\s])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
                }
            }
            return (regexMain, regexAlt);
        }

        private static bool HasAnyMatchingFile(DataTable dtReestrFilesFiltered, string text, Regex regexMain, Regex regexAlt, StringBuilder logBuilder)
        {
            bool hasMatch = false;
            foreach (DataRow fileRow in dtReestrFilesFiltered.Rows)
            {
                var path = fileRow["Путь к файлу"]?.ToString();
                if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(text)) continue;
                string fileName = Path.GetFileNameWithoutExtension(path);

                bool isMatch = false;
                if (regexMain != null && regexAlt != null)
                {
                    if (fileName.IndexOf("zatavl", StringComparison.OrdinalIgnoreCase) >= 0)
                        isMatch = regexAlt.IsMatch(fileName);
                    else
                        isMatch = regexMain.IsMatch(fileName);
                }
                else if (regexMain != null)
                {
                    isMatch = regexMain.IsMatch(fileName);
                }

                if (isMatch)
                {
                    logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - hasMatch: Найдено совпадение для '{text}' в файле '{fileName}'");
                    hasMatch = true;
                    break;
                }
            }
            return hasMatch;
        }

        private static Regex Anchored(string token, bool excludeZatavl = false)
        {
            var safe = Regex.Escape(token);
            var negative = excludeZatavl ? "(?!.*zatavl\\w*)" : string.Empty;
            return new Regex($@"(^|[_\s]){negative}{safe}(\d{{1,3}})?($|[_\s])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        private static (HashSet<string> parentSubjects, HashSet<string> foundParentSlots) DetectParentSlots(
            DataTable dtReestrFilesFiltered,
            string requestNumber,
            StringBuilder logBuilder)
        {
            var parentSubjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var foundParentSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

            var parentSubjectsForLog = parentSubjects.Count > 0 ? string.Join(",", parentSubjects) : "-";
            var foundParentSlotsForLog = foundParentSlots.Count > 0 ? string.Join(",", foundParentSlots) : "-";
            logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Родитель найден: {parentSubjects.Count > 0}, subject_type={parentSubjectsForLog}, slots={foundParentSlotsForLog}");

            return (parentSubjects, foundParentSlots);
        }

        private static List<DataRow> GetMatchingUpdateRows(DataTable dtBookOfReferenceReestrRK, string text, HashSet<string> parentSubjects)
        {
            return dtBookOfReferenceReestrRK.AsEnumerable()
                .Where(updateRow =>
                {
                    var rowText = updateRow["Текст"]?.ToString();
                    if (string.IsNullOrEmpty(rowText) || !rowText.Equals(text, StringComparison.OrdinalIgnoreCase))
                        return false;

                    if (text.Equals("registration", StringComparison.OrdinalIgnoreCase))
                    {
                        var docSet = updateRow["document_set"]?.ToString()?.Trim();
                        var subject = updateRow["subject_type"]?.ToString()?.Trim();
                        var isBankParent = parentSubjects.Contains("BANK");
                        var isBrokParent = parentSubjects.Contains("BROK");
                        var isEdoParent = parentSubjects.Contains("EDO");

                        if (isBankParent && docSet == "BN_DKBO0064" && subject == "BANK") return true;
                        if (isBrokParent && docSet == "PD0085" && subject == "BROK") return true;
                        if (isEdoParent && docSet == "PD0085" && subject == "EDO") return true;
                        return false;
                    }

                    return true;
                })
                .ToList();
        }

        private static List<DataRow> GetRowsWithTextInFilePaths(DataTable dtReestrFilesFiltered, string text)
        {
            var rowsWithTextInFilePaths = new List<DataRow>();
            Regex regexFilePath = text.Contains("anketa", StringComparison.OrdinalIgnoreCase)
                ? new Regex($@"(?!.*zatavl\\w*)({Regex.Escape(text)})(\d{{1,3}})?", RegexOptions.IgnoreCase | RegexOptions.Compiled)
                : new Regex($@"(^|[_\s])({Regex.Escape(text)})(\d{{1,3}})?($|[_\s])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            foreach (DataRow filteredRow in dtReestrFilesFiltered.Rows)
            {
                var rowText = filteredRow["Путь к файлу"]?.ToString();
                if (string.IsNullOrEmpty(rowText)) continue;
                string fileName = Path.GetFileNameWithoutExtension(rowText);
                if (regexFilePath.IsMatch(fileName))
                    rowsWithTextInFilePaths.Add(filteredRow);
            }

            return rowsWithTextInFilePaths;
        }

        private static bool IsChildSlot(string text)
        {
            return text.Equals("uvedomlenie1", StringComparison.OrdinalIgnoreCase)
                || text.Equals("uvedomlenie2", StringComparison.OrdinalIgnoreCase)
                || text.Equals("uvedomlenie3", StringComparison.OrdinalIgnoreCase)
                || text.Equals("uvedomlenie4", StringComparison.OrdinalIgnoreCase)
                || text.Equals("ZayavleniyeBanka", StringComparison.OrdinalIgnoreCase)
                || text.Equals("ZayavleniyeKompaniya", StringComparison.OrdinalIgnoreCase)
                || text.Equals("registration", StringComparison.OrdinalIgnoreCase);
        }

        private static HashSet<string> BuildImportedKeys(DataTable reestrRKUpdate)
        {
            var importedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataRow ex in reestrRKUpdate.Rows)
            {
                var req = ex["Номер заявки"]?.ToString()?.Trim() ?? string.Empty;
                var ds = ex["document_set"]?.ToString()?.Trim() ?? string.Empty;
                var st = ex["subject_type"]?.ToString()?.Trim() ?? string.Empty;
                importedKeys.Add($"{req}|{ds}|{st}");
            }
            return importedKeys;
        }

        private static void PrepareUpdateRowIdentifiers(
            DataRow updRow,
            string requestNumber,
            string guidEBA,
            Dictionary<int, string> dictionaryGUIDservices,
            bool isChildSlot,
            bool hasParent,
            HashSet<string> parentSubjects,
            Dictionary<string, Guid> complectCache)
        {
            var guid = Guid.NewGuid();
            var guidDocumentId = Guid.NewGuid();

            updRow["Номер заявки"] = requestNumber;

            string subjectType = updRow["subject_type"]?.ToString();
            var normalizedSubjectType = subjectType?.Trim();

            if (isChildSlot && hasParent && !string.IsNullOrEmpty(normalizedSubjectType) && parentSubjects.Contains(normalizedSubjectType))
            {
                var complectKey = $"{requestNumber}|{normalizedSubjectType}";
                if (!complectCache.TryGetValue(complectKey, out var existing))
                    complectCache[complectKey] = guid;
                else
                    guid = existing;
            }

            updRow["complect_id"] = guid;
            updRow["document_id"] = guidDocumentId;
            updRow["master_id"] = guidEBA;

            string GUIDserviceNumber = updRow["GUID услуги"]?.ToString();
            if (int.TryParse(GUIDserviceNumber, out int serviceNumber) && dictionaryGUIDservices.TryGetValue(serviceNumber, out string guidService))
            {
                updRow["contract_id"] = guidService;
            }
        }

        private static void CollectFileIdsForUpdateRow(
            DataTable dtReestrFilesFiltered,
            string requestNumber,
            string text,
            string documentSet,
            string normalizedSubjectType,
            HashSet<string> parentSubjects,
            HashSet<string> foundParentSlots,
            DataRow updRow,
            StringBuilder logBuilder)
        {
            var passportSets = new HashSet<string> { "BN_DKBO0132", "BN_DKBO0048", "EDO0019", "BK1444", "DU0080", "PD0075" };

            // Наличие любого файла по text (для некоторых правил)
            var regexSearchPassport = text.Contains("anketa", StringComparison.OrdinalIgnoreCase)
                ? new Regex($@"(?!.*zatavl\\w*)({Regex.Escape(text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])", RegexOptions.IgnoreCase | RegexOptions.Compiled)
                : new Regex($@"(^|[_\s])({Regex.Escape(text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            bool hasTextFile = dtReestrFilesFiltered.AsEnumerable()
                .Any(r =>
                    r["Номер заявки"]?.ToString() == requestNumber &&
                    !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                    regexSearchPassport.IsMatch(Path.GetFileNameWithoutExtension(r["Путь к файлу"].ToString()))
                );

            // Спецобработка BN_DKBO0134 (raspiska → множественный импорт)
            if (documentSet?.Trim() == "BN_DKBO0134")
            {
                logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - ОБРАБОТКА documentSet == BN_DKBO0134");
                var raspiskaFileIds = CollectBySubstring(dtReestrFilesFiltered, requestNumber, "raspiska");
                if (raspiskaFileIds.Count > 0)
                {
                    // Первая строка — в текущий updRow
                    updRow["file_id"] = raspiskaFileIds[0];
                    // Остальные — будут импортированы вне (в оригинальном коде делался импорт сразу)
                    // Мы сохраним прежнее поведение:
                    // Импорт текущего ряда + последующие дубли с разными file_id
                    // Вынуждено повторяем здесь импорт, чтобы не нарушить логику
                    // (переносим блок импорта сюда для BN_DKBO0134)
                    var table = updRow.Table;
                    table.DataSet.Tables[table.TableName].ImportRow(updRow);

                    for (int i = 1; i < raspiskaFileIds.Count; i++)
                    {
                        var newRow = table.NewRow();
                        foreach (DataColumn col in updRow.Table.Columns)
                        {
                            if (table.Columns.Contains(col.ColumnName))
                                newRow[col.ColumnName] = updRow[col.ColumnName];
                        }
                        newRow["file_id"] = raspiskaFileIds[i];
                        table.Rows.Add(newRow);
                    }
                }
                return;
            }

            // Passport наборы: разрешение по родительским слотам и сбор pasport*
            if (passportSets.Contains(documentSet) && hasTextFile)
            {
                var passportParentRules = new (string ParentSlot, string DocumentSet, string SubjectType)[]
                {
                    ("anketa_zatavl", "BN_DKBO0132", "BANK"),
                    ("AnketaBank",    "BN_DKBO0048", "BANK"),
                    ("zayavlenie",    "EDO0019",     "EDO"),
                    ("zayvlenieakcept","EDO0019",    "EDO"),
                    ("AnketaBroker",  "BK1444",      "BROK"),
                    ("AnketaDU",      "DU0080",      "DU"),
                    ("anketa",        "PD0075",      "BANK"),
                };

                var normDocSet = documentSet?.Trim();
                var normSubject = normalizedSubjectType;

                var rule = passportParentRules.FirstOrDefault(r =>
                    r.DocumentSet.Equals(normDocSet, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrEmpty(r.SubjectType) ||
                     r.SubjectType.Equals(normSubject, StringComparison.OrdinalIgnoreCase)) &&
                    foundParentSlots.Contains(r.ParentSlot));

                if (rule.DocumentSet == null)
                {
                    logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Пропуск {documentSet} ({normalizedSubjectType}) для заявки {requestNumber}: не найден родительский слот для passport (rules from kZadache.csv)");
                }
                else
                {
                    var fileIds = CollectBySubstring(dtReestrFilesFiltered, requestNumber, "pasport");
                    if (fileIds.Count > 0) updRow["file_id"] = string.Join("|", fileIds);
                }
                return;
            }

            // PD0084 (BANK/BROK/EDO) → uvedomlenieX
            if (documentSet?.Trim() == "PD0084")
            {
                if (normalizedSubjectType == "BANK")
                {
                    var ids1 = CollectBySubstring(dtReestrFilesFiltered, requestNumber, "uvedomlenie1");
                    var ids2 = CollectBySubstring(dtReestrFilesFiltered, requestNumber, "uvedomlenie2");
                    var all = ids1.Union(ids2).ToList();
                    if (all.Count > 0) updRow["file_id"] = string.Join("|", all);
                }
                else if (normalizedSubjectType == "BROK" || normalizedSubjectType == "EDO")
                {
                    var ids3 = CollectBySubstring(dtReestrFilesFiltered, requestNumber, "uvedomlenie3");
                    var ids4 = CollectBySubstring(dtReestrFilesFiltered, requestNumber, "uvedomlenie4");
                    var all = ids3.Union(ids4).ToList();
                    if (all.Count > 0) updRow["file_id"] = string.Join("|", all);
                }
                return;
            }

            // PD0085 → ZayavleniyeBanka/ZayavleniyeKompaniya/registration
            if (documentSet?.Trim() == "PD0085")
            {
                string searchText = normalizedSubjectType switch
                {
                    "BANK" => "ZayavleniyeBanka",
                    "BROK" => text.Equals("registration", StringComparison.OrdinalIgnoreCase) ? "registration" : "ZayavleniyeKompaniya",
                    "EDO" => text.Equals("registration", StringComparison.OrdinalIgnoreCase) ? "registration" : "ZayavleniyeKompaniya",
                    _ => null
                };
                if (!string.IsNullOrEmpty(searchText))
                {
                    var ids = CollectBySubstring(dtReestrFilesFiltered, requestNumber, searchText);
                    if (ids.Count > 0) updRow["file_id"] = string.Join("|", ids);
                }
                return;
            }

            // EDO0078/EDO → ZayavleniyeKompaniya
            if (documentSet?.Trim() == "EDO0078" && normalizedSubjectType == "EDO")
            {
                var ids = CollectBySubstring(dtReestrFilesFiltered, requestNumber, "ZayavleniyeKompaniya");
                if (ids.Count > 0) updRow["file_id"] = string.Join("|", ids);
                return;
            }

            // BN_DKBO0064 → registration или ZayavleniyeBanka
            if (documentSet?.Trim() == "BN_DKBO0064")
            {
                string searchText = text.Equals("registration", StringComparison.OrdinalIgnoreCase) ? "registration" : "ZayavleniyeBanka";
                var ids = CollectBySubstring(dtReestrFilesFiltered, requestNumber, searchText);
                if (ids.Count > 0) updRow["file_id"] = string.Join("|", ids);
                return;
            }

            // Общий поиск по regex (включая правила для anketa/zatavl)
            {
                var regexSearch = text.Contains("anketa", StringComparison.OrdinalIgnoreCase)
                    ? new Regex($@"(?!.*zatavl\w*)({Regex.Escape(text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])", RegexOptions.IgnoreCase | RegexOptions.Compiled)
                    : new Regex($@"(^|[_\s])({Regex.Escape(text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

                var fileIds = CollectByRegex(dtReestrFilesFiltered, requestNumber, regexSearch);
                if (fileIds.Count > 0) updRow["file_id"] = string.Join("|", fileIds);
            }
        }

        private static List<string> CollectBySubstring(DataTable dt, string requestNumber, string substring)
        {
            var list = new List<string>();
            foreach (DataRow r in dt.Rows)
            {
                if (r["Номер заявки"]?.ToString() == requestNumber &&
                    !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                    r["Путь к файлу"].ToString().IndexOf(substring, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    r["ID файла в СХФ"]?.ToString() != "error")
                {
                    var id = r["ID файла в СХФ"]?.ToString();
                    if (!string.IsNullOrEmpty(id) && !list.Contains(id))
                        list.Add(id);
                }
            }
            return list;
        }

        private static List<string> CollectByRegex(DataTable dt, string requestNumber, Regex regexSearch)
        {
            var list = new List<string>();
            foreach (DataRow r in dt.Rows)
            {
                if (r["Номер заявки"]?.ToString() != requestNumber ||
                    string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) ||
                    r["ID файла в СХФ"]?.ToString() == "error")
                    continue;

                string fileName = Path.GetFileNameWithoutExtension(r["Путь к файлу"].ToString());
                if (regexSearch.IsMatch(fileName))
                {
                    var id = r["ID файла в СХФ"]?.ToString();
                    if (!string.IsNullOrEmpty(id) && !list.Contains(id))
                        list.Add(id);
                }
            }
            return list;
        }

        private static void TryImportUnique(
            DataTable reestrRKUpdate,
            DataRow updRow,
            HashSet<string> importedKeys,
            string requestNumber,
            string documentSet,
            string subjectType,
            StringBuilder logBuilder)
        {
            var key = $"{requestNumber}|{documentSet?.Trim()}|{subjectType?.Trim()}";
            if (importedKeys.Add(key))
            {
                reestrRKUpdate.ImportRow(updRow);
            }
            else
            {
                logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Пропуск дубля для ключа {key}");
            }
        }


    }
}
