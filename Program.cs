using System.Data;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;
using static System.Net.Mime.MediaTypeNames;
using System.Text.Json;


namespace _958
{

    // Классы конфигурации — вне класса Program, на уровне namespace
    public class AppConfig
    {
        public List<string> PassportDocumentSets { get; set; } = new();
        public List<ParentSlotConfig> ParentSlots { get; set; } = new();
        public List<string> ChildSlots { get; set; } = new();
        public List<DocumentRule> DocumentRules { get; set; } = new();
        public List<FileIdSearchRule> FileIdSearchRules { get; set; } = new();
    }

    public class ParentSlotConfig
    {
        public string Slot { get; set; }
        public string SubjectType { get; set; }
        public int Priority { get; set; }
        public bool ExcludeZatavl { get; set; }
    }

    public class DocumentRule
    {
        public string ParentSlot { get; set; }
        public List<string> ChildSlots { get; set; }
        public string DocumentSet { get; set; }
        public string SubjectType { get; set; }
        public int IsOriginal { get; set; }
        public int ServiceTypeId { get; set; }
        public int BoProcessing { get; set; }
        public string ComplectName { get; set; }
        public bool Required { get; set; }
        public bool SharedComplectId { get; set; }
        public string SpecialAction { get; set; }
    }

    public class FileIdSearchRule
    {
        public string DocumentSet { get; set; }
        public string SubjectType { get; set; }
        public List<string> SearchTokens { get; set; } = new();
        public string Action { get; set; }
    }



    internal class Program
    {

        // Метод загрузки конфигурации — внутри Program
        public static AppConfig LoadConfig(string path)
        {
            var json = File.ReadAllText(path, Encoding.UTF8);
            return JsonSerializer.Deserialize<AppConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new AppConfig();
        }

        static void Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);


            // Примеры использования метода для разных файлов CSV
            DataTable reestrFilesTable = ReadCsvToDataTable("958_test_101225_reestrFiles.csv");
            //DataTable filteredFilesTable = ReadCsvToDataTable("dtReestrFilesFiltered.csv");

            // Получаем первое значение "Номер заявки"
            var firstRequestNumber = reestrFilesTable.AsEnumerable()
                .Select(r => r.Field<string>("Номер заявки"))
                .FirstOrDefault(num => !string.IsNullOrEmpty(num));

            // Фильтруем строки по этому значению
            DataTable filteredFilesTable = reestrFilesTable.AsEnumerable()
                .Where(r => r.Field<string>("Номер заявки") == firstRequestNumber)
                .CopyToDataTable();


            // Получаем JSON из первой непустой строки
            string jsonServices = filteredFilesTable.AsEnumerable()
                .Select(row => row.Field<string>("GUID услуги"))
                .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

            var dictionaryGUIDservices = new Dictionary<int, string>();

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


            DataTable bookReferenceTable = ReadCsvToDataTable("dtBookOfReferenceReestrRK.csv");

            // Вывод информации для демонстрации (например, количество строк)
            Console.WriteLine($"reestrFiles.csv: {reestrFilesTable.Rows.Count} строк");
            Console.WriteLine($"dtReestrFilesFiltered.csv: {filteredFilesTable.Rows.Count} строк");
            Console.WriteLine($"dtBookOfReferenceReestrRK.csv: {bookReferenceTable.Rows.Count} строк");

            //Добавить в bookReferenceTable колонки "Номер заявки", "master_id", "contract_id", "document_id", "file_id"
            bookReferenceTable.Columns.Add("Номер заявки", typeof(string));
            bookReferenceTable.Columns.Add("master_id", typeof(string));
            bookReferenceTable.Columns.Add("contract_id", typeof(string));
            bookReferenceTable.Columns.Add("document_id", typeof(string));
            bookReferenceTable.Columns.Add("file_id", typeof(string));



            //Создать DataTable ReestrRKUpdate и добавить в него колонки "Номер заявки" "document_set" "subject_type" "complect_name" "complect_id" "is_original" "file_id" "request_id" "event_time" "result BizTalk" "result Lotus" "Ссылка на РК" "Завершена обработка"
            DataTable ReestrRKUpdate = new DataTable();
            ReestrRKUpdate.Columns.Add("Номер заявки", typeof(string));
            ReestrRKUpdate.Columns.Add("document_set", typeof(string));
            ReestrRKUpdate.Columns.Add("subject_type", typeof(string));
            ReestrRKUpdate.Columns.Add("complect_name", typeof(string));
            ReestrRKUpdate.Columns.Add("complect_id", typeof(string));
            ReestrRKUpdate.Columns.Add("is_original", typeof(string));
            ReestrRKUpdate.Columns.Add("file_id", typeof(string));
            ReestrRKUpdate.Columns.Add("request_id", typeof(string));
            ReestrRKUpdate.Columns.Add("event_time", typeof(string));
            ReestrRKUpdate.Columns.Add("result BizTalk", typeof(string));
            ReestrRKUpdate.Columns.Add("result Lotus", typeof(string));
            ReestrRKUpdate.Columns.Add("Ссылка на РК", typeof(string));
            ReestrRKUpdate.Columns.Add("Завершена обработка", typeof(string));

            //получить список по колонке "Текст" из bookReferenceTable
            // Создаем список уникальных текстов из колонки "Текст"
            var uniqueTexts = bookReferenceTable.AsEnumerable()
                .Select(row => row.Field<string>("Текст"))
                .Where(text => !string.IsNullOrEmpty(text))
                .Distinct()
                .ToList();

            //Для каждой строки в dtReestrFilesFiltered
            foreach (DataRow rowUniq in filteredFilesTable.Rows)
            {

                //Для каждой строки в bookReferenceTable
                foreach (var text in uniqueTexts)
                {
                    /*// Получаем значение из колонки "Текст"
                    string text = row["Текст"].ToString();*/
                    // Проверяем, что текст не пустой
                    if (!string.IsNullOrEmpty(text))
                    {
                        Console.WriteLine($"Обрабатываем текст: {text}");

                        //объявить переменную для лога
                        string log = string.Empty;

                        // Получаем значение service_type, которое != 1, из словаря dictionaryGUIDservices
                        var serviceTypeNotEqualToOne = dictionaryGUIDservices
                            .Where(kvp => kvp.Key != 1)
                            .Select(kvp => kvp.Key)
                            .FirstOrDefault(); // Берем первое значение, если их несколько

                        if (serviceTypeNotEqualToOne != 0) // Проверяем, что значение найдено
                        {
                            // Ищем строки в bookReferenceTable, где "Текст" == 'uvedomlenie3' и "document_set" == 'PD0084'
                            var rowsToUpdate = bookReferenceTable.AsEnumerable()
                                .Where(row =>
                                    (row.Field<string>("Текст") == "uvedomlenie3" && row.Field<string>("document_set") == "PD0084") ||
                                    (row.Field<string>("Текст") == "uvedomlenie4" && row.Field<string>("document_set") == "PD0084"));

                            // Устанавливаем значение service_type в поле "GUID услуги" для найденных строк
                            foreach (var row in rowsToUpdate)
                            {
                                row["GUID услуги"] = serviceTypeNotEqualToOne.ToString();
                            }
                        }
                        else
                        {
                            Console.WriteLine("Значение service_type, отличное от 1, не найдено в словаре dictionaryGUIDservices.");
                        }



                        //Вызываем метод FillReestrRK_NEW для обработки текста
                        //FillReestrRK_NEW(filteredFilesTable, bookReferenceTable, rowUniq, ReadDictionaryGUIDServices("dictionaryGUIDservices.csv"), ref log, text, ReestrRKUpdate);
                        //FillReestrRK_optimaize(filteredFilesTable, bookReferenceTable, rowUniq, dictionaryGUIDservices, ref log, text, ReestrRKUpdate);

                        //Вызвать метод FillReestrRK_NEW
                        FillReestrRK_NEW(filteredFilesTable, bookReferenceTable, rowUniq, dictionaryGUIDservices, ref log, text, ReestrRKUpdate);
                    }
                    else
                    {
                        Console.WriteLine("Пустой текст, пропускаем обработку.");
                    }
                }

            }


        }               



        public static string GetFileNameWithoutExtension(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return string.Empty;
            // Получаем имя файла без расширения
            return Path.GetFileNameWithoutExtension(filePath);
        }


        public static DataTable ReadCsvToDataTable(string filePath)
        {
            DataTable dt = new DataTable();
            // Определяем используемую кодировку. Если файлы в Windows-1251, то:
            Encoding encoding = Encoding.GetEncoding("windows-1251");

            using (TextFieldParser parser = new TextFieldParser(filePath, encoding))
            {
                parser.TextFieldType = FieldType.Delimited;
                parser.SetDelimiters(";");
                parser.HasFieldsEnclosedInQuotes = true;

                bool isHeader = true;
                while (!parser.EndOfData)
                {
                    string[] fields = parser.ReadFields();

                    if (isHeader)
                    {
                        foreach (string header in fields)
                        {
                            dt.Columns.Add(header);
                        }
                        isHeader = false;
                    }
                    else
                    {
                        dt.Rows.Add(fields);
                    }
                }
            }

            return dt;
        }

        public static void FillReestrRK_optimaize(DataTable dtReestrFilesFiltered, DataTable dtBookOfReferenceReestrRK, DataRow rowUniqNumber, Dictionary<int, string> dictionaryGUIDservices, ref string log, string text, DataTable ReestrRKUpdate)
        {
            var logBuilder = new System.Text.StringBuilder();
            try
            {
                // Кэшируем значения для ускорения доступа
                string requestNumber = rowUniqNumber["Номер заявки"].ToString();
                string guidEBA = null;
                foreach (DataRow row in dtReestrFilesFiltered.Rows)
                {
                    guidEBA = row["GUID ЕВА клиента"]?.ToString();
                    if (!string.IsNullOrEmpty(guidEBA)) break;
                }

                // Предварительно компилируем регулярные выражения
                System.Text.RegularExpressions.Regex regexMain = null, regexAlt = null;
                if (!string.IsNullOrEmpty(text))
                {
                    if (text.Contains("anketa", StringComparison.Ordinal))
                    {
                        regexAlt = new System.Text.RegularExpressions.Regex($@"(^|[_\s])({System.Text.RegularExpressions.Regex.Escape(text)}_zatavl)(\d{{1,3}})?($|[_\s])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
                        regexMain = new System.Text.RegularExpressions.Regex($@"(^|[_\s])(?!.*\bzatavl\b)({System.Text.RegularExpressions.Regex.Escape(text)})(\d{{1,3}})?($|[_\s])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
                    }
                    else
                    {
                        regexMain = new System.Text.RegularExpressions.Regex($@"(^|[_\s])({System.Text.RegularExpressions.Regex.Escape(text)})(\d{{1,3}})?($|[_\s])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
                    }
                }

                // Поиск совпадения
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
                        {
                            isMatch = regexAlt.IsMatch(fileName);
                        }
                        else
                        {
                            isMatch = regexMain.IsMatch(fileName);
                        }
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

                if (!hasMatch) return;


                //Родитель начало
                // Определение наличия родителя и его subject_type (точные шаблоны и приоритет)
                bool hasParent = false;
                string preferredSubject = null;

                // Локальная фабрика якорных шаблонов с опциональным исключением zatavl для чистого "anketa"
                static Regex Anchored(string token, bool excludeZatavl = false)
                {
                    var safe = Regex.Escape(token);
                    var negative = excludeZatavl ? "(?!.*zatavl\\w*)" : "";
                    return new Regex($@"(^|[_\s]){negative}{safe}(\d{{1,3}})?($|[_\s])",
                        RegexOptions.IgnoreCase | RegexOptions.Compiled);
                }

                // Более специфичные — раньше, чтобы не поймать "anketa" в "AnketaBroker/AnketaBank"
                var parentPatterns = new (string subject, Regex pattern)[]
                {
                    ("BROK", Anchored("AnketaBroker")),
                    ("BANK", Anchored("AnketaBank")),
                    ("BANK", Anchored("anketa_zatavl")),
                    ("BANK", Anchored("anketa", excludeZatavl: true)),
                    ("EDO",  Anchored("zayаvlenieakcept")),
                    ("EDO",  Anchored("zayavlenie")),
                };

                foreach (DataRow r in dtReestrFilesFiltered.Rows)
                {
                    if (!string.Equals(r["Номер заявки"]?.ToString(), requestNumber, StringComparison.Ordinal)) continue;

                    var name = Path.GetFileNameWithoutExtension(r["Путь к файлу"]?.ToString() ?? "");
                    if (string.IsNullOrEmpty(name)) continue;

                    foreach (var (subject, pattern) in parentPatterns)
                    {
                        if (pattern.IsMatch(name))
                        {
                            hasParent = true;
                            preferredSubject ??= subject; // фиксируем первый по приоритету
                            break;
                        }
                    }
                    if (preferredSubject != null) break;
                }

                logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Родитель найден: {hasParent}, subject_type={preferredSubject ?? "-"}");

                

                // Кэшируем строки для обновления
                var matchingUpdateRows = new List<DataRow>();
                foreach (DataRow updateRow in dtBookOfReferenceReestrRK.Rows)
                {
                    var rowText = updateRow["Текст"]?.ToString();
                    if (!string.IsNullOrEmpty(rowText) && rowText.Equals(text, StringComparison.OrdinalIgnoreCase))
                        matchingUpdateRows.Add(updateRow);
                }

                // Кэшируем строки с совпадением по тексту в путях файлов
                var rowsWithTextInFilePaths = new List<DataRow>();
                Regex regexFilePath = null;
                if (text.Contains("anketa"))
                {
                    //regexFilePath = new Regex($@"(^|[_\s])(?!.*zatavl\w*)({Regex.Escape(text)})(\d{{1,3}})?($|[_\s])", RegexOptions.Compiled);
                    regexFilePath = new System.Text.RegularExpressions.Regex($@"(?!.*zatavl\w*)({System.Text.RegularExpressions.Regex.Escape(text)})(\d{{1,3}})?", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

                }
                else
                {
                    regexFilePath = new System.Text.RegularExpressions.Regex($@"(^|[_\s])({System.Text.RegularExpressions.Regex.Escape(text)})(\d{{1,3}})?($|[_\s])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
                }

                foreach (DataRow filteredRow in dtReestrFilesFiltered.Rows)
                {
                    var rowText = filteredRow["Путь к файлу"]?.ToString();
                    if (string.IsNullOrEmpty(rowText)) continue;
                    string fileName = Path.GetFileNameWithoutExtension(rowText);
                    if (regexFilePath.IsMatch(fileName))
                        rowsWithTextInFilePaths.Add(filteredRow);
                }

                logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Количество строк в matchingUpdateRows для {text} = {matchingUpdateRows.Count}");
                logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Количество строк в rowsWithTextInFilePaths для {text} = {rowsWithTextInFilePaths.Count}");

                //Родитель начало

                // Определяем, относится ли текущий text к дочерним слотам
                bool isChildSlot = text.Equals("uvedomlenie1", StringComparison.OrdinalIgnoreCase)
                    || text.Equals("uvedomlenie2", StringComparison.OrdinalIgnoreCase)
                    || text.Equals("uvedomlenie3", StringComparison.OrdinalIgnoreCase)
                    || text.Equals("uvedomlenie4", StringComparison.OrdinalIgnoreCase)
                    || text.Equals("ZayavleniyeBanka", StringComparison.OrdinalIgnoreCase)
                    || text.Equals("ZayavleniyeKompaniya", StringComparison.OrdinalIgnoreCase)
                    || text.Equals("registration", StringComparison.OrdinalIgnoreCase);

                // Если родитель найден и это дочерний слот — фильтруем по subject_type родителя
                if (isChildSlot && hasParent && !string.IsNullOrEmpty(preferredSubject))
                {
                    matchingUpdateRows = matchingUpdateRows
                        .Where(row => string.Equals(row["subject_type"]?.ToString()?.Trim(), preferredSubject, StringComparison.OrdinalIgnoreCase))
                        .ToList();

                    logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - matchingUpdateRows отфильтрован по subject_type={preferredSubject}, rows={matchingUpdateRows.Count}");

                    // Если после фильтрации ничего не осталось — фиксируем и выходим
                    if (matchingUpdateRows.Count == 0)
                    {
                        logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - WARN - Для '{text}' не найдено строк справочника c subject_type={preferredSubject}. Пропуск.");
                        return;
                    }
                }

                //Родитель конец




                if (rowsWithTextInFilePaths.Count == 0) return;

                // Кэшируем список документов для ускорения Contains
                var passportSets = new HashSet<string> { "BN_DKBO0132", "BN_DKBO0048", "EDO0019", "BK1444", "DU0080", "PD0075" };


                var complectCache = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

                // Построим локальный набор уже импортированных ключей на основе ReestrRKUpdate
                var importedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (DataRow ex in ReestrRKUpdate.Rows)
                {
                    var req = ex["Номер заявки"]?.ToString()?.Trim() ?? string.Empty;
                    var ds = ex["document_set"]?.ToString()?.Trim() ?? string.Empty;
                    var st = ex["subject_type"]?.ToString()?.Trim() ?? string.Empty;
                    importedKeys.Add($"{req}|{ds}|{st}");
                }

                foreach (var updRow in matchingUpdateRows)
                {
                    var guid = Guid.NewGuid();
                    var guidDocumentId = Guid.NewGuid();

                    updRow["Номер заявки"] = requestNumber;


                    // Если это дочерний слот и есть родитель — переиспользуем единый complect_id
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
                    List<string> fileIds = null;




                    // Проверяем, есть ли хотя бы один файл с text (по regex)
                    Regex regexSearchPasport = null;
                    if (text.Contains("anketa"))
                    {

                        regexSearchPasport = new System.Text.RegularExpressions.Regex($@"(?!.*zatavl\w*)({System.Text.RegularExpressions.Regex.Escape(text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

                    }
                    else
                    {
                        regexSearchPasport = new System.Text.RegularExpressions.Regex($@"(^|[_\s])({System.Text.RegularExpressions.Regex.Escape(text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
                    }


                    bool hasTextFile = dtReestrFilesFiltered.AsEnumerable()
                        .Any(r =>
                            r["Номер заявки"]?.ToString() == requestNumber &&
                            !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                            regexSearchPasport.IsMatch(Path.GetFileNameWithoutExtension(r["Путь к файлу"].ToString()))
                        );



                    if (passportSets.Contains(documentSet) && hasTextFile)
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
                    else if (documentSet?.Trim() == "PD0084" && subjectType?.Trim() == "BANK")
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
                    else if (documentSet?.Trim() == "PD0084" && subjectType?.Trim() == "BROK")
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
                    else if (documentSet?.Trim() == "PD0085" && subjectType?.Trim() == "BANK")
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
                    else if (documentSet?.Trim() == "PD0085" && subjectType?.Trim() == "BROK")
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

                        System.Text.RegularExpressions.Regex regexSearch = null;
                        if (text.Contains("anketa"))
                        {

                            regexSearch = new System.Text.RegularExpressions.Regex($@"(?!.*zatavl\w*)({System.Text.RegularExpressions.Regex.Escape(text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

                        }
                        else
                        {
                            regexSearch = new System.Text.RegularExpressions.Regex($@"(^|[_\s])({System.Text.RegularExpressions.Regex.Escape(text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
                        }


                        fileIds = new List<string>();
                        foreach (DataRow r in dtReestrFilesFiltered.Rows)
                        {
                            if (r["Номер заявки"]?.ToString() != requestNumber ||
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


                    //Если documentSet != "BN_DKBO0134"  и updRow["file_id"] не пустой, то импортируем строку один раз на ключ заявки+набор+субъект
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
            catch (Exception ex)
            {
                logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - ERR - {ex.Message}  {ex.StackTrace}  {ex.Data}  {ex.Source}");
            }
            finally
            {
                log = log + Environment.NewLine + logBuilder.ToString();
            }
        }

        public static void FiilReestrRK_opimaizeOLD(DataTable dtReestrFilesFiltered, DataTable dtBookOfReferenceReestrRK, DataRow rowUniqNumber, Dictionary<int, string> dictionaryGUIDservices, ref string log, string text, DataTable ReestrRKUpdate)
        {
            var logBuilder = new System.Text.StringBuilder();
            try
            {
                // Кэшируем значения для ускорения доступа
                string requestNumber = rowUniqNumber["Номер заявки"].ToString();
                string guidEBA = null;
                foreach (DataRow row in dtReestrFilesFiltered.Rows)
                {
                    guidEBA = row["GUID ЕВА клиента"]?.ToString();
                    if (!string.IsNullOrEmpty(guidEBA)) break;
                }
                // Предварительно компилируем регулярные выражения
                System.Text.RegularExpressions.Regex regexMain = null, regexAlt = null;
                if (!string.IsNullOrEmpty(text))
                {
                    if (text.Contains("anketa", StringComparison.Ordinal))
                    {
                        regexAlt = new System.Text.RegularExpressions.Regex($@"(^|[_\s])({System.Text.RegularExpressions.Regex.Escape(text)}_zatavl)(\d{{1,3}})?($|[_\s])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
                        regexMain = new System.Text.RegularExpressions.Regex($@"(^|[_\s])(?!.*\bzatavl\b)({System.Text.RegularExpressions.Regex.Escape(text)})(\d{{1,3}})?($|[_\s])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
                    }
                    else
                    {
                        regexMain = new System.Text.RegularExpressions.Regex($@"(^|[_\s])({System.Text.RegularExpressions.Regex.Escape(text)})(\d{{1,3}})?($|[_\s])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
                    }
                }

                // Поиск совпадения
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
                        {
                            isMatch = regexAlt.IsMatch(fileName);
                        }
                        else
                        {
                            isMatch = regexMain.IsMatch(fileName);
                        }
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

                if (!hasMatch) return;

                // Кэшируем строки для обновления
                var matchingUpdateRows = new List<DataRow>();
                foreach (DataRow updateRow in dtBookOfReferenceReestrRK.Rows)
                {
                    var rowText = updateRow["Текст"]?.ToString();
                    if (!string.IsNullOrEmpty(rowText) && rowText.Equals(text, StringComparison.OrdinalIgnoreCase))
                        matchingUpdateRows.Add(updateRow);
                }

                // Кэшируем строки с совпадением по тексту в путях файлов
                var rowsWithTextInFilePaths = new List<DataRow>();
                System.Text.RegularExpressions.Regex regexFilePath = null;
                if (text.Contains("anketa"))
                {

                    regexFilePath = new System.Text.RegularExpressions.Regex($@"(?!.*zatavl\w*)({System.Text.RegularExpressions.Regex.Escape(text)})(\d{{1,3}})?", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

                }
                else
                {
                    regexFilePath = new System.Text.RegularExpressions.Regex($@"(^|[_\s])({System.Text.RegularExpressions.Regex.Escape(text)})(\d{{1,3}})?($|[_\s])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
                }

                foreach (DataRow filteredRow in dtReestrFilesFiltered.Rows)
                {
                    var rowText = filteredRow["Путь к файлу"]?.ToString();
                    if (string.IsNullOrEmpty(rowText)) continue;
                    string fileName = Path.GetFileNameWithoutExtension(rowText);
                    if (regexFilePath.IsMatch(fileName))
                        rowsWithTextInFilePaths.Add(filteredRow);
                }

                logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Количество строк в matchingUpdateRows для {text} = {matchingUpdateRows.Count}");
                logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Количество строк в rowsWithTextInFilePaths для {text} = {rowsWithTextInFilePaths.Count}");

                if (rowsWithTextInFilePaths.Count == 0) return;

                // Кэшируем список документов для ускорения Contains
                var passportSets = new HashSet<string> { "BN_DKBO0132", "BN_DKBO0048", "EDO0019", "BK1444", "DU0080", "PD0075" };

                var importedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (DataRow ex in ReestrRKUpdate.Rows)
                {
                    var req = ex["Номер заявки"]?.ToString()?.Trim() ?? string.Empty;

                    var ds = ex["document_set"]?.ToString()?.Trim() ?? string.Empty;

                    var st = ex["subject_type"]?.ToString()?.Trim() ?? string.Empty;

                    importedKeys.Add($"{req}|{ds}|{st}");

                }

                foreach (var updRow in matchingUpdateRows)
                {
                    var guid = Guid.NewGuid();
                    var guidDocumentId = Guid.NewGuid();

                    updRow["Номер заявки"] = requestNumber;
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
                    List<string> fileIds = null;




                    // Проверяем, есть ли хотя бы один файл с text (по regex)
                    System.Text.RegularExpressions.Regex regexSearchPasport = null;
                    if (text.Contains("anketa"))
                    {

                        regexSearchPasport = new System.Text.RegularExpressions.Regex($@"(?!.*zatavl\w*)({System.Text.RegularExpressions.Regex.Escape(text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

                    }
                    else
                    {
                        regexSearchPasport = new System.Text.RegularExpressions.Regex($@"(^|[_\s])({System.Text.RegularExpressions.Regex.Escape(text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
                    }


                    bool hasTextFile = dtReestrFilesFiltered.AsEnumerable()
                        .Any(r =>
                            r["Номер заявки"]?.ToString() == requestNumber &&
                            !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                            regexSearchPasport.IsMatch(Path.GetFileNameWithoutExtension(r["Путь к файлу"].ToString()))
                        );



                    if (passportSets.Contains(documentSet) && hasTextFile)
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
                    else if (documentSet?.Trim() == "PD0084" && subjectType?.Trim() == "BANK")
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
                    else if (documentSet?.Trim() == "PD0084" && subjectType?.Trim() == "BROK")
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
                    else if (documentSet?.Trim() == "PD0085" && subjectType?.Trim() == "BANK")
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
                    else if (documentSet?.Trim() == "PD0085" && subjectType?.Trim() == "BROK")
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

                        System.Text.RegularExpressions.Regex regexSearch = null;
                        if (text.Contains("anketa"))
                        {

                            regexSearch = new System.Text.RegularExpressions.Regex($@"(?!.*zatavl\w*)({System.Text.RegularExpressions.Regex.Escape(text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

                        }
                        else
                        {
                            regexSearch = new System.Text.RegularExpressions.Regex($@"(^|[_\s])({System.Text.RegularExpressions.Regex.Escape(text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
                        }


                        fileIds = new List<string>();
                        foreach (DataRow r in dtReestrFilesFiltered.Rows)
                        {
                            if (r["Номер заявки"]?.ToString() != requestNumber ||
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


                    //Если documentSet != "BN_DKBO0134" и updRow["file_id"] не пустой, то импортируем строку один раз на ключ заявки+набор+субъект 
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
            catch (Exception ex)
            {
                logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - ERR - {ex.Message}  {ex.StackTrace}  {ex.Data}  {ex.Source}");
            }
            finally
            {
                log = log + Environment.NewLine + logBuilder.ToString();
            }
        }


        public static void FillReestrRK_NEW(DataTable dtReestrFilesFiltered, DataTable dtBookOfReferenceReestrRK, DataRow rowUniqNumber, Dictionary<int, string> dictionaryGUIDservices, ref string log, string text, DataTable ReestrRKUpdate)
        {
            var logBuilder = new System.Text.StringBuilder();
            try
            {
                string requestNumber = rowUniqNumber["Номер заявки"].ToString();
                string guidEBA = null;
                foreach (DataRow row in dtReestrFilesFiltered.Rows)
                {
                    guidEBA = row["GUID ЕВА клиента"]?.ToString();
                    if (!string.IsNullOrEmpty(guidEBA)) break;
                }

                System.Text.RegularExpressions.Regex regexMain = null, regexAlt = null;
                if (!string.IsNullOrEmpty(text))
                {
                    if (text.Contains("anketa", StringComparison.Ordinal))
                    {
                        regexAlt = new System.Text.RegularExpressions.Regex($@"(^|[_\s])({System.Text.RegularExpressions.Regex.Escape(text)}_zatavl)(\d{{1,3}})?($|[_\s])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
                        regexMain = new System.Text.RegularExpressions.Regex($@"(^|[_\s])(?!.*\bzatavl\b)({System.Text.RegularExpressions.Regex.Escape(text)})(\d{{1,3}})?($|[_\s])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
                    }
                    else
                    {
                        regexMain = new System.Text.RegularExpressions.Regex($@"(^|[_\s])({System.Text.RegularExpressions.Regex.Escape(text)})(\d{{1,3}})?($|[_\s])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
                    }
                }

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
                        {
                            isMatch = regexAlt.IsMatch(fileName);
                        }
                        else
                        {
                            isMatch = regexMain.IsMatch(fileName);
                        }
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

                if (!hasMatch) return;

                var parentSubjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var foundParentSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                static Regex Anchored(string token, bool excludeZatavl = false)
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
                    // строгая anketa как отдельный слот
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

                bool hasParent = parentSubjects.Count > 0;
                var parentSubjectsForLog = hasParent ? string.Join(",", parentSubjects) : "-";
                var foundParentSlotsForLog = foundParentSlots.Count > 0 ? string.Join(",", foundParentSlots) : "-";
                logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Родитель найден: {hasParent}, subject_type={parentSubjectsForLog}, slots={foundParentSlotsForLog}");

                /*var matchingUpdateRows = new List<DataRow>();
                foreach (DataRow updateRow in dtBookOfReferenceReestrRK.Rows)
                {
                    var rowText = updateRow["Текст"]?.ToString();
                    if (!string.IsNullOrEmpty(rowText) && rowText.Equals(text, StringComparison.OrdinalIgnoreCase))
                        matchingUpdateRows.Add(updateRow);
                }*/

                var matchingUpdateRows = dtBookOfReferenceReestrRK.AsEnumerable()
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
                            // EDO: родитель zayavlenie / zayvlenieakcept → PD0085/EDO
                            if (isEdoParent && docSet == "PD0085" && subject == "EDO")
                                return true;

                            return false;
                        }

                        return true;
                    })
                    .ToList();

                var rowsWithTextInFilePaths = new List<DataRow>();
                Regex regexFilePath = null;
                if (text.Contains("anketa"))
                {
                    regexFilePath = new Regex($@"(?!.*zatavl\\w*)({Regex.Escape(text)})(\d{{1,3}})?", RegexOptions.IgnoreCase | RegexOptions.Compiled);
                }
                else
                {
                    regexFilePath = new Regex($@"(^|[_\s])({Regex.Escape(text)})(\d{{1,3}})?($|[_\s])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
                }

                foreach (DataRow filteredRow in dtReestrFilesFiltered.Rows)
                {
                    var rowText = filteredRow["Путь к файлу"]?.ToString();
                    if (string.IsNullOrEmpty(rowText)) continue;
                    string fileName = Path.GetFileNameWithoutExtension(rowText);
                    if (regexFilePath.IsMatch(fileName))
                        rowsWithTextInFilePaths.Add(filteredRow);
                }

                logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Количество строк в matchingUpdateRows для {text} = {matchingUpdateRows.Count}");
                logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Количество строк в rowsWithTextInFilePaths для {text} = {rowsWithTextInFilePaths.Count}");

                bool isChildSlot = text.Equals("uvedomlenie1", StringComparison.OrdinalIgnoreCase)
                    || text.Equals("uvedomlenie2", StringComparison.OrdinalIgnoreCase)
                    || text.Equals("uvedomlenie3", StringComparison.OrdinalIgnoreCase)
                    || text.Equals("uvedomlenie4", StringComparison.OrdinalIgnoreCase)
                    || text.Equals("ZayavleniyeBanka", StringComparison.OrdinalIgnoreCase)
                    || text.Equals("ZayavleniyeKompaniya", StringComparison.OrdinalIgnoreCase)
                    || text.Equals("registration", StringComparison.OrdinalIgnoreCase);

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

                    // Если после фильтрации ничего не осталось — фиксируем и выходим
                    if (matchingUpdateRows.Count == 0)
                    {
                        logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - WARN - Для '{text}' не найдено строк справочника c subject_type={parentSubjectsForLog}. Пропуск.");
                        return;
                    }
                }

                if (rowsWithTextInFilePaths.Count == 0) return;

                var passportSets = new HashSet<string> { "BN_DKBO0132", "BN_DKBO0048", "EDO0019", "BK1444", "DU0080", "PD0075" };


                var complectCache = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

                var importedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (DataRow ex in ReestrRKUpdate.Rows)
                {
                    var req = ex["Номер заявки"]?.ToString()?.Trim() ?? string.Empty;
                    var ds = ex["document_set"]?.ToString()?.Trim() ?? string.Empty;
                    var st = ex["subject_type"]?.ToString()?.Trim() ?? string.Empty;
                    importedKeys.Add($"{req}|{ds}|{st}");
                }

                foreach (var updRow in matchingUpdateRows)
                {
                    var guid = Guid.NewGuid();
                    var guidDocumentId = Guid.NewGuid();

                    updRow["Номер заявки"] = requestNumber;

                    string documentSet = updRow["document_set"]?.ToString();
                    string subjectType = updRow["subject_type"]?.ToString();
                    var normalizedSubjectType = subjectType?.Trim();

                    if (isChildSlot && hasParent && !string.IsNullOrEmpty(normalizedSubjectType) && parentSubjects.Contains(normalizedSubjectType))
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
                        regexSearchPasport = new Regex($@"(^|[_\s])({Regex.Escape(text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
                    }

                    bool hasTextFile = dtReestrFilesFiltered.AsEnumerable()
                        .Any(r =>
                            r["Номер заявки"]?.ToString() == requestNumber &&
                            !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                            regexSearchPasport.IsMatch(Path.GetFileNameWithoutExtension(r["Путь к файлу"].ToString()))
                        );


                    /*if (passportSets.Contains(documentSet) && hasTextFile)
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
                            updRow["file_id"] = string.Join("|", fileIds);*/

                    if (passportSets.Contains(documentSet) && hasTextFile)
                    {
                        // Правила из кЗадаче.csv: какой родительский слот даёт право на какой passport document_set
                        // parentSlot, document_set, ожидаемый subject_type
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

                        // Ищем правило, которое разрешает создание данного passport-набора
                        var rule = passportParentRules.FirstOrDefault(r =>
                            r.DocumentSet.Equals(normDocSet, StringComparison.OrdinalIgnoreCase) &&
                            (string.IsNullOrEmpty(r.SubjectType) ||
                                r.SubjectType.Equals(normSubject, StringComparison.OrdinalIgnoreCase)) &&
                            foundParentSlots.Contains(r.ParentSlot));

                        if (rule.DocumentSet == null)
                        {
                            // Нет подходящего родителя — пропускаем этот passport-комплект
                            logBuilder.AppendLine(
                                $"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Пропуск {documentSet} ({subjectType}) для заявки {requestNumber}: не найден родительский слот для passport (rules from kZadache.csv)");
                        }
                        else
                        {
                            // Родитель найден по правилам — привязываем все pasport* файлы
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
                        // Для EDO по кЗадаче: registration → PD0085/EDO, должны брать registration_*
                        searchText = text.Equals("registration", StringComparison.OrdinalIgnoreCase)
                            ? "registration"
                            : "ZayavleniyeKompaniya"; // теоретически сюда не попадём для других слотов, но оставим на всякий случай

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
                        //searchText = "ZayavleniyeKompaniya";
                        searchText = text.Equals("registration", StringComparison.OrdinalIgnoreCase)
                            ? "registration"
                            : "ZayavleniyeKompaniya";

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
                        searchText = text.Equals("registration", StringComparison.OrdinalIgnoreCase)
                            ? "registration"
                            : "ZayavleniyeBanka";

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

                        System.Text.RegularExpressions.Regex regexSearch = null;
                        if (text.Contains("anketa"))
                        {

                            regexSearch = new System.Text.RegularExpressions.Regex($@"(?!.*zatavl\w*)({System.Text.RegularExpressions.Regex.Escape(text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

                        }
                        else
                        {
                            regexSearch = new System.Text.RegularExpressions.Regex($@"(^|[_\s])({System.Text.RegularExpressions.Regex.Escape(text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);
                        }


                        fileIds = new List<string>();
                        foreach (DataRow r in dtReestrFilesFiltered.Rows)
                        {
                            if (r["Номер заявки"]?.ToString() != requestNumber ||
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


                    //Если documentSet != "BN_DKBO0134" и updRow["file_id"] не пустой, то импортируем строку один раз на ключ заявки+набор+субъект 
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
            catch (Exception ex)
            {
                logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - ERR - {ex.Message}  {ex.StackTrace}  {ex.Data}  {ex.Source}");
            }
            finally
            {
                log = log + Environment.NewLine + logBuilder.ToString();
            }
        }






        public static Dictionary<int, string> ReadDictionaryGUIDServices(string filePath)
        {
            var dictionary = new Dictionary<int, string>();
            // Используем кодировку windows-1251 для корректного чтения русских символов
            Encoding encoding = Encoding.GetEncoding("windows-1251");

            using (TextFieldParser parser = new TextFieldParser(filePath, encoding))
            {
                parser.TextFieldType = FieldType.Delimited;
                parser.SetDelimiters(";");
                parser.HasFieldsEnclosedInQuotes = true;

                bool isHeader = true;
                while (!parser.EndOfData)
                {
                    string[] fields = parser.ReadFields();
                    if (isHeader)
                    {
                        // Пропускаем заголовок
                        isHeader = false;
                        continue;
                    }
                    if (fields.Length >= 2 && int.TryParse(fields[0], out int key))
                    {
                        dictionary[key] = fields[1];
                    }
                }
            }
            return dictionary;
        }

        public static void ProcessJsonResponse(
            string Value,
            DataTable dtReestrRK,
            DataTable dtReestrFiles,
            ref string log,
            ref string errorMessage)
        {
            var logBuilder = new System.Text.StringBuilder();
            try
            {
                // 1. Парсим JSON-ответ
                using var doc = System.Text.Json.JsonDocument.Parse(Value);
                var root = doc.RootElement;

                string requestId = root.GetProperty("body").GetProperty("request_id").GetString();

                logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - requestId: {requestId}");

                // Проверяем наличие дополнительных полей
                string dataBaseId = root.GetProperty("body").TryGetProperty("data_base_id", out var dbIdProp) ? dbIdProp.GetString() : null;

                logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - dataBaseId: {dataBaseId}");

                string unidRegCard = root.GetProperty("body").TryGetProperty("unid_registration_card", out var unidProp) ? unidProp.GetString() : null;

                logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - unidRegCard: {unidRegCard}");

                if (!string.IsNullOrEmpty(requestId))
                {
                    // 2. Находим строку в dtReestrRK по request_id   
                    var targetRow = dtReestrRK.AsEnumerable()
                        .FirstOrDefault(row => row.Field<string>("request_id") == requestId);

                    if (targetRow != null)
                    {
                        logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - в Реестре РК найдена строка с соответствующим ответом сервиса request_id");

                        // 3. Если есть unid_registration_card — нормальная обработка
                        if (!string.IsNullOrEmpty(unidRegCard))
                        {
                            targetRow.SetField("result Lotus", "sucess");

                            string unidRegCardResult = unidRegCard.Split('#').First();

                            logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - unidRegCardResult {unidRegCardResult}");

                            targetRow.SetField("Ссылка на РК", $"notes://Server/{dataBaseId}/0/{unidRegCardResult}");

                            // 4. Работаем с file_id (старый путь — при наличии unid_registration_card)
                            string fileIdsSuccess = targetRow.Field<string>("file_id");

                            logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - значение поля fileIds: {fileIdsSuccess}");

                            string documentSetSuccess = targetRow.Field<string>("document_set");

                            logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - значение поля documentSet: {documentSetSuccess}");

                            if (!string.IsNullOrEmpty(fileIdsSuccess) && !string.IsNullOrEmpty(documentSetSuccess))
                            {
                                logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - значение поля documentSet и fileIds -  не пустые");

                                var fileIdList = fileIdsSuccess.Split('|').Select(f => f.Trim()).Where(f => !string.IsNullOrEmpty(f));

                                foreach (var fileId in fileIdList)
                                {
                                    logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - значение поля fileId -  {fileId}");

                                    // Найти строку в dtReestrFiles по "ID файла в СХФ"
                                    var fileRow = dtReestrFiles.AsEnumerable()
                                        .FirstOrDefault(fr => fr.Field<string>("ID файла в СХФ").Contains(fileId));

                                    if (fileRow != null)
                                    {
                                        logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - строка fileRow найдена");
                                    }
                                    else
                                    {
                                        logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - строка fileRow НЕ найдена");
                                    }

                                    // Найти столбец в dtReestrFiles с заголовком, равным значению document_set
                                    if (dtReestrFiles.Columns.Contains(documentSetSuccess))
                                    {
                                        logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - таблица dtReestrFiles содержит колонку -  {documentSetSuccess}");

                                        if (fileRow != null)
                                            fileRow.SetField(documentSetSuccess, "success");

                                        logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - для колонки {documentSetSuccess} установлен статус ");
                                    }
                                }
                            }
                            else
                            {
                                logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - ERR - fileIds: {fileIdsSuccess} && documentSet: {documentSetSuccess}");
                            }
                        }
                        else
                        {
                            // Если нет unid_registration_card — определить тип ошибки и проставить статусы
                            var body = root.GetProperty("body");

                            bool handledError = false;

                            if (body.TryGetProperty("validation_error", out var valErr))
                            {
                                // a) validation_error
                                logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Найден validation_error в ответе.");
                                if (dtReestrRK.Columns.Contains("result BizTalk"))
                                    targetRow.SetField("result BizTalk", "error");
                                if (dtReestrRK.Columns.Contains("result Lotus"))
                                    targetRow.SetField("result Lotus", "not sent");

                                handledError = true;
                            }
                            else if (body.TryGetProperty("response_error", out var respErr))
                            {
                                // b) response_error
                                logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Найден response_error в ответе.");
                                if (dtReestrRK.Columns.Contains("result Lotus"))
                                    targetRow.SetField("result Lotus", "error");

                                handledError = true;
                            }
                            else
                            {
                                logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - В ответе не найдено unid_registration_card и известных признаков ошибки (validation_error/response_error).");
                            }

                            // 2) Получаем file_id и document_set из targetRow
                            string fileIds = targetRow.Field<string>("file_id");
                            string documentSet = targetRow.Field<string>("document_set");

                            logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - fileIds: {fileIds}; documentSet: {documentSet}");

                            if (!string.IsNullOrEmpty(fileIds) && !string.IsNullOrEmpty(documentSet))
                            {
                                var fileIdList = fileIds.Split('|').Select(f => f.Trim()).Where(f => !string.IsNullOrEmpty(f));
                                foreach (var fileId in fileIdList)
                                {
                                    logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Обработка fileId: {fileId}");

                                    // 3a) Найти строку в реестре файлов по точному соответствию "ID файла в СХФ"
                                    var fileRow = dtReestrFiles.AsEnumerable()
                                        .FirstOrDefault(fr => string.Equals(fr.Field<string>("ID файла в СХФ")?.Trim(), fileId, StringComparison.OrdinalIgnoreCase));

                                    if (fileRow == null)
                                    {
                                        logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - WARN - В реестре файлов не найдена строка для ID файла {fileId}");
                                        continue;
                                    }

                                    // 3b) Найти столбец с заголовком = documentSet
                                    if (!dtReestrFiles.Columns.Contains(documentSet))
                                    {
                                        logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - WARN - В dtReestrFiles нет колонки {documentSet} для fileId {fileId}");
                                        continue;
                                    }

                                    // 3c) Указать в найденном столбце значение = error
                                    fileRow.SetField(documentSet, "error");
                                    logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Для fileId {fileId} в колонке {documentSet} установлен статус error.");
                                }
                            }
                            else
                            {
                                logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - ERR - Пустые file_id или document_set для request_id {requestId}");
                            }

                            // В данном методе изменения в DataTable остаются в памяти — при необходимости внешняя логика должна их сохранить.
                            if (handledError)
                            {
                                logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Обработка ошибки для request_id {requestId} завершена.");
                            }
                        }


                    }
                    else
                    {
                        logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - ERR - В dtReestrRK не найдена строка по request_id");
                    }
                }
            }
            catch (System.Exception ex)
            {
                errorMessage = @$"{ex.Message} /n {ex.StackTrace} /n {ex.Data} /n {ex.Source}";
                logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - ERR - {ex.Message}  {ex.StackTrace}  {ex.Data}  {ex.Source}");
            }
            log = log + Environment.NewLine + logBuilder.ToString();
        }
    }
}
