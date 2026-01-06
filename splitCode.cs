using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;

public static partial class Program
{
    // Входные/разделяемые переменные (RPA устанавливает через первый метод)
    public static DataTable s_dtReestrFilesFiltered;
    public static DataTable s_dtBookOfReferenceReestrRK;
    public static DataRow s_rowUniqNumber;
    public static Dictionary<int, string> s_dictionaryGUIDservices;
    public static string s_text;
    public static DataTable s_ReestrRKUpdate;
    public static string s_log; // итоговый лог, RPA может прочитать в конце

    // Внутреннее состояние между шагами
    public static StringBuilder s_logBuilder;
    public static string s_requestNumber;
    public static string s_guidEBA;
    public static Regex s_regexMain;
    public static Regex s_regexAlt;
    public static bool s_hasMatch;
    public static HashSet<string> s_parentSubjects;
    public static HashSet<string> s_foundParentSlots;
    public static (string subject, string slot, Regex pattern)[] s_parentPatterns;
    public static List<DataRow> s_matchingUpdateRows;
    public static List<DataRow> s_rowsWithTextInFilePaths;
    public static bool s_isChildSlot;
    public static HashSet<string> s_passportSets;
    public static Dictionary<string, Guid> s_complectCache;
    public static HashSet<string> s_importedKeys;
    public static bool s_shouldAbort; // если true — дальнейшие шаги можно не выполнять

    // Шаг 1 — установить входные параметры и инициализировать лог
    public static void FillReestrRK_NEW_Part1_SetParameters(DataTable dtReestrFilesFiltered, DataTable dtBookOfReferenceReestrRK, DataRow rowUniqNumber, Dictionary<int, string> dictionaryGUIDservices, ref string log, string text, DataTable ReestrRKUpdate)
    {
        try
        {
            s_dtReestrFilesFiltered = dtReestrFilesFiltered;
            s_dtBookOfReferenceReestrRK = dtBookOfReferenceReestrRK;
            s_rowUniqNumber = rowUniqNumber;
            s_dictionaryGUIDservices = dictionaryGUIDservices;
            s_text = text ?? string.Empty;
            s_ReestrRKUpdate = ReestrRKUpdate;
            s_log = log ?? string.Empty;

            s_logBuilder = new StringBuilder();
            s_requestNumber = s_rowUniqNumber?["Номер заявки"]?.ToString();
            s_guidEBA = null;
            s_hasMatch = false;
            s_shouldAbort = false;
        }
        catch (Exception ex)
        {
            s_logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - ERR - Part1: {ex.Message}");
        }
    }

    // Шаг 2 — найти guidEBA (если не найден на шаге 1)
    public static void FillReestrRK_NEW_Part2_FindGuidEBA()
    {
        try
        {
            if (!string.IsNullOrEmpty(s_guidEBA)) return;
            if (s_dtReestrFilesFiltered == null) return;

            foreach (DataRow row in s_dtReestrFilesFiltered.Rows)
            {
                var g = row["GUID ЕВА клиента"]?.ToString();
                if (!string.IsNullOrEmpty(g))
                {
                    s_guidEBA = g;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            s_logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - ERR - Part2: {ex.Message}");
        }
    }

    // Шаг 3 — скомпилировать регулярные выражения для поиска по имени файла
    public static void FillReestrRK_NEW_Part3_CompileRegex()
    {
        try
        {
            s_regexMain = null;
            s_regexAlt = null;
            if (string.IsNullOrEmpty(s_text)) return;

            if (s_text.Contains("anketa", StringComparison.Ordinal))
            {
                s_regexAlt = new Regex($@"(^|[_\s])({Regex.Escape(s_text)}_zatavl)(\d{{1,3}})?($|[_\s])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
                s_regexMain = new Regex($@"(^|[_\s])(?!.*\bzatavl\b)({Regex.Escape(s_text)})(\d{{1,3}})?($|[_\s])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }
            else
            {
                s_regexMain = new Regex($@"(^|[_\s])({Regex.Escape(s_text)})(\d{{1,3}})?($|[_\s])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }
        }
        catch (Exception ex)
        {
            s_logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - ERR - Part3: {ex.Message}");
        }
    }

    // Шаг 4 — проверить, есть ли совпадение имени файла с заданным текстом
    public static void FillReestrRK_NEW_Part4_CheckHasMatch()
    {
        try
        {
            s_hasMatch = false;
            if (s_dtReestrFilesFiltered == null || string.IsNullOrEmpty(s_text)) return;

            foreach (DataRow fileRow in s_dtReestrFilesFiltered.Rows)
            {
                var path = fileRow["Путь к файлу"]?.ToString();
                if (string.IsNullOrEmpty(path)) continue;
                string fileName = Path.GetFileNameWithoutExtension(path);
                if (string.IsNullOrEmpty(fileName)) continue;

                bool isMatch = false;
                if (s_regexMain != null && s_regexAlt != null)
                {
                    if (fileName.IndexOf("zatavl", StringComparison.OrdinalIgnoreCase) >= 0)
                        isMatch = s_regexAlt.IsMatch(fileName);
                    else
                        isMatch = s_regexMain.IsMatch(fileName);
                }
                else if (s_regexMain != null)
                {
                    isMatch = s_regexMain.IsMatch(fileName);
                }

                if (isMatch)
                {
                    s_logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - hasMatch: Найдено совпадение для '{s_text}' в файле '{fileName}'");
                    s_hasMatch = true;
                    break;
                }
            }

            if (!s_hasMatch) s_shouldAbort = true;
        }
        catch (Exception ex)
        {
            s_logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - ERR - Part4: {ex.Message}");
        }
    }

    // Шаг 5 — инициализация паттернов родителей и коллекций
    public static void FillReestrRK_NEW_Part5_InitParentPatterns()
    {
        try
        {
            s_parentSubjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            s_foundParentSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Вспомогательная строка для исключения zatavl (аналог Anchored)
            Func<string, bool, Regex> Anchored = (token, excludeZatavl) =>
            {
                var safe = Regex.Escape(token);
                var negative = excludeZatavl ? "(?!.*zatavl\\w*)" : string.Empty;
                return new Regex($@"(^|[_\s]){negative}{safe}(\d{{1,3}})?($|[_\s])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            };

            s_parentPatterns = new (string subject, string slot, Regex pattern)[]
            {
                ("BROK", "AnketaBroker",    Anchored("AnketaBroker", false)),
                ("BANK", "AnketaBank",      Anchored("AnketaBank", false)),
                ("BANK", "anketa_zatavl",   Anchored("anketa_zatavl", false)),
                ("BANK", "anketa",          new Regex($@"(^|[_\s])anketa(?![_a-zA-Z])(\d{{1,3}})?($|[_\s])", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
                ("EDO",  "zayavlenieakcept",Anchored("zayavlenieakcept", false)),
                ("EDO",  "zayavlenie",      Anchored("zayavlenie", false)),
                ("DU",   "AnketaDU",        Anchored("AnketaDU", false)),
            };
        }
        catch (Exception ex)
        {
            s_logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - ERR - Part5: {ex.Message}");
        }
    }

    // Шаг 6 — обнаружить родительские слоты по строкам файлов для текущей заявки
    public static void FillReestrRK_NEW_Part6_DetectParentSubjects()
    {
        try
        {
            if (s_dtReestrFilesFiltered == null || string.IsNullOrEmpty(s_requestNumber)) return;

            foreach (DataRow r in s_dtReestrFilesFiltered.Rows)
            {
                if (!string.Equals(r["Номер заявки"]?.ToString(), s_requestNumber, StringComparison.Ordinal)) continue;
                var name = Path.GetFileNameWithoutExtension(r["Путь к файлу"]?.ToString() ?? string.Empty);
                if (string.IsNullOrEmpty(name)) continue;

                foreach (var (subject, slot, pattern) in s_parentPatterns)
                {
                    if (pattern.IsMatch(name))
                    {
                        s_parentSubjects.Add(subject);
                        s_foundParentSlots.Add(slot);
                    }
                }
            }

            bool hasParent = s_parentSubjects.Count > 0;
            var parentSubjectsForLog = hasParent ? string.Join(",", s_parentSubjects) : "-";
            var foundParentSlotsForLog = s_foundParentSlots.Count > 0 ? string.Join(",", s_foundParentSlots) : "-";
            s_logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Родитель найден: {hasParent}, subject_type={parentSubjectsForLog}, slots={foundParentSlotsForLog}");
        }
        catch (Exception ex)
        {
            s_logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - ERR - Part6: {ex.Message}");
        }
    }

    // Шаг 7 — собрать matchingUpdateRows (с учётом специальной логики для registration)
    public static void FillReestrRK_NEW_Part7_FilterMatchingUpdateRows()
    {
        try
        {
            s_matchingUpdateRows = new List<DataRow>();
            if (s_dtBookOfReferenceReestrRK == null) return;

            var enumerable = s_dtBookOfReferenceReestrRK.AsEnumerable();
            foreach (var updateRow in enumerable)
            {
                var rowText = updateRow["Текст"]?.ToString();
                if (string.IsNullOrEmpty(rowText) || !rowText.Equals(s_text, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (s_text.Equals("registration", StringComparison.OrdinalIgnoreCase))
                {
                    var docSet = updateRow["document_set"]?.ToString()?.Trim();
                    var subject = updateRow["subject_type"]?.ToString()?.Trim();
                    var isBankParent = s_parentSubjects.Contains("BANK");
                    var isBrokParent = s_parentSubjects.Contains("BROK");
                    var isEdoParent = s_parentSubjects.Contains("EDO");

                    if (isBankParent && docSet == "BN_DKBO0064" && subject == "BANK")
                    {
                        s_matchingUpdateRows.Add(updateRow);
                    }
                    else if (isBrokParent && docSet == "PD0085" && subject == "BROK")
                    {
                        s_matchingUpdateRows.Add(updateRow);
                    }
                    else if (isEdoParent && docSet == "PD0085" && subject == "EDO")
                    {
                        s_matchingUpdateRows.Add(updateRow);
                    }
                }
                else
                {
                    s_matchingUpdateRows.Add(updateRow);
                }
            }

            s_logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Количество строк в matchingUpdateRows для {s_text} = {s_matchingUpdateRows.Count}");
        }
        catch (Exception ex)
        {
            s_logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - ERR - Part7: {ex.Message}");
        }
    }

    // Шаг 8 — найти строки, где имя файла содержит текст (rowsWithTextInFilePaths)
    public static void FillReestrRK_NEW_Part8_FindRowsWithTextInFilePaths()
    {
        try
        {
            s_rowsWithTextInFilePaths = new List<DataRow>();
            if (s_dtReestrFilesFiltered == null) return;

            Regex regexFilePath;
            if (s_text.Contains("anketa"))
                regexFilePath = new Regex($@"(?!.*zatavl\\w*)({Regex.Escape(s_text)})(\d{{1,3}})?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            else
                regexFilePath = new Regex($@"(^|[_\s])({Regex.Escape(s_text)})(\d{{1,3}})?($|[_\s])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

            foreach (DataRow filteredRow in s_dtReestrFilesFiltered.Rows)
            {
                var rowText = filteredRow["Путь к файлу"]?.ToString();
                if (string.IsNullOrEmpty(rowText)) continue;
                string fileName = Path.GetFileNameWithoutExtension(rowText);
                if (regexFilePath.IsMatch(fileName))
                    s_rowsWithTextInFilePaths.Add(filteredRow);
            }

            s_logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Количество строк в rowsWithTextInFilePaths для {s_text} = {s_rowsWithTextInFilePaths.Count}");
            if (s_rowsWithTextInFilePaths.Count == 0) s_shouldAbort = true;
        }
        catch (Exception ex)
        {
            s_logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - ERR - Part8: {ex.Message}");
        }
    }

    // Шаг 9 — отфильтровать matchingUpdateRows для дочерних слотов (если надо)
    public static void FillReestrRK_NEW_Part9_FilterByParentForChildSlots()
    {
        try
        {
            s_isChildSlot = s_text.Equals("uvedomlenie1", StringComparison.OrdinalIgnoreCase)
                || s_text.Equals("uvedomlenie2", StringComparison.OrdinalIgnoreCase)
                || s_text.Equals("uvedomlenie3", StringComparison.OrdinalIgnoreCase)
                || s_text.Equals("uvedomlenie4", StringComparison.OrdinalIgnoreCase)
                || s_text.Equals("ZayavleniyeBanka", StringComparison.OrdinalIgnoreCase)
                || s_text.Equals("ZayavleniyeKompaniya", StringComparison.OrdinalIgnoreCase)
                || s_text.Equals("registration", StringComparison.OrdinalIgnoreCase);

            if (s_isChildSlot && s_parentSubjects.Count > 0 && s_matchingUpdateRows != null)
            {
                s_matchingUpdateRows = s_matchingUpdateRows
                    .Where(row =>
                    {
                        var rowSubject = row["subject_type"]?.ToString()?.Trim();
                        return !string.IsNullOrEmpty(rowSubject) && s_parentSubjects.Contains(rowSubject);
                    })
                    .ToList();

                var parentSubjectsForLog = s_parentSubjects.Count > 0 ? string.Join(",", s_parentSubjects) : "-";
                s_logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - matchingUpdateRows отфильтрован по subject_type={parentSubjectsForLog}, rows={s_matchingUpdateRows.Count}");

                if (s_matchingUpdateRows.Count == 0)
                {
                    s_logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - WARN - Для '{s_text}' не найдено строк справочника c subject_type={parentSubjectsForLog}. Пропуск.");
                    s_shouldAbort = true;
                }
            }
        }
        catch (Exception ex)
        {
            s_logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - ERR - Part9: {ex.Message}");
        }
    }

    // Шаг 10 — подготовить к импортированию: множества, кеши, ключи
    public static void FillReestrRK_NEW_Part10_PrepareCaches()
    {
        try
        {
            s_passportSets = new HashSet<string> { "BN_DKBO0132", "BN_DKBO0048", "EDO0019", "BK1444", "DU0080", "PD0075" };
            s_complectCache = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            s_importedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (s_ReestrRKUpdate != null)
            {
                foreach (DataRow ex in s_ReestrRKUpdate.Rows)
                {
                    var req = ex["Номер заявки"]?.ToString()?.Trim() ?? string.Empty;
                    var ds = ex["document_set"]?.ToString()?.Trim() ?? string.Empty;
                    var st = ex["subject_type"]?.ToString()?.Trim() ?? string.Empty;
                    s_importedKeys.Add($"{req}|{ds}|{st}");
                }
            }
        }
        catch (Exception ex)
        {
            s_logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - ERR - Part10: {ex.Message}");
        }
    }

    // Шаг 11 — главный цикл обработки matchingUpdateRows (устанавливает file_id, ids, импортирует в s_ReestrRKUpdate)
    public static void FillReestrRK_NEW_Part11_ProcessMatchingUpdateRows()
    {
        try
        {
            if (s_matchingUpdateRows == null || s_matchingUpdateRows.Count == 0 || s_shouldAbort) return;
            if (s_dtReestrFilesFiltered == null) return;

            foreach (var updRow in s_matchingUpdateRows)
            {
                var guid = Guid.NewGuid();
                var guidDocumentId = Guid.NewGuid();

                updRow["Номер заявки"] = s_requestNumber;

                string documentSet = updRow["document_set"]?.ToString();
                string subjectType = updRow["subject_type"]?.ToString();
                var normalizedSubjectType = subjectType?.Trim();

                if (s_isChildSlot && s_parentSubjects.Count > 0 && !string.IsNullOrEmpty(normalizedSubjectType) && s_parentSubjects.Contains(normalizedSubjectType))
                {
                    var complectKey = $"{s_requestNumber}|{normalizedSubjectType}";
                    if (!s_complectCache.TryGetValue(complectKey, out var existing))
                    {
                        s_complectCache[complectKey] = guid;
                    }
                    else
                    {
                        guid = existing;
                    }
                }

                updRow["complect_id"] = guid;
                updRow["document_id"] = guidDocumentId;
                updRow["master_id"] = s_guidEBA;

                string GUIDserviceNumber = updRow["GUID услуги"]?.ToString();
                if (int.TryParse(GUIDserviceNumber, out int serviceNumber) && s_dictionaryGUIDservices != null && s_dictionaryGUIDservices.TryGetValue(serviceNumber, out string guidService))
                {
                    updRow["contract_id"] = guidService;
                }

                string searchText = null;
                List<string> fileIds = null;

                Regex regexSearchPasport = null;
                if (s_text.Contains("anketa"))
                {
                    regexSearchPasport = new Regex($@"(?!.*zatavl\\w*)({Regex.Escape(s_text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
                }
                else
                {
                    regexSearchPasport = new Regex($@"(^|[_\s])({Regex.Escape(s_text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
                }

                bool hasTextFile = s_dtReestrFilesFiltered.AsEnumerable()
                    .Any(r =>
                        r["Номер заявки"]?.ToString() == s_requestNumber &&
                        !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                        regexSearchPasport.IsMatch(Path.GetFileNameWithoutExtension(r["Путь к файлу"].ToString()))
                    );

                // Passport sets handling
                if (s_passportSets.Contains(documentSet) && hasTextFile)
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
                        s_foundParentSlots.Contains(r.ParentSlot));

                    if (rule.DocumentSet == null)
                    {
                        s_logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Пропуск {documentSet} ({subjectType}) для заявки {s_requestNumber}: не найден родительский слот для passport (rules from kZadache.csv)");
                    }
                    else
                    {
                        searchText = "pasport";
                        fileIds = new List<string>();
                        foreach (DataRow r in s_dtReestrFilesFiltered.Rows)
                        {
                            if (r["Номер заявки"]?.ToString() == s_requestNumber &&
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
                else if (documentSet?.Trim() == "PD0084" && normalizedSubjectType == "BANK")
                {
                    var filesWithUvedomlenie1 = new List<string>();
                    var filesWithUvedomlenie2 = new List<string>();
                    foreach (DataRow r in s_dtReestrFilesFiltered.Rows)
                    {
                        if (r["Номер заявки"]?.ToString() == s_requestNumber &&
                            !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                            r["ID файла в СХФ"]?.ToString() != "error")
                        {
                            var path = r["Путь к файлу"].ToString();
                            var id = r["ID файла в СХФ"]?.ToString();
                            if (!string.IsNullOrEmpty(id))
                            {
                                if (path.IndexOf("uvedomlenie1", StringComparison.OrdinalIgnoreCase) >= 0 && !filesWithUvedomlenie1.Contains(id))
                                    filesWithUvedomlenie1.Add(id);
                                if (path.IndexOf("uvedomlenie2", StringComparison.OrdinalIgnoreCase) >= 0 && !filesWithUvedomlenie2.Contains(id))
                                    filesWithUvedomlenie2.Add(id);
                            }
                        }
                    }
                    var allFiles = filesWithUvedomlenie1.Union(filesWithUvedomlenie2).ToList();
                    if (allFiles.Count > 0)
                        updRow["file_id"] = string.Join("|", allFiles);
                }
                else if (documentSet?.Trim() == "PD0084" && normalizedSubjectType == "BROK")
                {
                    var filesWithUvedomlenie3 = new List<string>();
                    var filesWithUvedomlenie4 = new List<string>();
                    foreach (DataRow r in s_dtReestrFilesFiltered.Rows)
                    {
                        if (r["Номер заявки"]?.ToString() == s_requestNumber &&
                            !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                            r["ID файла в СХФ"]?.ToString() != "error")
                        {
                            var path = r["Путь к файлу"].ToString();
                            var id = r["ID файла в СХФ"]?.ToString();
                            if (!string.IsNullOrEmpty(id))
                            {
                                if (path.IndexOf("uvedomlenie3", StringComparison.OrdinalIgnoreCase) >= 0 && !filesWithUvedomlenie3.Contains(id))
                                    filesWithUvedomlenie3.Add(id);
                                if (path.IndexOf("uvedomlenie4", StringComparison.OrdinalIgnoreCase) >= 0 && !filesWithUvedomlenie4.Contains(id))
                                    filesWithUvedomlenie4.Add(id);
                            }
                        }
                    }
                    var allFiles = filesWithUvedomlenie3.Union(filesWithUvedomlenie4).ToList();
                    if (allFiles.Count > 0)
                        updRow["file_id"] = string.Join("|", allFiles);
                }
                else if (documentSet?.Trim() == "PD0084" && normalizedSubjectType == "EDO")
                {
                    var filesWithUvedomlenie3 = new List<string>();
                    var filesWithUvedomlenie4 = new List<string>();
                    foreach (DataRow r in s_dtReestrFilesFiltered.Rows)
                    {
                        if (r["Номер заявки"]?.ToString() == s_requestNumber &&
                            !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                            r["ID файла в СХФ"]?.ToString() != "error")
                        {
                            var path = r["Путь к файлу"].ToString();
                            var id = r["ID файла в СХФ"]?.ToString();
                            if (!string.IsNullOrEmpty(id))
                            {
                                if (path.IndexOf("uvedomlenie3", StringComparison.OrdinalIgnoreCase) >= 0 && !filesWithUvedomlenie3.Contains(id))
                                    filesWithUvedomlenie3.Add(id);
                                if (path.IndexOf("uvedomlenie4", StringComparison.OrdinalIgnoreCase) >= 0 && !filesWithUvedomlenie4.Contains(id))
                                    filesWithUvedomlenie4.Add(id);
                            }
                        }
                    }
                    var allFiles = filesWithUvedomlenie3.Union(filesWithUvedomlenie4).ToList();
                    if (allFiles.Count > 0)
                        updRow["file_id"] = string.Join("|", allFiles);
                }
                else if (documentSet?.Trim() == "PD0085" && normalizedSubjectType == "BANK")
                {
                    searchText = "ZayavleniyeBanka";
                    fileIds = new List<string>();
                    foreach (DataRow r in s_dtReestrFilesFiltered.Rows)
                    {
                        if (r["Номер заявки"]?.ToString() == s_requestNumber &&
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
                else if (documentSet?.Trim() == "PD0085" && normalizedSubjectType == "EDO")
                {
                    searchText = s_text.Equals("registration", StringComparison.OrdinalIgnoreCase)
                        ? "registration"
                        : "ZayavleniyeKompaniya";

                    fileIds = new List<string>();
                    foreach (DataRow r in s_dtReestrFilesFiltered.Rows)
                    {
                        if (r["Номер заявки"]?.ToString() == s_requestNumber &&
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
                else if (documentSet?.Trim() == "PD0085" && normalizedSubjectType == "BROK")
                {
                    searchText = "registration";
                    fileIds = new List<string>();
                    foreach (DataRow r in s_dtReestrFilesFiltered.Rows)
                    {
                        if (r["Номер заявки"]?.ToString() == s_requestNumber &&
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
                else if (documentSet?.Trim() == "EDO0078" && normalizedSubjectType == "EDO")
                {
                    searchText = "ZayavleniyeKompaniya";
                    fileIds = new List<string>();
                    foreach (DataRow r in s_dtReestrFilesFiltered.Rows)
                    {
                        if (r["Номер заявки"]?.ToString() == s_requestNumber &&
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
                else if (documentSet?.Trim() == "BK1186" && normalizedSubjectType == "BROK")
                {
                    searchText = "ZayavleniyeKompaniya";
                    fileIds = new List<string>();
                    foreach (DataRow r in s_dtReestrFilesFiltered.Rows)
                    {
                        if (r["Номер заявки"]?.ToString() == s_requestNumber &&
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
                else if (documentSet?.Trim() == "BN_DKBO0064")
                {
                    searchText = s_text.Equals("registration", StringComparison.OrdinalIgnoreCase)
                        ? "registration"
                        : "ZayavleniyeBanka";

                    fileIds = new List<string>();
                    foreach (DataRow r in s_dtReestrFilesFiltered.Rows)
                    {
                        if (r["Номер заявки"]?.ToString() == s_requestNumber &&
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
                else if (documentSet?.Trim() == "BN_DKBO0134")
                {
                    s_logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - ОБРАБОТКА documentSet == BN_DKBO0134");
                    var raspiskaFileIds = new List<string>();
                    foreach (DataRow r in s_dtReestrFilesFiltered.Rows)
                    {
                        if (r["Номер заявки"]?.ToString() == s_requestNumber &&
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
                        s_ReestrRKUpdate.ImportRow(updRow);

                        for (int i = 1; i < raspiskaFileIds.Count; i++)
                        {
                            var newRow = s_ReestrRKUpdate.NewRow();
                            foreach (DataColumn col in updRow.Table.Columns)
                            {
                                if (s_ReestrRKUpdate.Columns.Contains(col.ColumnName))
                                    newRow[col.ColumnName] = updRow[col.ColumnName];
                            }
                            newRow["file_id"] = raspiskaFileIds[i];
                            s_ReestrRKUpdate.Rows.Add(newRow);
                        }
                    }
                    continue;
                }
                else
                {
                    searchText = updRow["Текст"]?.ToString();
                    Regex regexSearch;
                    if (s_text.Contains("anketa"))
                        regexSearch = new Regex($@"(?!.*zatavl\w*)({Regex.Escape(s_text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
                    else
                        regexSearch = new Regex($@"(^|[_\s])({Regex.Escape(s_text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

                    fileIds = new List<string>();
                    foreach (DataRow r in s_dtReestrFilesFiltered.Rows)
                    {
                        if (r["Номер заявки"]?.ToString() != s_requestNumber ||
                            string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) ||
                            r["ID файла в СХФ"]?.ToString() == "error")
                            continue;

                        string fileName = Path.GetFileNameWithoutExtension(r["Путь к файлу"].ToString());
                        if (regexSearch.IsMatch(fileName))
                        {
                            var id = r["ID файла в СХФ"]?.ToString();
                            if (!string.IsNullOrEmpty(id) && !fileIds.Contains(id))
                                fileIds.Add(id);
                        }
                    }
                    if (fileIds.Count > 0)
                        updRow["file_id"] = string.Join("|", fileIds);
                }

                // импорт строки (если есть file_id) с проверкой дубля
                if (documentSet != "BN_DKBO0134" && !string.IsNullOrEmpty(updRow["file_id"]?.ToString()))
                {
                    var key = $"{s_requestNumber}|{documentSet?.Trim()}|{subjectType?.Trim()}";
                    if (s_importedKeys.Add(key))
                    {
                        s_ReestrRKUpdate.ImportRow(updRow);
                    }
                    else
                    {
                        s_logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Пропуск дубля для ключа {key}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            s_logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - ERR - Part11: {ex.Message}");
        }
    }

    // Шаг 12 — финализация: дописать лог в переменную s_log
    public static void FillReestrRK_NEW_Part12_Finalize(ref string log)
    {
        try
        {
            log = log + Environment.NewLine + s_logBuilder.ToString();
            s_log = log;
        }
        catch (Exception ex)
        {
            // В крайнем случае — записать ошибку в s_log
            s_logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - ERR - Part12: {ex.Message}");
            s_log = log + Environment.NewLine + s_logBuilder.ToString();
        }
    }
}
