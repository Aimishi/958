using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace _958
{
    public class FillReestrRKState
    {
        // kept for compatibility but will be unused after refactor
    }

    public static class FillReestrRKRefactor
    {
        public static void FillReestrRK_NEW_Sequential(
            DataTable dtReestrFilesFiltered,
            DataTable dtBookOfReferenceReestrRK,
            DataRow rowUniqNumber,
            Dictionary<int, string> dictionaryGUIDservices,
            ref string log,
            string text,
            DataTable ReestrRKUpdate)
        {
            // Initialize local variables (copies of what was in FillReestrRKState)
            var dtReestrFilesFiltered_local = dtReestrFilesFiltered;
            var dtBookOfReferenceReestrRK_local = dtBookOfReferenceReestrRK;
            var rowUniqNumber_local = rowUniqNumber;
            var dictionaryGUIDservices_local = dictionaryGUIDservices;
            var text_local = text;
            var ReestrRKUpdate_local = ReestrRKUpdate;

            StringBuilder logBuilder = new StringBuilder();

            string requestNumber = null;
            string guidEBA = null;
            Regex regexMain = null;
            Regex regexAlt = null;
            bool hasMatch = false;
            HashSet<string> parentSubjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> foundParentSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<DataRow> matchingUpdateRows = new List<DataRow>();
            List<DataRow> rowsWithTextInFilePaths = new List<DataRow>();
            HashSet<string> passportSets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "BN_DKBO0132","BN_DKBO0048","EDO0019","BK1444","DU0080","PD0075" };
            Dictionary<string, Guid> complectCache = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> importedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // Step 1
                Step01_ExtractRequestAndGuidEBA(dtReestrFilesFiltered_local, rowUniqNumber_local, ref requestNumber, ref guidEBA, logBuilder);

                // Step 2
                Step02_CompileRegexes(text_local, out regexMain, out regexAlt, logBuilder);

                // Step 3
                Step03_DetectHasMatch(dtReestrFilesFiltered_local, text_local, regexMain, regexAlt, out hasMatch, logBuilder);
                if (!hasMatch)
                {
                    // merge logs and return
                    log = log + Environment.NewLine + logBuilder.ToString();
                    return;
                }

                // Step 4
                Step04_FindParentSubjectsSlots(dtReestrFilesFiltered_local, requestNumber, parentSubjects, foundParentSlots, logBuilder);

                // Step 5
                Step05_FilterMatchingUpdateRows(dtBookOfReferenceReestrRK_local, text_local, parentSubjects, ref matchingUpdateRows, logBuilder);
                if (matchingUpdateRows == null || matchingUpdateRows.Count == 0)
                {
                    log = log + Environment.NewLine + logBuilder.ToString();
                    return;
                }

                // Step 6
                Step06_FindRowsWithTextInFilePaths(dtReestrFilesFiltered_local, text_local, ref rowsWithTextInFilePaths, logBuilder);
                if (rowsWithTextInFilePaths == null || rowsWithTextInFilePaths.Count == 0)
                {
                    log = log + Environment.NewLine + logBuilder.ToString();
                    return;
                }

                // Step 7
                Step07_PreparePassportSetsAndCaches(ReestrRKUpdate_local, ref importedKeys, complectCache, logBuilder);

                // Step 8
                Step08_ProcessMatchingUpdateRows(
                    dtReestrFilesFiltered_local,
                    dictionaryGUIDservices_local,
                    requestNumber,
                    guidEBA,
                    text_local,
                    passportSets,
                    parentSubjects,
                    foundParentSlots,
                    matchingUpdateRows,
                    ReestrRKUpdate_local,
                    ref complectCache,
                    ref importedKeys,
                    logBuilder
                );
            }
            catch (Exception ex)
            {
                logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - ERR - {ex.Message}  {ex.StackTrace}");
            }
            finally
            {
                // Step 9: finalize and append
                Step09_FinalizeAndAppendLog(logBuilder, ref log);
            }
        }

        private static void Step01_ExtractRequestAndGuidEBA(
            DataTable dtReestrFilesFiltered,
            DataRow rowUniqNumber,
            ref string requestNumber,
            ref string guidEBA,
            StringBuilder logBuilder)
        {
            requestNumber = rowUniqNumber["Номер заявки"]?.ToString();
            guidEBA = null;
            foreach (DataRow row in dtReestrFilesFiltered.Rows)
            {
                guidEBA = row["GUID ЕВА клиента"]?.ToString();
                if (!string.IsNullOrEmpty(guidEBA)) break;
            }
            logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - requestNumber = {requestNumber}; guidEBA={guidEBA}");
        }

        private static void Step02_CompileRegexes(
            string text,
            out Regex regexMain,
            out Regex regexAlt,
            StringBuilder logBuilder)
        {
            regexMain = null;
            regexAlt = null;
            if (!string.IsNullOrEmpty(text))
            {
                if (text.Contains("anketa", StringComparison.Ordinal))
                {
                    regexAlt = new Regex($@"(^|[_\s])({Regex.Escape(text)}_zatavl)(\d{{1,3}})?($|[_\s])",
                        RegexOptions.IgnoreCase | RegexOptions.Compiled);
                    regexMain = new Regex($@"(^|[_\s])(?!.*\bzatavl\b)({Regex.Escape(text)})(\d{{1,3}})?($|[_\s])",
                        RegexOptions.IgnoreCase | RegexOptions.Compiled);
                }
                else
                {
                    regexMain = new Regex($@"(^|[_\s])({Regex.Escape(text)})(\d{{1,3}})?($|[_\s])",
                        RegexOptions.IgnoreCase | RegexOptions.Compiled);
                }
            }
            logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Regex compiled for text={text}");
        }

        private static void Step03_DetectHasMatch(
            DataTable dtReestrFilesFiltered,
            string text,
            Regex regexMain,
            Regex regexAlt,
            out bool hasMatch,
            StringBuilder logBuilder)
        {
            hasMatch = false;
            foreach (DataRow fileRow in dtReestrFilesFiltered.Rows)
            {
                var path = fileRow["Путь к файлу"]?.ToString();
                if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(text)) continue;
                string fileName = System.IO.Path.GetFileNameWithoutExtension(path);

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
        }

        private static void Step04_FindParentSubjectsSlots(
            DataTable dtReestrFilesFiltered,
            string requestNumber,
            HashSet<string> parentSubjects,
            HashSet<string> foundParentSlots,
            StringBuilder logBuilder)
        {
            parentSubjects.Clear();
            foundParentSlots.Clear();

            static Regex Anchored(string token, bool excludeZatavl = false)
            {
                var safe = Regex.Escape(token);
                var negative = excludeZatavl ? "(?!.*zatavl\\w*)" : string.Empty;
                return new Regex($@"(^|[_\s]){negative}{safe}(\d{{1,3}})?($|[_\s])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }

            var parentPatterns = new (string subject, string slot, Regex pattern)[]
            {
                ("BROK", "AnketaBroker", Anchored("AnketaBroker")),
                ("BANK", "AnketaBank", Anchored("AnketaBank")),
                ("BANK", "anketa_zatavl", Anchored("anketa_zatavl")),
                ("BANK", "anketa", new Regex($@"(^|[_\s])anketa(?![_a-zA-Z])(\d{{1,3}})?($|[_\s])", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
                ("EDO", "zayavlenieakcept", Anchored("zayavlenieakcept")),
                ("EDO", "zayavlenie", Anchored("zayavlenie")),
                ("DU", "AnketaDU", Anchored("AnketaDU")),
            };

            foreach (DataRow r in dtReestrFilesFiltered.Rows)
            {
                if (!string.Equals(r["Номер заявки"]?.ToString(), requestNumber, StringComparison.Ordinal)) continue;

                var name = System.IO.Path.GetFileNameWithoutExtension(r["Путь к файлу"]?.ToString() ?? string.Empty);
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

            bool hasParent = parentSubjects.Count > 0;
            var parentSubjectsForLog = hasParent ? string.Join(",", parentSubjects) : "-";
            var foundParentSlotsForLog = foundParentSlots.Count > 0 ? string.Join(",", foundParentSlots) : "-";
            logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Родитель найден: {hasParent}, subject_type={parentSubjectsForLog}, slots={foundParentSlotsForLog}");
        }

        private static void Step05_FilterMatchingUpdateRows(
            DataTable dtBookOfReferenceReestrRK,
            string text,
            HashSet<string> parentSubjects,
            ref List<DataRow> matchingUpdateRows,
            StringBuilder logBuilder)
        {
            matchingUpdateRows = dtBookOfReferenceReestrRK.AsEnumerable()
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

                        if (isBankParent && docSet == "BN_DKBO0064" && subject == "BANK")
                            return true;
                        if (isBrokParent && docSet == "PD0085" && subject == "BROK")
                            return true;
                        if (isEdoParent && docSet == "PD0085" && subject == "EDO")
                            return true;

                        return false;
                    }

                    return true;
                })
                .ToList();

            logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Количество строк в matchingUpdateRows для {text} = {matchingUpdateRows.Count}");
        }

        private static void Step06_FindRowsWithTextInFilePaths(
            DataTable dtReestrFilesFiltered,
            string text,
            ref List<DataRow> rowsWithTextInFilePaths,
            StringBuilder logBuilder)
        {
            rowsWithTextInFilePaths = new List<DataRow>();
            Regex regexFilePath = null;
            if (text.Contains("anketa"))
            {
                regexFilePath = new Regex($@"(?!.*zatavl\w*)({Regex.Escape(text)})(\d{{1,3}})?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }
            else
            {
                regexFilePath = new Regex($@"(^|[_\s])({Regex.Escape(text)})(\d{{1,3}})?($|[_\s])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }

            foreach (DataRow filteredRow in dtReestrFilesFiltered.Rows)
            {
                var rowText = filteredRow["Путь к файлу"]?.ToString();
                if (string.IsNullOrEmpty(rowText)) continue;
                string fileName = System.IO.Path.GetFileNameWithoutExtension(rowText);
                if (regexFilePath.IsMatch(fileName))
                    rowsWithTextInFilePaths.Add(filteredRow);
            }
            logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Количество строк в rowsWithTextInFilePaths для {text} = {rowsWithTextInFilePaths.Count}");
        }

        private static void Step07_PreparePassportSetsAndCaches(
            DataTable ReestrRKUpdate,
            ref HashSet<string> importedKeys,
            Dictionary<string, Guid> complectCache,
            StringBuilder logBuilder)
        {
            // PassportSets are local in orchestrator
            complectCache.Clear();
            importedKeys.Clear();

            foreach (DataRow ex in ReestrRKUpdate.Rows)
            {
                var req = ex["Номер заявки"]?.ToString()?.Trim() ?? string.Empty;
                var ds = ex["document_set"]?.ToString()?.Trim() ?? string.Empty;
                var st = ex["subject_type"]?.ToString()?.Trim() ?? string.Empty;
                importedKeys.Add($"{req}|{ds}|{st}");
            }
            logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Prepared passport sets and caches. ImportedKeys={importedKeys.Count}");
        }

        private static void Step08_ProcessMatchingUpdateRows(
            DataTable dtReestrFilesFiltered,
            Dictionary<int, string> dictionaryGUIDservices,
            string requestNumber,
            string guidEBA,
            string text,
            HashSet<string> passportSets,
            HashSet<string> parentSubjects,
            HashSet<string> foundParentSlots,
            List<DataRow> matchingUpdateRows,
            DataTable ReestrRKUpdate,
            ref Dictionary<string, Guid> complectCache,
            ref HashSet<string> importedKeys,
            StringBuilder logBuilder)
        {
            foreach (var updRow in matchingUpdateRows)
            {
                var guid = Guid.NewGuid();
                var guidDocumentId = Guid.NewGuid();

                updRow["Номер заявки"] = requestNumber;

                bool isChildSlot = text.Equals("uvedomlenie1", StringComparison.OrdinalIgnoreCase)
                    || text.Equals("uvedomlenie2", StringComparison.OrdinalIgnoreCase)
                    || text.Equals("uvedomlenie3", StringComparison.OrdinalIgnoreCase)
                    || text.Equals("uvedomlenie4", StringComparison.OrdinalIgnoreCase)
                    || text.Equals("ZayavleniyeBanka", StringComparison.OrdinalIgnoreCase)
                    || text.Equals("ZayavleniyeKompaniya", StringComparison.OrdinalIgnoreCase)
                    || text.Equals("registration", StringComparison.OrdinalIgnoreCase);

                string documentSet = updRow["document_set"]?.ToString();
                string subjectType = updRow["subject_type"]?.ToString();
                var normalizedSubjectType = subjectType?.Trim();

                if (isChildSlot && parentSubjects.Count > 0 && !string.IsNullOrEmpty(normalizedSubjectType) && parentSubjects.Contains(normalizedSubjectType))
                {
                    var complectKey = $"{requestNumber}|{normalizedSubjectType}";
                    if (!complectCache.TryGetValue(complectKey, out var existing))
                    {
                        complectCache[complectKey] = guid;
                    }
                    else
                    {
                        guid = existing;
                    }
                }

                updRow["complect_id"] = guid;
                updRow["document_id"] = guidDocumentId;
                updRow["master_id"] = guidEBA;

                string GUIDserviceNumber = updRow["GUID услуги"]?.ToString();
                if (int.TryParse(GUIDserviceNumber, out int serviceNumber) && dictionaryGUIDservices.TryGetValue(serviceNumber, out string guidService))
                {
                    updRow["contract_id"] = guidService;
                }

                string searchText = null;
                List<string> fileIds = null;

                Regex regexSearchPasport = null;
                if (text.Contains("anketa"))
                {
                    regexSearchPasport = new Regex($@"(?!.*zatavl\\w*)({Regex.Escape(text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
                }
                else
                {
                    regexSearchPasport = new Regex($@"(^|[_\\s])({Regex.Escape(text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
                }

                bool hasTextFile = dtReestrFilesFiltered.AsEnumerable()
                    .Any(r =>
                        r["Номер заявки"]?.ToString() == requestNumber &&
                        !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                        regexSearchPasport.IsMatch(System.IO.Path.GetFileNameWithoutExtension(r["Путь к файлу"].ToString()))
                    );

                if (passportSets.Contains(documentSet) && hasTextFile)
                {
                    // passport parent rules
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
                        (string.IsNullOrEmpty(r.SubjectType) || r.SubjectType.Equals(normSubject, StringComparison.OrdinalIgnoreCase)) &&
                        foundParentSlots.Contains(r.ParentSlot));

                    if (rule.DocumentSet == null)
                    {
                        logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Пропуск {documentSet} ({subjectType}) для заявки {requestNumber}: не найден родительский слот для passport (rules from kZadache.csv)");
                    }
                    else
                    {
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
                else if (documentSet?.Trim() == "PD0084" && normalizedSubjectType == "BANK")
                {
                    var filesWithUvedomlenie1 = new List<string>();
                    var filesWithUvedomlenie2 = new List<string>();
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
                                if (path.IndexOf("uvedomlenie3", StringComparison.OrdinalIgnoreCase) >= 0 && !filesWithUvedomlenie3.Contains(id))
                                    filesWithUvedomlenie3.Add(id);
                                if (path.IndexOf("uvedomlenie4", StringComparison.OrdinalIgnoreCase) >= 0 && !filesWithUvedomлениe4.Contains(id))
                                    filesWithUvedomлениe4.Add(id);
                            }
                        }
                    }
                    var allFiles = filesWithUvedomлениe3.Union(filesWithUvedомлениe4).ToList();
                    if (allFiles.Count > 0)
                        updRow["file_id"] = string.Join("|", allFiles);
                }
                else if (documentSet?.Trim() == "PD0085" && normalizedSubjectType == "BANK")
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
                    if (fileIds.Count > 0)
                        updRow["file_id"] = string.Join("|", fileIds);
                }
                else if (documentSet?.Trim() == "PD0085" && normalizedSubjectType == "EDO")
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
                    if (fileIds.Count > 0)
                        updRow["file_id"] = string.Join("|", fileIds);
                }
                else if (documentSet?.Trim() == "PD0085" && normalizedSubjectType == "BROK")
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
                    if (fileIds.Count > 0)
                        updRow["file_id"] = string.Join("|", fileIds);
                }
                else if (documentSet?.Trim() == "EDO0078" && normalizedSubjectType == "EDO")
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
                    if (fileIds.Count > 0)
                        updRow["file_id"] = string.Join("|", fileIds);
                }
                else if (documentSet?.Trim() == "BK1186" && normalizedSubjectType == "BROK")
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
                    if (fileIds.Count > 0)
                        updRow["file_id"] = string.Join("|", fileIds);
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
                    if (fileIds.Count > 0)
                        updRow["file_id"] = string.Join("|", fileIds);
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
                    searchText = updRow["Текст"]?.ToString();

                    Regex regexSearch = null;
                    if (text.Contains("anketa"))
                    {
                        regexSearch = new Regex($@"(?!.*zatavl\\w*)({Regex.Escape(text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
                    }
                    else
                    {
                        regexSearch = new Regex($@"(^|[_\\s])({Regex.Escape(text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
                    }

                    fileIds = new List<string>();
                    foreach (DataRow r in dtReestrFilesFiltered.Rows)
                    {
                        if (r["Номер заявки"]?.ToString() != requestNumber ||
                            string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) ||
                            r["ID файла в СХФ"]?.ToString() == "error")
                            continue;

                        string fileName = System.IO.Path.GetFileNameWithoutExtension(r["Путь к файлу"].ToString());
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
        }

        private static void Step09_FinalizeAndAppendLog(StringBuilder logBuilder, ref string log)
        {
            log = log + Environment.NewLine + logBuilder.ToString();
        }
    }
}
