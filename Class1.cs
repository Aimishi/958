using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace _958
{
    internal class Class1
    {
        public static void FillReestrRK_optimaize_manyMethods(System.Data.DataTable dtReestrFilesFiltered, System.Data.DataTable dtBookOfReferenceReestrRK, System.Data.DataRow rowUniqNumber, System.Collections.Generic.Dictionary<int, string> dictionaryGUIDservices, ref string log, string text, System.Data.DataTable ReestrRKUpdate)
        {
            var logBuilder = new System.Text.StringBuilder();

            try
            {
                // Переменные общего контекста, доступны всем локальным методам
                string requestNumber = null;
                string guidEBA = null;

                System.Text.RegularExpressions.Regex regexMain = null;
                System.Text.RegularExpressions.Regex regexAlt = null;

                bool hasMatch = false;

                bool hasParent = false;
                string preferredSubject = null;

                System.Collections.Generic.List<System.Data.DataRow> matchingUpdateRows = null;
                System.Collections.Generic.List<System.Data.DataRow> rowsWithTextInFilePaths = null;

                bool isChildSlot = false;

                var passportSets = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
                {
                    "BN_DKBO0132", "BN_DKBO0048", "EDO0019", "BK1444", "DU0080", "PD0075"
                };

                var complectCache = new System.Collections.Generic.Dictionary<string, System.Guid>(System.StringComparer.OrdinalIgnoreCase);
                var importedKeys = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

                // 1) Инициализация ключевых значений и regex
                void InitializeContext()
                {
                    requestNumber = rowUniqNumber["Номер заявки"].ToString();

                    foreach (System.Data.DataRow row in dtReestrFilesFiltered.Rows)
                    {
                        guidEBA = row["GUID ЕВА клиента"]?.ToString();
                        if (!string.IsNullOrEmpty(guidEBA)) break;
                    }

                    regexMain = null;
                    regexAlt = null;

                    if (!string.IsNullOrEmpty(text))
                    {
                        if (text.Contains("anketa", System.StringComparison.Ordinal))
                        {
                            regexAlt = new System.Text.RegularExpressions.Regex(
                                $@"(^|[_\s])({System.Text.RegularExpressions.Regex.Escape(text)}_zatavl)(\d{{1,3}})?($|[_\s])",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

                            regexMain = new System.Text.RegularExpressions.Regex(
                                $@"(^|[_\s])(?!.*\bzatavl\b)({System.Text.RegularExpressions.Regex.Escape(text)})(\d{{1,3}})?($|[_\s])",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
                        }
                        else
                        {
                            regexMain = new System.Text.RegularExpressions.Regex(
                                $@"(^|[_\s])({System.Text.RegularExpressions.Regex.Escape(text)})(\d{{1,3}})?($|[_\s])",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
                        }
                    }
                }

                // 2) Поиск первичного совпадения по имени файла
                void FindPrimaryMatch()
                {
                    hasMatch = false;
                    foreach (System.Data.DataRow fileRow in dtReestrFilesFiltered.Rows)
                    {
                        var path = fileRow["Путь к файлу"]?.ToString();
                        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(text)) continue;

                        string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                        bool isMatchLocal = false;

                        if (regexMain != null && regexAlt != null)
                        {
                            if (fileName.IndexOf("zatavl", System.StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                isMatchLocal = regexAlt.IsMatch(fileName);
                            }
                            else
                            {
                                isMatchLocal = regexMain.IsMatch(fileName);
                            }
                        }
                        else if (regexMain != null)
                        {
                            isMatchLocal = regexMain.IsMatch(fileName);
                        }

                        if (isMatchLocal)
                        {
                            logBuilder.AppendLine($"{System.DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - hasMatch: Найдено совпадение для '{text}' в файле '{fileName}'");
                            hasMatch = true;
                            break;
                        }
                    }
                }

                // 3) Определение родителя и subject_type
                void DetectParent()
                {
                    hasParent = false;
                    preferredSubject = null;

                    static System.Text.RegularExpressions.Regex Anchored(string token, bool excludeZatavl = false)
                    {
                        var safe = System.Text.RegularExpressions.Regex.Escape(token);
                        var negative = excludeZatavl ? "(?!.*zatavl\\w*)" : "";
                        return new System.Text.RegularExpressions.Regex(
                            $@"(^|[_\s]){negative}{safe}(\d{{1,3}})?($|[_\s])",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
                    }

                    var parentPatterns = new (string subject, System.Text.RegularExpressions.Regex pattern)[]
                    {
                        ("BROK", Anchored("AnketaBroker")),
                        ("BANK", Anchored("AnketaBank")),
                        ("BANK", Anchored("anketa_zatavl")),
                        ("BANK", Anchored("anketa", excludeZatavl: true)),
                        ("EDO",  Anchored("zayvlenieakcept")),
                        ("EDO",  Anchored("zayavlenie")),
                    };

                    foreach (System.Data.DataRow r in dtReestrFilesFiltered.Rows)
                    {
                        if (!string.Equals(r["Номер заявки"]?.ToString(), requestNumber, System.StringComparison.Ordinal)) continue;

                        var name = System.IO.Path.GetFileNameWithoutExtension(r["Путь к файлу"]?.ToString() ?? "");
                        if (string.IsNullOrEmpty(name)) continue;

                        foreach (var item in parentPatterns)
                        {
                            var subject = item.subject;
                            var pattern = item.pattern;
                            if (pattern.IsMatch(name))
                            {
                                hasParent = true;
                                preferredSubject ??= subject;
                                break;
                            }
                        }

                        if (preferredSubject != null) break;
                    }

                    logBuilder.AppendLine($"{System.DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Родитель найден: {hasParent}, subject_type={preferredSubject ?? "-"}");
                }

                // 4) Кэшируем строки справочника, совпадающие по тексту
                void CacheMatchingUpdateRows()
                {
                    matchingUpdateRows = new System.Collections.Generic.List<System.Data.DataRow>();
                    foreach (System.Data.DataRow updateRow in dtBookOfReferenceReestrRK.Rows)
                    {
                        var rowText = updateRow["Текст"]?.ToString();
                        if (!string.IsNullOrEmpty(rowText) && rowText.Equals(text, System.StringComparison.OrdinalIgnoreCase))
                            matchingUpdateRows.Add(updateRow);
                    }
                }

                // 5) Кэшируем строки файлов, совпадающие по имени
                void CacheRowsWithTextInFilePaths()
                {
                    rowsWithTextInFilePaths = new System.Collections.Generic.List<System.Data.DataRow>();

                    System.Text.RegularExpressions.Regex regexFilePath;
                    if (text.Contains("anketa", System.StringComparison.Ordinal))
                    {
                        regexFilePath = new System.Text.RegularExpressions.Regex(
                            $@"(?!.*zatavl\w*)({System.Text.RegularExpressions.Regex.Escape(text)})(\d{{1,3}})?",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
                    }
                    else
                    {
                        regexFilePath = new System.Text.RegularExpressions.Regex(
                            $@"(^|[_\s])({System.Text.RegularExpressions.Regex.Escape(text)})(\d{{1,3}})?($|[_\s])",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
                    }

                    foreach (System.Data.DataRow filteredRow in dtReestrFilesFiltered.Rows)
                    {
                        var rowText = filteredRow["Путь к файлу"]?.ToString();
                        if (string.IsNullOrEmpty(rowText)) continue;
                        string fileName = System.IO.Path.GetFileNameWithoutExtension(rowText);
                        if (regexFilePath.IsMatch(fileName))
                            rowsWithTextInFilePaths.Add(filteredRow);
                    }

                    logBuilder.AppendLine($"{System.DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Количество строк в matchingUpdateRows для {text} = {matchingUpdateRows.Count}");
                    logBuilder.AppendLine($"{System.DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Количество строк в rowsWithTextInFilePaths для {text} = {rowsWithTextInFilePaths.Count}");
                }

                // 6) Определяем дочерний слот и фильтруем по родительскому subject_type
                bool FilterByParentSubjectType()
                {
                    isChildSlot =
                           text.Equals("uvedomlenie1", System.StringComparison.OrdinalIgnoreCase)
                        || text.Equals("uvedomlenie2", System.StringComparison.OrdinalIgnoreCase)
                        || text.Equals("uvedomlenie3", System.StringComparison.OrdinalIgnoreCase)
                        || text.Equals("uvedomlenie4", System.StringComparison.OrdinalIgnoreCase)
                        || text.Equals("ZayavleniyeBanka", System.StringComparison.OrdinalIgnoreCase)
                        || text.Equals("ZayavleniyeKompaniya", System.StringComparison.OrdinalIgnoreCase)
                        || text.Equals("registration", System.StringComparison.OrdinalIgnoreCase);

                    if (isChildSlot && hasParent && !string.IsNullOrEmpty(preferredSubject))
                    {
                        matchingUpdateRows = matchingUpdateRows
                            .Where(row => string.Equals(row["subject_type"]?.ToString()?.Trim(), preferredSubject, System.StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        logBuilder.AppendLine($"{System.DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - matchingUpdateRows отфильтрован по subject_type={preferredSubject}, rows={matchingUpdateRows.Count}");

                        if (matchingUpdateRows.Count == 0)
                        {
                            logBuilder.AppendLine($"{System.DateTime.Now:dd-MM-yyyy HH:mm:ss} - WARN - Для '{text}' не найдено строк справочника c subject_type={preferredSubject}. Пропуск.");
                            return false;
                        }
                    }

                    return true;
                }

                // 7) Построить набор уже импортированных ключей
                void BuildImportedKeys()
                {
                    importedKeys.Clear();
                    foreach (System.Data.DataRow ex in ReestrRKUpdate.Rows)
                    {
                        var req = ex["Номер заявки"]?.ToString()?.Trim() ?? string.Empty;
                        var ds = ex["document_set"]?.ToString()?.Trim() ?? string.Empty;
                        var st = ex["subject_type"]?.ToString()?.Trim() ?? string.Empty;
                        importedKeys.Add($"{req}|{ds}|{st}");
                    }
                }

                // 8) Основная обработка строк обновления
                void ProcessUpdateRows()
                {
                    foreach (var updRow in matchingUpdateRows)
                    {
                        var guid = System.Guid.NewGuid();
                        var guidDocumentId = System.Guid.NewGuid();

                        updRow["Номер заявки"] = requestNumber;

                        if (isChildSlot && hasParent && !string.IsNullOrEmpty(preferredSubject))
                        {
                            var complectKey = $"{requestNumber}|{preferredSubject}";
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

                        string documentSet = updRow["document_set"]?.ToString();
                        string subjectType = updRow["subject_type"]?.ToString();

                        string GUIDserviceNumber = updRow["GUID услуги"]?.ToString();
                        if (int.TryParse(GUIDserviceNumber, out int serviceNumber) && dictionaryGUIDservices.TryGetValue(serviceNumber, out string guidService))
                        {
                            updRow["contract_id"] = guidService;
                        }

                        string searchText = null;
                        System.Collections.Generic.List<string> fileIds = null;

                        System.Text.RegularExpressions.Regex regexSearchPasport;
                        if (text.Contains("anketa", System.StringComparison.Ordinal))
                        {
                            regexSearchPasport = new System.Text.RegularExpressions.Regex(
                                $@"(?!.*zatavl\w*)({System.Text.RegularExpressions.Regex.Escape(text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
                        }
                        else
                        {
                            regexSearchPasport = new System.Text.RegularExpressions.Regex(
                                $@"(^|[_\s])({System.Text.RegularExpressions.Regex.Escape(text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
                        }

                        bool hasTextFile = dtReestrFilesFiltered.AsEnumerable()
                            .Any(r =>
                                r["Номер заявки"]?.ToString() == requestNumber &&
                                !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                                regexSearchPasport.IsMatch(System.IO.Path.GetFileNameWithoutExtension(r["Путь к файлу"].ToString()))
                            );

                        if (passportSets.Contains(documentSet) && hasTextFile)
                        {
                            searchText = "pasport";
                            fileIds = new System.Collections.Generic.List<string>();
                            foreach (System.Data.DataRow r in dtReestrFilesFiltered.Rows)
                            {
                                if (r["Номер заявки"]?.ToString() == requestNumber &&
                                    !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                                    r["Путь к файлу"].ToString().IndexOf(searchText, System.StringComparison.OrdinalIgnoreCase) >= 0 &&
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
                        else if (documentSet?.Trim() == "PD0084" && subjectType?.Trim() == "BANK")
                        {
                            var filesWithUvedomlenie1 = new System.Collections.Generic.List<string>();
                            var filesWithUvedomlenie2 = new System.Collections.Generic.List<string>();
                            foreach (System.Data.DataRow r in dtReestrFilesFiltered.Rows)
                            {
                                if (r["Номер заявки"]?.ToString() == requestNumber &&
                                    !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                                    r["ID файла в СХФ"]?.ToString() != "error")
                                {
                                    var path = r["Путь к файлу"].ToString();
                                    var id = r["ID файла в СХФ"]?.ToString();
                                    if (!string.IsNullOrEmpty(id))
                                    {
                                        if (path.IndexOf("uvedomlenie1", System.StringComparison.OrdinalIgnoreCase) >= 0 && !filesWithUvedomlenie1.Contains(id))
                                            filesWithUvedomlenie1.Add(id);
                                        if (path.IndexOf("uvedomlenie2", System.StringComparison.OrdinalIgnoreCase) >= 0 && !filesWithUvedomlenie2.Contains(id))
                                            filesWithUvedomlenie2.Add(id);
                                    }
                                }
                            }
                            var allFiles = filesWithUvedomlenie1.Union(filesWithUvedomlenie2).ToList();
                            if (allFiles.Count > 0)
                                updRow["file_id"] = string.Join("|", allFiles);
                        }
                        else if (documentSet?.Trim() == "PD0084" && subjectType?.Trim() == "BROK")
                        {
                            var filesWithUvedomlenie3 = new System.Collections.Generic.List<string>();
                            var filesWithUvedomlenie4 = new System.Collections.Generic.List<string>();
                            foreach (System.Data.DataRow r in dtReestrFilesFiltered.Rows)
                            {
                                if (r["Номер заявки"]?.ToString() == requestNumber &&
                                    !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                                    r["ID файла в СХФ"]?.ToString() != "error")
                                {
                                    var path = r["Путь к файлу"].ToString();
                                    var id = r["ID файла в СХФ"]?.ToString();
                                    if (!string.IsNullOrEmpty(id))
                                    {
                                        if (path.IndexOf("uvedomlenie3", System.StringComparison.OrdinalIgnoreCase) >= 0 && !filesWithUvedomlenie3.Contains(id))
                                            filesWithUvedomlenie3.Add(id);
                                        if (path.IndexOf("uvedomlenie4", System.StringComparison.OrdinalIgnoreCase) >= 0 && !filesWithUvedomlenie4.Contains(id))
                                            filesWithUvedomlenie4.Add(id);
                                    }
                                }
                            }
                            var allFiles = filesWithUvedomlenie3.Union(filesWithUvedomlenie4).ToList();
                            if (allFiles.Count > 0)
                                updRow["file_id"] = string.Join("|", allFiles);
                        }
                        else if (documentSet?.Trim() == "PD0085" && subjectType?.Trim() == "BANK")
                        {
                            searchText = "ZayavleniyeBanka";
                            fileIds = new System.Collections.Generic.List<string>();
                            foreach (System.Data.DataRow r in dtReestrFilesFiltered.Rows)
                            {
                                if (r["Номер заявки"]?.ToString() == requestNumber &&
                                    !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                                    r["Путь к файлу"].ToString().IndexOf(searchText, System.StringComparison.OrdinalIgnoreCase) >= 0 &&
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
                        else if (documentSet?.Trim() == "PD0085" && subjectType?.Trim() == "BROK")
                        {
                            searchText = "ZayavleniyeKompaniya";
                            fileIds = new System.Collections.Generic.List<string>();
                            foreach (System.Data.DataRow r in dtReestrFilesFiltered.Rows)
                            {
                                if (r["Номер заявки"]?.ToString() == requestNumber &&
                                    !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                                    r["Путь к файлу"].ToString().IndexOf(searchText, System.StringComparison.OrdinalIgnoreCase) >= 0 &&
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
                            logBuilder.AppendLine($"{System.DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - ОБРАБОТКА documentSet == BN_DKBO0134");
                            var raspiskaFileIds = new System.Collections.Generic.List<string>();
                            foreach (System.Data.DataRow r in dtReestrFilesFiltered.Rows)
                            {
                                if (r["Номер заявки"]?.ToString() == requestNumber &&
                                    !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                                    r["Путь к файлу"].ToString().IndexOf("raspiska", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
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
                                    foreach (System.Data.DataColumn col in updRow.Table.Columns)
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

                            System.Text.RegularExpressions.Regex regexSearch;
                            if (text.Contains("anketa", System.StringComparison.Ordinal))
                            {
                                regexSearch = new System.Text.RegularExpressions.Regex(
                                    $@"(?!.*zatavl\w*)({System.Text.RegularExpressions.Regex.Escape(text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])",
                                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
                            }
                            else
                            {
                                regexSearch = new System.Text.RegularExpressions.Regex(
                                    $@"(^|[_\s])({System.Text.RegularExpressions.Regex.Escape(text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])",
                                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
                            }

                            fileIds = new System.Collections.Generic.List<string>();
                            foreach (System.Data.DataRow r in dtReestrFilesFiltered.Rows)
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
                                logBuilder.AppendLine($"{System.DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Пропуск дубля для ключа {key}");
                            }
                        }
                    }
                }

                // Последовательное выполнение
                InitializeContext();
                FindPrimaryMatch();
                if (!hasMatch) return;

                DetectParent();
                CacheMatchingUpdateRows();
                CacheRowsWithTextInFilePaths();
                if (rowsWithTextInFilePaths.Count == 0) return;

                if (!FilterByParentSubjectType()) return;

                BuildImportedKeys();
                ProcessUpdateRows();
            }
            catch (System.Exception ex)
            {
                logBuilder.AppendLine($"{System.DateTime.Now:dd-MM-yyyy HH:mm:ss} - ERR - {ex.Message}  {ex.StackTrace}  {ex.Data}  {ex.Source}");
            }
            finally
            {
                log = log + System.Environment.NewLine + logBuilder.ToString();
            }
        }

    }
}
