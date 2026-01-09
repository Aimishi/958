using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace _958
{
    internal static class PixRpaStepsVerifier
    {
        public static void VerifyPixStepsEquivalence()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var reestrFilesTable = Program.ReadCsvToDataTable("958_test_101225_reestrFiles.csv");
            var firstRequestNumber = reestrFilesTable.AsEnumerable()
                .Select(r => r.Field<string>("Номер заявки"))
                .FirstOrDefault(num => !string.IsNullOrEmpty(num));

            var filteredFilesTable = reestrFilesTable.AsEnumerable()
                .Where(r => r.Field<string>("Номер заявки") == firstRequestNumber)
                .CopyToDataTable();

            var dictionaryGUIDservices = BuildDictionaryFromJson(filteredFilesTable);
            var bookReferenceTable = Program.ReadCsvToDataTable("dtBookOfReferenceReestrRK_new_2.csv");
            AddUpdateColumns(bookReferenceTable);

            var uniqueTexts = bookReferenceTable.AsEnumerable()
                .Select(row => row.Field<string>("Текст"))
                .Where(text => !string.IsNullOrEmpty(text))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var originalBookRef = bookReferenceTable.Copy();
            var stepsBookRef = bookReferenceTable.Copy();
            var originalFiles = filteredFilesTable.Copy();
            var stepsFiles = filteredFilesTable.Copy();

            var originalResult = RunOriginal(originalFiles, originalBookRef, dictionaryGUIDservices, uniqueTexts);
            var stepsResult = RunPixSteps(originalFiles: stepsFiles, bookReference: stepsBookRef, dictionaryGUIDservices: dictionaryGUIDservices, uniqueTexts: uniqueTexts);

            var areEqual = CompareResults(originalResult, stepsResult, out var missing, out var extra);
            Console.WriteLine(areEqual ? "OK: FillReestrRK_NEW output matches PIX steps" : "FAIL: differences detected between FillReestrRK_NEW and PIX steps");
            Console.WriteLine($"Original rows: {originalResult.Rows.Count}, Steps rows: {stepsResult.Rows.Count}");

            if (!areEqual)
            {
                if (missing.Count > 0)
                {
                    Console.WriteLine("Missing in steps:");
                    foreach (var key in missing) Console.WriteLine($"  {key}");
                }

                if (extra.Count > 0)
                {
                    Console.WriteLine("Extra in steps:");
                    foreach (var key in extra) Console.WriteLine($"  {key}");
                }
            }
        }

        private static DataTable RunOriginal(DataTable filteredFilesTable, DataTable bookReferenceTable, Dictionary<int, string> dictionaryGUIDservices, List<string> uniqueTexts)
        {
            var reestr = CreateEmptyReestrTable();
            foreach (DataRow rowUniq in filteredFilesTable.Rows)
            {
                foreach (var text in uniqueTexts)
                {
                    if (string.IsNullOrEmpty(text)) continue;
                    var log = string.Empty;
                    Program.FillReestrRK_NEW(filteredFilesTable, bookReferenceTable, rowUniq, dictionaryGUIDservices, ref log, text, reestr);
                }
            }
            return reestr;
        }

        private static DataTable RunPixSteps(DataTable originalFiles, DataTable bookReference, Dictionary<int, string> dictionaryGUIDservices, List<string> uniqueTexts)
        {
            var reestr = CreateEmptyReestrTable();

            foreach (DataRow rowUniq in originalFiles.Rows)
            {
                foreach (var text in uniqueTexts)
                {
                    if (string.IsNullOrEmpty(text)) continue;

                    var logBuilder = new StringBuilder();
                    var shouldAbort = false;
                    var hasMatch = false;
                    var isChildSlot = false;
                    var hasParent = false;

                    string requestNumber = null;
                    string guidEBA = null;
                    Regex regexMain = null;
                    Regex regexAlt = null;

                    var parentSubjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var foundParentSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var matchingUpdateRows = new List<DataRow>();
                    var rowsWithTextInFilePaths = new List<DataRow>();
                    var passportSets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var complectCache = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
                    var importedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    // STEP01_InitContext
                    logBuilder.Clear();
                    shouldAbort = false;
                    hasMatch = false;
                    isChildSlot = false;
                    hasParent = false;

                    requestNumber = rowUniq?["Номер заявки"]?.ToString();
                    guidEBA = null;

                    if (originalFiles != null)
                    {
                        foreach (DataRow row in originalFiles.Rows)
                        {
                            var g = row["GUID ЕВА клиента"]?.ToString();
                            if (!string.IsNullOrEmpty(g))
                            {
                                guidEBA = g;
                                break;
                            }
                        }
                    }

                    parentSubjects.Clear();
                    foundParentSlots.Clear();
                    matchingUpdateRows.Clear();
                    rowsWithTextInFilePaths.Clear();

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

                    // STEP03_CheckHasMatch
                    hasMatch = false;
                    if (shouldAbort || originalFiles == null || string.IsNullOrEmpty(text))
                    {
                        shouldAbort = true;
                    }
                    else
                    {
                        foreach (DataRow fileRow in originalFiles.Rows)
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

                    // STEP04_DetectParents
                    if (!shouldAbort)
                    {
                        parentSubjects.Clear();
                        foundParentSlots.Clear();

                        var regexParent_AnketaBroker = new Regex($@"(^|[_\s])AnketaBroker(\d{{1,3}})?($|[_\s])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
                        var regexParent_AnketaBank = new Regex($@"(^|[_\s])AnketaBank(\d{{1,3}})?($|[_\s])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
                        var regexParent_anketa_zatavl = new Regex($@"(^|[_\s])anketa_zatavl(\d{{1,3}})?($|[_\s])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
                        var regexParent_anketa_strict = new Regex($@"(^|[_\s])anketa(?![_a-zA-Z])(\d{{1,3}})?($|[_\s])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
                        var regexParent_zayavlenieakcept = new Regex($@"(^|[_\s])zayavlenieakcept(\d{{1,3}})?($|[_\s])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
                        var regexParent_zayavlenie = new Regex($@"(^|[_\s])zayavlenie(\d{{1,3}})?($|[_\s])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
                        var regexParent_AnketaDU = new Regex($@"(^|[_\s])AnketaDU(\d{{1,3}})?($|[_\s])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

                        foreach (DataRow r in originalFiles.Rows)
                        {
                            if (!string.Equals(r["Номер заявки"]?.ToString(), requestNumber, StringComparison.Ordinal)) continue;
                            var name = Path.GetFileNameWithoutExtension(r["Путь к файлу"]?.ToString() ?? string.Empty);
                            if (string.IsNullOrEmpty(name)) continue;

                            if (regexParent_AnketaBroker.IsMatch(name))
                            {
                                parentSubjects.Add("BROK");
                                foundParentSlots.Add("AnketaBroker");
                            }
                            if (regexParent_AnketaBank.IsMatch(name))
                            {
                                parentSubjects.Add("BANK");
                                foundParentSlots.Add("AnketaBank");
                            }
                            if (regexParent_anketa_zatavl.IsMatch(name))
                            {
                                parentSubjects.Add("BANK");
                                foundParentSlots.Add("anketa_zatavl");
                            }
                            if (regexParent_anketa_strict.IsMatch(name))
                            {
                                parentSubjects.Add("BANK");
                                foundParentSlots.Add("anketa");
                            }
                            if (regexParent_zayavlenieakcept.IsMatch(name))
                            {
                                parentSubjects.Add("EDO");
                                foundParentSlots.Add("zayavlenieakcept");
                            }
                            if (regexParent_zayavlenie.IsMatch(name))
                            {
                                parentSubjects.Add("EDO");
                                foundParentSlots.Add("zayavlenie");
                            }
                            if (regexParent_AnketaDU.IsMatch(name))
                            {
                                parentSubjects.Add("DU");
                                foundParentSlots.Add("AnketaDU");
                            }
                        }

                        hasParent = parentSubjects.Count > 0;
                        logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Родитель найден: {hasParent}, subject_type={string.Join(',', parentSubjects.DefaultIfEmpty("-"))}, slots={string.Join(',', foundParentSlots.DefaultIfEmpty("-"))}");
                    }

                    // STEP05_SelectMatchingUpdateRows
                    if (!shouldAbort)
                    {
                        matchingUpdateRows.Clear();

                        foreach (DataRow updateRow in bookReference.Rows)
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
                                var isEdoParent = parentSubjects.Contains("EDO");

                                if (isBankParent && docSet == "BN_DKBO0064" && subject == "BANK") { matchingUpdateRows.Add(updateRow); continue; }
                                if (isBrokParent && docSet == "PD0085" && subject == "BROK") { matchingUpdateRows.Add(updateRow); continue; }
                                if (isEdoParent && docSet == "PD0085" && subject == "EDO") { matchingUpdateRows.Add(updateRow); continue; }

                                continue;
                            }

                            matchingUpdateRows.Add(updateRow);
                        }
                    }

                    // STEP06_FindRowsWithTextInFilePaths
                    if (!shouldAbort)
                    {
                        rowsWithTextInFilePaths.Clear();

                        Regex regexFilePath = text.Contains("anketa")
                            ? new Regex($@"(?!.*zatavl\w*)({Regex.Escape(text)})(\d{{1,3}})?", RegexOptions.IgnoreCase | RegexOptions.Compiled)
                            : new Regex($@"(^|[_\s])({Regex.Escape(text)})(\d{{1,3}})?($|[_\s])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

                        foreach (DataRow filteredRow in originalFiles.Rows)
                        {
                            var rowText = filteredRow["Путь к файлу"]?.ToString();
                            if (string.IsNullOrEmpty(rowText)) continue;
                            string fileName = Path.GetFileNameWithoutExtension(rowText);
                            if (regexFilePath.IsMatch(fileName))
                                rowsWithTextInFilePaths.Add(filteredRow);
                        }

                        if (rowsWithTextInFilePaths.Count == 0) shouldAbort = true;
                    }

                    // STEP07_DetectIsChildSlot_And_FilterByParentSubject
                    if (!shouldAbort)
                    {
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
                    }

                    // STEP08_PrepareCaches
                    if (!shouldAbort)
                    {
                        passportSets.Clear();
                        foreach (var code in new[] { "BN_DKBO0132", "BN_DKBO0048", "EDO0019", "BK1444", "DU0080", "PD0075" })
                            passportSets.Add(code);

                        complectCache.Clear();

                        importedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (DataRow ex in reestr.Rows)
                        {
                            var req = ex["Номер заявки"]?.ToString()?.Trim() ?? string.Empty;
                            var ds = ex["document_set"]?.ToString()?.Trim() ?? string.Empty;
                            var st = ex["subject_type"]?.ToString()?.Trim() ?? string.Empty;
                            importedKeys.Add($"{req}|{ds}|{st}");
                        }
                    }

                    // STEP09_ProcessMatchingUpdateRows
                    if (!shouldAbort)
                    {
                        if (matchingUpdateRows == null || matchingUpdateRows.Count == 0)
                        {
                            shouldAbort = true;
                        }
                        else
                        {
                            foreach (var updRow in matchingUpdateRows)
                            {
                                var complectId = Guid.NewGuid();
                                var documentId = Guid.NewGuid();

                                updRow["Номер заявки"] = requestNumber;

                                var documentSet = updRow["document_set"]?.ToString();
                                var subjectType = updRow["subject_type"]?.ToString();
                                var normalizedSubject = subjectType?.Trim();
                                var normalizedDocumentSet = documentSet?.Trim();

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

                                bool hasTextFile = originalFiles.AsEnumerable().Any(r =>
                                    r["Номер заявки"]?.ToString() == requestNumber &&
                                    !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                                    regexSearchPassport.IsMatch(Path.GetFileNameWithoutExtension(r["Путь к файлу"].ToString())));

                                List<string> fileIds = null;
                                string searchText = null;

                                if (passportSets.Contains(normalizedDocumentSet ?? documentSet) && hasTextFile)
                                {
                                    var passportParentRules = new List<string[]>
                                    {
                                        new [] { "BN_DKBO0132", "anketa_zatavl", "BANK" },
                                        new [] { "BN_DKBO0048", "AnketaBank", "BANK" },
                                        new [] { "EDO0019", "zayavlenie", "EDO" },
                                        new [] { "EDO0019", "zayavlenieakcept", "EDO" },
                                        new [] { "BK1444", "AnketaBroker", "BROK" },
                                        new [] { "DU0080", "AnketaDU", "DU" },
                                        new [] { "PD0075", "anketa", "BANK" },
                                    };

                                    string matchedRuleDocumentSet = null;
                                    foreach (var rule in passportParentRules)
                                    {
                                        var ruleDocSet = rule[0];
                                        var ruleSlot = rule[1];
                                        var ruleSubject = rule[2];
                                        var docMatches = !string.IsNullOrEmpty(ruleDocSet) && string.Equals(ruleDocSet, normalizedDocumentSet ?? string.Empty, StringComparison.OrdinalIgnoreCase);
                                        var subjectMatches = string.IsNullOrEmpty(ruleSubject) || string.Equals(ruleSubject, normalizedSubject, StringComparison.OrdinalIgnoreCase);
                                        if (docMatches && subjectMatches && foundParentSlots.Contains(ruleSlot))
                                        {
                                            matchedRuleDocumentSet = ruleDocSet;
                                            break;
                                        }
                                    }

                                    if (matchedRuleDocumentSet == null)
                                    {
                                        logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Пропуск {documentSet} ({subjectType}) для {requestNumber}: нет родительского слота для passport.");
                                    }
                                    else
                                    {
                                        searchText = "pasport";
                                        fileIds = new List<string>();
                                        foreach (DataRow r in originalFiles.Rows)
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
                                else if (normalizedDocumentSet == "PD0084" && normalizedSubject == "BANK")
                                {
                                    var files1 = new List<string>();
                                    var files2 = new List<string>();
                                    foreach (DataRow r in originalFiles.Rows)
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
                                else if (normalizedDocumentSet == "PD0084" && (normalizedSubject == "BROK" || normalizedSubject == "EDO"))
                                {
                                    var files3 = new List<string>();
                                    var files4 = new List<string>();
                                    foreach (DataRow r in originalFiles.Rows)
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
                                else if (normalizedDocumentSet == "PD0085" && normalizedSubject == "BANK")
                                {
                                    searchText = "ZayavleniyeBanka";
                                    fileIds = new List<string>();
                                    foreach (DataRow r in originalFiles.Rows)
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
                                else if (normalizedDocumentSet == "PD0085" && normalizedSubject == "EDO")
                                {
                                    searchText = text.Equals("registration", StringComparison.OrdinalIgnoreCase) ? "registration" : "ZayavleniyeKompaniya";
                                    fileIds = new List<string>();
                                    foreach (DataRow r in originalFiles.Rows)
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
                                else if (normalizedDocumentSet == "PD0085" && normalizedSubject == "BROK")
                                {
                                    searchText = "registration";
                                    fileIds = new List<string>();
                                    foreach (DataRow r in originalFiles.Rows)
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
                                else if (normalizedDocumentSet == "EDO0078" && normalizedSubject == "EDO")
                                {
                                    searchText = "ZayavleniyeKompaniya";
                                    fileIds = new List<string>();
                                    foreach (DataRow r in originalFiles.Rows)
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
                                else if (normalizedDocumentSet == "BK1186" && normalizedSubject == "BROK")
                                {
                                    searchText = "ZayavleniyeKompaniya";
                                    fileIds = new List<string>();
                                    foreach (DataRow r in originalFiles.Rows)
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
                                else if (normalizedDocumentSet == "BN_DKBO0064")
                                {
                                    searchText = text.Equals("registration", StringComparison.OrdinalIgnoreCase) ? "registration" : "ZayavleniyeBanka";
                                    fileIds = new List<string>();
                                    foreach (DataRow r in originalFiles.Rows)
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
                                else if (normalizedDocumentSet == "BN_DKBO0134")
                                {
                                    logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - ОБРАБОТКА documentSet == BN_DKBO0134");
                                    var raspiskaFileIds = new List<string>();
                                    foreach (DataRow r in originalFiles.Rows)
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
                                        reestr.ImportRow(updRow);

                                        for (int i = 1; i < raspiskaFileIds.Count; i++)
                                        {
                                            var newRow = reestr.NewRow();
                                            foreach (DataColumn col in updRow.Table.Columns)
                                            {
                                                if (reestr.Columns.Contains(col.ColumnName))
                                                    newRow[col.ColumnName] = updRow[col.ColumnName];
                                            }
                                            newRow["file_id"] = raspiskaFileIds[i];
                                            reestr.Rows.Add(newRow);
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
                                    foreach (DataRow r in originalFiles.Rows)
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

                                if (normalizedDocumentSet != "BN_DKBO0134" && !string.IsNullOrEmpty(updRow["file_id"]?.ToString()))
                                {
                                    var key = $"{requestNumber}|{normalizedDocumentSet}|{normalizedSubject}";
                                    if (importedKeys.Add(key))
                                    {
                                        reestr.ImportRow(updRow);
                                    }
                                    else
                                    {
                                        logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Пропуск дубля для ключа {key}");
                                    }
                                }
                            }
                        }
                    }

                    // STEP10_FlushLog (диагностика; лог в консоль)
                    if (logBuilder.Length > 0)
                    {
                        Console.WriteLine(logBuilder.ToString());
                    }
                }
            }

            return reestr;
        }

        private static DataTable CreateEmptyReestrTable()
        {
            var table = new DataTable();
            table.Columns.Add("Номер заявки", typeof(string));
            table.Columns.Add("document_set", typeof(string));
            table.Columns.Add("subject_type", typeof(string));
            table.Columns.Add("complect_name", typeof(string));
            table.Columns.Add("complect_id", typeof(string));
            table.Columns.Add("is_original", typeof(string));
            table.Columns.Add("file_id", typeof(string));
            table.Columns.Add("request_id", typeof(string));
            table.Columns.Add("event_time", typeof(string));
            table.Columns.Add("result BizTalk", typeof(string));
            table.Columns.Add("result Lotus", typeof(string));
            table.Columns.Add("Ссылка на РК", typeof(string));
            table.Columns.Add("Завершена обработка", typeof(string));
            table.Columns.Add("GUID услуги", typeof(string));
            return table;
        }

        private static Dictionary<int, string> BuildDictionaryFromJson(DataTable filteredFilesTable)
        {
            var dictionaryGUIDservices = new Dictionary<int, string>();
            string jsonServices = filteredFilesTable.AsEnumerable()
                .Select(row => row.Field<string>("GUID услуги"))
                .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

            if (!string.IsNullOrWhiteSpace(jsonServices))
            {
                var jsonArray = JsonSerializer.Deserialize<JsonElement>(jsonServices);
                foreach (var element in jsonArray.EnumerateArray())
                {
                    int serviceType = element.GetProperty("service_type").GetInt32();
                    string serviceGuid = element.GetProperty("service_guid").GetString();
                    dictionaryGUIDservices[serviceType] = serviceGuid;
                }
            }

            return dictionaryGUIDservices;
        }

        private static void AddUpdateColumns(DataTable bookReferenceTable)
        {
            var requiredColumns = new[]
            {
                "Номер заявки", "master_id", "contract_id", "document_id", "file_id"
            };

            foreach (var columnName in requiredColumns)
            {
                if (!bookReferenceTable.Columns.Contains(columnName))
                {
                    bookReferenceTable.Columns.Add(columnName, typeof(string));
                }
            }
        }

        private static bool CompareResults(DataTable original, DataTable steps, out List<string> missing, out List<string> extra)
        {
            var originalKeys = BuildKeySet(original);
            var stepKeys = BuildKeySet(steps);

            missing = originalKeys.Except(stepKeys).ToList();
            extra = stepKeys.Except(originalKeys).ToList();

            return missing.Count == 0 && extra.Count == 0;
        }

        private static IEnumerable<string> BuildKeySet(DataTable table)
        {
            foreach (DataRow row in table.Rows)
            {
                var req = row["Номер заявки"]?.ToString()?.Trim() ?? string.Empty;
                var ds = row["document_set"]?.ToString()?.Trim() ?? string.Empty;
                var st = row["subject_type"]?.ToString()?.Trim() ?? string.Empty;
                var fileId = row["file_id"]?.ToString()?.Trim() ?? string.Empty;
                yield return $"{req}|{ds}|{st}|{fileId}";
            }
        }
    }
}
