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


            //DataTable bookReferenceTable = ReadCsvToDataTable("dtBookOfReferenceReestrRK.csv");

            DataTable bookReferenceTable = ReadCsvToDataTable("dtBookOfReferenceReestrRK_new_2.csv");

            // Вывод информации для демонстрации (например, количество строк)
            Console.WriteLine($"reestrFiles.csv: {reestrFilesTable.Rows.Count} строк");
            Console.WriteLine($"dtReestrFilesFiltered_new_2.csv: {filteredFilesTable.Rows.Count} строк");
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

                        //Вызвать метод FillReestrRK_NEW (существующий монолитный)
                        FillReestrRK_NEW(filteredFilesTable, bookReferenceTable, rowUniq, dictionaryGUIDservices, ref log, text, ReestrRKUpdate);
                        
                        // === НОВЫЙ PIPELINE для PIX RPA ===
                        // Раскомментировать для использования нового pipeline-подхода:
                        // FillReestrRK_NEW_Pipeline(filteredFilesTable, bookReferenceTable, rowUniq, dictionaryGUIDservices, ref log, text, ReestrRKUpdate);
                        
                        //Class2.FillReestrRK_NEW(filteredFilesTable, bookReferenceTable, rowUniq, dictionaryGUIDservices, ref log, text, ReestrRKUpdate);
                    }
                    else
                    {
                        Console.WriteLine("Пустой текст, пропускаем обработку.");
                    }
                }

            }


        }


        // Нормализует/обновляет dtBookOfReferenceReestrRK перед основной обработкой:
        // - проставляет "GUID услуги" (ключ service_type) для регистрационных/PD0084/PD0085 случаев
        // - задаёт subject_type для registration/PD0084/uvedomlenie3/4/ZayavleniyeKompaniya в зависимости от service_type
        // Вызывать один раз перед циклом, где вызывается FillReestrRK_NEW.
        public static void NormalizeBookReferenceForServices(DataTable dtBookOfReferenceReestrRK, Dictionary<int, string> dictionaryGUIDservices, ref string log)
        {
            var logBuilder = new System.Text.StringBuilder();
            try
            {
                // Выбор приоритетного service_type для "не-1" (предпочитаем 3 — EDO, если есть)
                int preferredNonOneServiceType = 0;
                if (dictionaryGUIDservices.ContainsKey(3))
                    preferredNonOneServiceType = 3;
                else
                    preferredNonOneServiceType = dictionaryGUIDservices.Keys.FirstOrDefault(k => k != 1);

                logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - preferredNonOneServiceType = {preferredNonOneServiceType}");

                // 1) Обработать PD0084 для uvedomlenie3/4: проставить GUID услуги и subject_type
                if (preferredNonOneServiceType != 0)
                {
                    var rowsU3 = dtBookOfReferenceReestrRK.AsEnumerable()
                        .Where(r => r.Field<string>("Текст") == "uvedomlenie3" && r.Field<string>("document_set") == "PD0084");
                    foreach (var r in rowsU3)
                    {
                        r["GUID услуги"] = preferredNonOneServiceType.ToString();
                        r["subject_type"] = (preferredNonOneServiceType == 3) ? "EDO" : "BROK";
                    }

                    var rowsU4 = dtBookOfReferenceReestrRK.AsEnumerable()
                        .Where(r => r.Field<string>("Текст") == "uvedomlenie4" && r.Field<string>("document_set") == "PD0084");
                    foreach (var r in rowsU4)
                    {
                        r["GUID услуги"] = preferredNonOneServiceType.ToString();
                        r["subject_type"] = (preferredNonOneServiceType == 3) ? "EDO" : "BROK";
                    }

                    // ZayavleniyeKompaniya: назначаем document_set в зависимости от service_type (BK1186 или EDO0078)
                    var zRow = dtBookOfReferenceReestrRK.AsEnumerable().FirstOrDefault(r => r.Field<string>("Текст") == "ZayavleniyeKompaniya");
                    if (zRow != null)
                    {
                        zRow["GUID услуги"] = preferredNonOneServiceType.ToString();
                        if (preferredNonOneServiceType == 3)
                        {
                            zRow["document_set"] = "EDO0078";
                            zRow["subject_type"] = "EDO";
                        }
                        else
                        {
                            zRow["document_set"] = "BK1186";
                            zRow["subject_type"] = "BROK";
                        }
                    }
                }
                else
                {
                    logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - WARN - Нет service_type != 1 в dictionaryGUIDservices, PD0084/EDO/BROK правила не применены");
                }

                // 2) Registration: BN_DKBO0064 -> service_type == 1 (BANK) если есть
                var serviceTypeEq1 = dictionaryGUIDservices.Keys.FirstOrDefault(k => k == 1);
                if (serviceTypeEq1 != 0)
                {
                    var rowsBn = dtBookOfReferenceReestrRK.AsEnumerable()
                        .Where(r => r.Field<string>("Текст") == "registration" && r.Field<string>("document_set") == "BN_DKBO0064");
                    foreach (var r in rowsBn)
                    {
                        r["GUID услуги"] = serviceTypeEq1.ToString();
                        r["subject_type"] = "BANK";
                    }
                }
                else
                {
                    // Если нет — удаляем такие строки как раньше (опционально)
                    var rowsToDelete = dtBookOfReferenceReestrRK.AsEnumerable()
                        .Where(r => r.Field<string>("Текст") == "registration" && r.Field<string>("document_set") == "BN_DKBO0064").ToList();
                    foreach (var rr in rowsToDelete) rr.Delete();
                    dtBookOfReferenceReestrRK.AcceptChanges();
                    logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Удалены registration+BN_DKBO0064 (service_type==1 не найден)");
                }

                // 3) Registration: PD0085 -> используем preferredNonOneServiceType (BROK/EDO)
                if (preferredNonOneServiceType != 0)
                {
                    var rowsPd = dtBookOfReferenceReestrRK.AsEnumerable()
                        .Where(r => r.Field<string>("Текст") == "registration" && r.Field<string>("document_set") == "PD0085");
                    foreach (var r in rowsPd)
                    {
                        r["GUID услуги"] = preferredNonOneServiceType.ToString();
                        r["subject_type"] = (preferredNonOneServiceType == 3) ? "EDO" : "BROK";
                    }
                }
                else
                {
                    var rowsToDelete = dtBookOfReferenceReestrRK.AsEnumerable()
                        .Where(r => r.Field<string>("Текст") == "registration" && r.Field<string>("document_set") == "PD0085").ToList();
                    foreach (var rr in rowsToDelete) rr.Delete();
                    dtBookOfReferenceReestrRK.AcceptChanges();
                    logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Удалены registration+PD0085 (service_type != 1 не найден)");
                }

                // Лог — несколько диагностических строк: покажем ключевые записи после изменений
                var diagKeys = new[] { "uvedomlenie3", "uvedomlenie4", "ZayavleniyeKompaniya", "registration" };
                foreach (var key in diagKeys)
                {
                    var list = dtBookOfReferenceReestrRK.AsEnumerable().Where(r => r.Field<string>("Текст") == key).ToList();
                    foreach (var r in list)
                    {
                        logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - DIAG - Text={key}; document_set={r["document_set"]}; subject_type={r["subject_type"]}; GUID услуги={r["GUID услуги"]}");
                    }
                }
            }
            catch (Exception ex)
            {
                logBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - ERR - {ex.Message}  {ex.StackTrace}");
            }
            finally
            {
                log = log + Environment.NewLine + logBuilder.ToString();
            }
        }


        public static void UpdateGuidAndSubjectForPD0084(DataTable dtBookOfReferenceReestrRK, Dictionary<int, string> dictionaryGUIDservices, ref string log)
        {
            var logBuilder = new System.Text.StringBuilder();

            try
            {
                // Получаем значение service_type, которое != 1, из словаря dictionaryGUIDservices
                var serviceTypeNotEqualToOne = dictionaryGUIDservices
                    .Where(kvp => kvp.Key != 1)
                    .Select(kvp => kvp.Key)
                    .FirstOrDefault(); // Берем первое значение, если их несколько

                if (serviceTypeNotEqualToOne != 0) // Проверяем, что значение найдено
                {
                    // Ищем строки в bookReferenceTable, где "Текст" == 'uvedomlenie3' и "document_set" == 'PD0084'
                    var rowsToUpdateuvedomlenie3 = dtBookOfReferenceReestrRK.AsEnumerable()
                        .Where(row =>
                            (row.Field<string>("Текст") == "uvedomlenie3" && row.Field<string>("document_set") == "PD0084"));

                    // Устанавливаем значение service_type в поле "GUID услуги" для найденных строк, а так же subject_type
                    foreach (var row in rowsToUpdateuvedomlenie3)
                    {
                        row["GUID услуги"] = serviceTypeNotEqualToOne.ToString();

                        if (serviceTypeNotEqualToOne == 2)
                        {
                            row["subject_type"] = "BROK";
                        }
                        else if (serviceTypeNotEqualToOne == 3)
                        {
                            row["subject_type"] = "EDO";
                        }
                    }

                    // Ищем строки в bookReferenceTable, где "Текст" == 'uvedomlenie4' и "document_set" == 'PD0084'
                    var rowsToUpdateuvedomlenie4 = dtBookOfReferenceReestrRK.AsEnumerable()
                        .Where(row =>
                            (row.Field<string>("Текст") == "uvedomlenie4" && row.Field<string>("document_set") == "PD0084"));

                    // Устанавливаем значение service_type в поле "GUID услуги" для найденных строк, а так же subject_type
                    foreach (var row in rowsToUpdateuvedomlenie4)
                    {
                        row["GUID услуги"] = serviceTypeNotEqualToOne.ToString();

                        if (serviceTypeNotEqualToOne == 2)
                        {
                            row["subject_type"] = "BROK";
                        }
                        else if (serviceTypeNotEqualToOne == 3)
                        {
                            row["subject_type"] = "EDO";
                        }
                    }

                    // Ищем строку в bookReferenceTable, где "Текст" == 'ZayavleniyeKompaniya'
                    var zayavleniyeRow = dtBookOfReferenceReestrRK.AsEnumerable()
                        .FirstOrDefault(row => row.Field<string>("Текст") == "ZayavleniyeKompaniya");

                    if (zayavleniyeRow != null)
                    {
                        // Устанавливаем значение service_type в поле "GUID услуги"
                        zayavleniyeRow["GUID услуги"] = serviceTypeNotEqualToOne.ToString();

                        // Устанавливаем значение в поле "document_set" в зависимости от service_type
                        if (serviceTypeNotEqualToOne == 2)
                        {
                            zayavleniyeRow["document_set"] = "BK1186";
                            zayavleniyeRow["subject_type"] = "BROK";
                        }
                        else if (serviceTypeNotEqualToOne == 3)
                        {
                            zayavleniyeRow["document_set"] = "EDO0078";
                            zayavleniyeRow["subject_type"] = "EDO";
                        }
                    }
                }
                else
                {
                    logBuilder.AppendLine($"Значение service_type, отличное от 1, не найдено в словаре dictionaryGUIDservices.");
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


        public static void UpdateRegistrationRows(DataTable dtBookOfReferenceReestrRK, Dictionary<int, string> dictionaryGUIDservices, ref string log)
        {
            var logBuilder = new System.Text.StringBuilder();

            try
            {
                // Получаем значение service_type, которое == 1, из словаря dictionaryGUIDservices
                var serviceTypeEqualToOne = dictionaryGUIDservices
                    .Where(kvp => kvp.Key == 1)
                    .Select(kvp => kvp.Key)
                    .FirstOrDefault();

                if (serviceTypeEqualToOne != 0) // Проверяем, что значение найдено
                {
                    // Ищем строки в bookReferenceTable, где "Текст" == 'registration' и "document_set" == 'BN_DKBO0064'
                    var rowsToUpdate = dtBookOfReferenceReestrRK.AsEnumerable()
                        .Where(row =>
                            row.Field<string>("Текст") == "registration" && row.Field<string>("document_set") == "BN_DKBO0064")
                        .ToList();

                    // Устанавливаем значение service_type в поле "GUID услуги" для найденных строк
                    foreach (var row in rowsToUpdate)
                    {
                        row["GUID услуги"] = serviceTypeEqualToOne.ToString();
                        row["subject_type"] = "BANK";
                    }
                }
                else
                {
                    // Удаляем строки registration + BN_DKBO0064 если service_type == 1 не найден
                    var rowsToDelete = dtBookOfReferenceReestrRK.AsEnumerable()
                        .Where(row =>
                            row.Field<string>("Текст") == "registration" && row.Field<string>("document_set") == "BN_DKBO0064")
                        .ToList();

                    foreach (var row in rowsToDelete)
                        row.Delete();

                    dtBookOfReferenceReestrRK.AcceptChanges();

                    logBuilder.AppendLine($"Значение service_type, равное 1, не найдено в словаре dictionaryGUIDservices. Строки registration + BN_DKBO0064 удалены из dtBookOfReferenceReestrRK");
                }

                // Получаем значение service_type, которое != 1, из словаря dictionaryGUIDservices
                var serviceTypeNotEqualToOne = dictionaryGUIDservices
                    .Where(kvp => kvp.Key != 1)
                    .Select(kvp => kvp.Key)
                    .FirstOrDefault(); // Берем первое значение, если их несколько

                if (serviceTypeNotEqualToOne != 0) // Проверяем, что значение найдено
                {
                    // Ищем строки в bookReferenceTable, где "Текст" == 'registration' и "document_set" == 'PD0085'
                    var rowsToUpdate = dtBookOfReferenceReestrRK.AsEnumerable()
                        .Where(row =>
                            row.Field<string>("Текст") == "registration" && row.Field<string>("document_set") == "PD0085")
                        .ToList();

                    // Устанавливаем значение service_type в поле "GUID услуги" для найденных строк
                    foreach (var row in rowsToUpdate)
                    {
                        row["GUID услуги"] = serviceTypeNotEqualToOne.ToString();

                        if (serviceTypeNotEqualToOne == 2)
                        {
                            row["subject_type"] = "BROK";
                        }
                        else if (serviceTypeNotEqualToOne == 3)
                        {
                            row["subject_type"] = "EDO";
                        }
                    }
                }
                else
                {
                    // Удаляем строки registration + PD0085 если нет service_type != 1
                    var rowsToDelete = dtBookOfReferenceReestrRK.AsEnumerable()
                        .Where(row =>
                            row.Field<string>("Текст") == "registration" && row.Field<string>("document_set") == "PD0085")
                        .ToList();

                    foreach (var row in rowsToDelete)
                        row.Delete();

                    dtBookOfReferenceReestrRK.AcceptChanges();

                    logBuilder.AppendLine($"Значение service_type, отличное от 1, не найдено в словаре dictionaryGUIDservices. Строки registration + PD0085 удалены из dtBookOfReferenceReestrRK");
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
                    else if (documentSet?.Trim() == "PD0084" && normalizedSubjectType == "EDO")
                    {
                        // по кЗадаче: zayavlenie/zayvlenieakcept + uvedomlenie3/4 → PD0084/EDO
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
                        // для брокера PD0085 — только registration
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
                        // по кЗадаче: zayavlenie/zayvlenieakcept + ZayavleniyeKompaniya → EDO0078/EDO
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
                        // ZayavleniyeKompaniya → BK1186/BROK
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

        // ========================================================================================
        // PIPELINE ARCHITECTURE FOR PIX RPA MIGRATION
        // ========================================================================================

        /// <summary>
        /// State DTO для хранения всех промежуточных данных обработки одной комбинации (rowUniq, text).
        /// Все поля — instance, не static, чтобы каждый вызов был изолирован.
        /// В PIX RPA эти поля станут переменными процесса.
        /// </summary>
        public class FillReestrRKNewState
        {
            // === Входные данные ===
            public DataTable DtReestrFilesFiltered { get; set; }
            public DataTable DtBookOfReferenceReestrRK { get; set; }
            public DataRow RowUniqNumber { get; set; }
            public Dictionary<int, string> DictionaryGUIDservices { get; set; }
            public string Text { get; set; }
            public DataTable ReestrRKUpdate { get; set; }

            // === Вычисленные значения ===
            public string RequestNumber { get; set; }
            public string GuidEBA { get; set; }
            public Regex RegexMain { get; set; }
            public Regex RegexAlt { get; set; }

            // === Флаги контроля ===
            public bool HasMatch { get; set; }
            public bool ShouldAbort { get; set; }

            // === Родительские слоты ===
            public HashSet<string> ParentSubjects { get; set; }
            public HashSet<string> FoundParentSlots { get; set; }
            public bool HasParent { get; set; }

            // === Коллекции строк ===
            public List<DataRow> MatchingUpdateRows { get; set; }
            public List<DataRow> RowsWithTextInFilePaths { get; set; }

            // === Кэши и наборы ===
            public HashSet<string> PassportSets { get; set; }
            public Dictionary<string, Guid> ComplectCache { get; set; }
            public HashSet<string> ImportedKeys { get; set; }

            // === Флаг дочернего слота ===
            public bool IsChildSlot { get; set; }

            // === Лог ===
            public StringBuilder LogBuilder { get; set; }

            public FillReestrRKNewState()
            {
                ParentSubjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                FoundParentSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                MatchingUpdateRows = new List<DataRow>();
                RowsWithTextInFilePaths = new List<DataRow>();
                PassportSets = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
                { 
                    "BN_DKBO0132", "BN_DKBO0048", "EDO0019", "BK1444", "DU0080", "PD0075" 
                };
                ComplectCache = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
                ImportedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                LogBuilder = new StringBuilder();
                ShouldAbort = false;
            }
        }

        /// <summary>
        /// Шаг 1: Инициализация контекста — извлечение requestNumber и guidEBA
        /// </summary>
        public static void Step01_InitContext(FillReestrRKNewState state)
        {
            state.RequestNumber = state.RowUniqNumber["Номер заявки"]?.ToString();
            
            foreach (DataRow row in state.DtReestrFilesFiltered.Rows)
            {
                state.GuidEBA = row["GUID ЕВА клиента"]?.ToString();
                if (!string.IsNullOrEmpty(state.GuidEBA)) break;
            }

            state.LogBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Step01: requestNumber={state.RequestNumber}, guidEBA={state.GuidEBA}");
        }

        /// <summary>
        /// Шаг 2: Построение регулярных выражений для поиска text в именах файлов
        /// </summary>
        public static void Step02_BuildRegex(FillReestrRKNewState state)
        {
            if (!string.IsNullOrEmpty(state.Text))
            {
                if (state.Text.Contains("anketa", StringComparison.Ordinal))
                {
                    state.RegexAlt = new Regex(
                        $@"(^|[_\s])({Regex.Escape(state.Text)}_zatavl)(\d{{1,3}})?($|[_\s])",
                        RegexOptions.IgnoreCase | RegexOptions.Compiled);
                    state.RegexMain = new Regex(
                        $@"(^|[_\s])(?!.*\bzatavl\b)({Regex.Escape(state.Text)})(\d{{1,3}})?($|[_\s])",
                        RegexOptions.IgnoreCase | RegexOptions.Compiled);
                }
                else
                {
                    state.RegexMain = new Regex(
                        $@"(^|[_\s])({Regex.Escape(state.Text)})(\d{{1,3}})?($|[_\s])",
                        RegexOptions.IgnoreCase | RegexOptions.Compiled);
                }
            }

            state.LogBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Step02: Regex compiled for text={state.Text}");
        }

        /// <summary>
        /// Шаг 3: Проверка наличия совпадения text в файлах (hasMatch)
        /// Если нет совпадения — устанавливает ShouldAbort
        /// </summary>
        public static void Step03_CheckHasMatch(FillReestrRKNewState state)
        {
            state.HasMatch = false;

            foreach (DataRow fileRow in state.DtReestrFilesFiltered.Rows)
            {
                var path = fileRow["Путь к файлу"]?.ToString();
                if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(state.Text)) continue;

                string fileName = Path.GetFileNameWithoutExtension(path);
                bool isMatch = false;

                if (state.RegexMain != null && state.RegexAlt != null)
                {
                    if (fileName.IndexOf("zatavl", StringComparison.OrdinalIgnoreCase) >= 0)
                        isMatch = state.RegexAlt.IsMatch(fileName);
                    else
                        isMatch = state.RegexMain.IsMatch(fileName);
                }
                else if (state.RegexMain != null)
                {
                    isMatch = state.RegexMain.IsMatch(fileName);
                }

                if (isMatch)
                {
                    state.LogBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Step03: Найдено совпадение для '{state.Text}' в файле '{fileName}'");
                    state.HasMatch = true;
                    break;
                }
            }

            if (!state.HasMatch)
            {
                state.ShouldAbort = true;
                state.LogBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Step03: Нет совпадений, установлен ShouldAbort");
            }
        }

        /// <summary>
        /// Шаг 4: Обнаружение родительских слотов (parent subjects) в файлах заявки
        /// </summary>
        public static void Step04_DetectParents(FillReestrRKNewState state)
        {
            state.ParentSubjects.Clear();
            state.FoundParentSlots.Clear();

            static Regex Anchored(string token, bool excludeZatavl = false)
            {
                var safe = Regex.Escape(token);
                var negative = excludeZatavl ? "(?!.*zatavl\\w*)" : string.Empty;
                return new Regex($@"(^|[_\s]){negative}{safe}(\d{{1,3}})?($|[_\s])", 
                    RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }

            var parentPatterns = new (string subject, string slot, Regex pattern)[]
            {
                ("BROK", "AnketaBroker", Anchored("AnketaBroker")),
                ("BANK", "AnketaBank", Anchored("AnketaBank")),
                ("BANK", "anketa_zatavl", Anchored("anketa_zatavl")),
                ("BANK", "anketa", new Regex($@"(^|[_\s])anketa(?![_a-zA-Z])(\d{{1,3}})?($|[_\s])", 
                    RegexOptions.IgnoreCase | RegexOptions.Compiled)),
                ("EDO", "zayavlenieakcept", Anchored("zayavlenieakcept")),
                ("EDO", "zayavlenie", Anchored("zayavlenie")),
                ("DU", "AnketaDU", Anchored("AnketaDU")),
            };

            foreach (DataRow r in state.DtReestrFilesFiltered.Rows)
            {
                if (!string.Equals(r["Номер заявки"]?.ToString(), state.RequestNumber, StringComparison.Ordinal)) 
                    continue;

                var name = Path.GetFileNameWithoutExtension(r["Путь к файлу"]?.ToString() ?? string.Empty);
                if (string.IsNullOrEmpty(name)) continue;

                foreach (var (subject, slot, pattern) in parentPatterns)
                {
                    if (pattern.IsMatch(name))
                    {
                        state.ParentSubjects.Add(subject);
                        state.FoundParentSlots.Add(slot);
                    }
                }
            }

            state.HasParent = state.ParentSubjects.Count > 0;
            var parentSubjectsForLog = state.HasParent ? string.Join(",", state.ParentSubjects) : "-";
            var foundParentSlotsForLog = state.FoundParentSlots.Count > 0 ? string.Join(",", state.FoundParentSlots) : "-";
            
            state.LogBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Step04: Родитель найден={state.HasParent}, subject_type={parentSubjectsForLog}, slots={foundParentSlotsForLog}");
        }

        /// <summary>
        /// Шаг 5: Выбор подходящих строк из справочника (matchingUpdateRows) с учётом родительских слотов
        /// Для дочерних слотов применяет фильтрацию по subject_type
        /// </summary>
        public static void Step05_SelectMatchingUpdateRows(FillReestrRKNewState state)
        {
            // Определяем, является ли текущий слот дочерним
            state.IsChildSlot = state.Text.Equals("uvedomlenie1", StringComparison.OrdinalIgnoreCase)
                || state.Text.Equals("uvedomlenie2", StringComparison.OrdinalIgnoreCase)
                || state.Text.Equals("uvedomlenie3", StringComparison.OrdinalIgnoreCase)
                || state.Text.Equals("uvedomlenie4", StringComparison.OrdinalIgnoreCase)
                || state.Text.Equals("ZayavleniyeBanka", StringComparison.OrdinalIgnoreCase)
                || state.Text.Equals("ZayavleniyeKompaniya", StringComparison.OrdinalIgnoreCase)
                || state.Text.Equals("registration", StringComparison.OrdinalIgnoreCase);

            state.MatchingUpdateRows = state.DtBookOfReferenceReestrRK.AsEnumerable()
                .Where(updateRow =>
                {
                    var rowText = updateRow["Текст"]?.ToString();
                    if (string.IsNullOrEmpty(rowText) || !rowText.Equals(state.Text, StringComparison.OrdinalIgnoreCase))
                        return false;

                    // Специальная логика для registration
                    if (state.Text.Equals("registration", StringComparison.OrdinalIgnoreCase))
                    {
                        var docSet = updateRow["document_set"]?.ToString()?.Trim();
                        var subject = updateRow["subject_type"]?.ToString()?.Trim();
                        var isBankParent = state.ParentSubjects.Contains("BANK");
                        var isBrokParent = state.ParentSubjects.Contains("BROK");
                        var isEdoParent = state.ParentSubjects.Contains("EDO");

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

            // Если это дочерний слот и есть родитель — фильтруем по subject_type
            if (state.IsChildSlot && state.HasParent)
            {
                state.MatchingUpdateRows = state.MatchingUpdateRows
                    .Where(row =>
                    {
                        var rowSubject = row["subject_type"]?.ToString()?.Trim();
                        return !string.IsNullOrEmpty(rowSubject) && state.ParentSubjects.Contains(rowSubject);
                    })
                    .ToList();
            }

            state.LogBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Step05: matchingUpdateRows={state.MatchingUpdateRows.Count}, isChildSlot={state.IsChildSlot}");

            if (state.MatchingUpdateRows.Count == 0)
            {
                state.ShouldAbort = true;
                state.LogBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - WARN - Step05: Нет подходящих строк справочника, установлен ShouldAbort");
            }
        }

        /// <summary>
        /// Шаг 6: Поиск файлов с совпадением text в путях (rowsWithTextInFilePaths)
        /// </summary>
        public static void Step06_FindRowsWithTextInPaths(FillReestrRKNewState state)
        {
            state.RowsWithTextInFilePaths.Clear();

            Regex regexFilePath = null;
            if (state.Text.Contains("anketa"))
            {
                regexFilePath = new Regex($@"(?!.*zatavl\w*)({Regex.Escape(state.Text)})(\d{{1,3}})?",
                    RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }
            else
            {
                regexFilePath = new Regex($@"(^|[_\s])({Regex.Escape(state.Text)})(\d{{1,3}})?($|[_\s])",
                    RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }

            foreach (DataRow filteredRow in state.DtReestrFilesFiltered.Rows)
            {
                var rowText = filteredRow["Путь к файлу"]?.ToString();
                if (string.IsNullOrEmpty(rowText)) continue;

                string fileName = Path.GetFileNameWithoutExtension(rowText);
                if (regexFilePath.IsMatch(fileName))
                    state.RowsWithTextInFilePaths.Add(filteredRow);
            }

            state.LogBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Step06: rowsWithTextInFilePaths={state.RowsWithTextInFilePaths.Count}");

            if (state.RowsWithTextInFilePaths.Count == 0)
            {
                state.ShouldAbort = true;
                state.LogBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Step06: Нет файлов с текстом в путях, установлен ShouldAbort");
            }
        }

        /// <summary>
        /// Шаг 7: Фильтрация по родительскому subject_type для дочерних слотов (дополнительная проверка)
        /// Этот шаг опционален — основная фильтрация уже в Step05
        /// </summary>
        public static void Step07_FilterByParentSubjectForChildSlots(FillReestrRKNewState state)
        {
            // Дополнительная фильтрация уже выполнена в Step05
            // Этот шаг оставлен для явного разделения логики в будущем PIX процессе
            state.LogBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Step07: Фильтрация по parent subject завершена (выполнена в Step05)");
        }

        /// <summary>
        /// Шаг 8: Построение кэшей (complectCache, importedKeys)
        /// </summary>
        public static void Step08_BuildCaches(FillReestrRKNewState state)
        {
            state.ComplectCache.Clear();
            state.ImportedKeys.Clear();

            foreach (DataRow ex in state.ReestrRKUpdate.Rows)
            {
                var req = ex["Номер заявки"]?.ToString()?.Trim() ?? string.Empty;
                var ds = ex["document_set"]?.ToString()?.Trim() ?? string.Empty;
                var st = ex["subject_type"]?.ToString()?.Trim() ?? string.Empty;
                state.ImportedKeys.Add($"{req}|{ds}|{st}");
            }

            state.LogBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Step08: ImportedKeys построен, count={state.ImportedKeys.Count}");
        }

        /// <summary>
        /// Шаг 9: Обработка каждой строки из matchingUpdateRows — присвоение file_id, создание записей в ReestrRKUpdate
        /// Это самый большой шаг, реализующий все правила подбора file_id
        /// </summary>
        public static void Step09_ProcessUpdateRows(FillReestrRKNewState state)
        {
            foreach (var updRow in state.MatchingUpdateRows)
            {
                var guid = Guid.NewGuid();
                var guidDocumentId = Guid.NewGuid();

                updRow["Номер заявки"] = state.RequestNumber;

                string documentSet = updRow["document_set"]?.ToString();
                string subjectType = updRow["subject_type"]?.ToString();
                var normalizedSubjectType = subjectType?.Trim();

                // Переиспользование complect_id для дочерних слотов одного subject_type
                if (state.IsChildSlot && state.HasParent && !string.IsNullOrEmpty(normalizedSubjectType) 
                    && state.ParentSubjects.Contains(normalizedSubjectType))
                {
                    var complectKey = $"{state.RequestNumber}|{normalizedSubjectType}";
                    if (!state.ComplectCache.TryGetValue(complectKey, out var existing))
                    {
                        state.ComplectCache[complectKey] = guid;
                    }
                    else
                    {
                        guid = existing;
                    }
                }

                updRow["complect_id"] = guid;
                updRow["document_id"] = guidDocumentId;
                updRow["master_id"] = state.GuidEBA;

                string GUIDserviceNumber = updRow["GUID услуги"]?.ToString();
                if (int.TryParse(GUIDserviceNumber, out int serviceNumber) 
                    && state.DictionaryGUIDservices.TryGetValue(serviceNumber, out string guidService))
                {
                    updRow["contract_id"] = guidService;
                }

                // Построение regex для проверки наличия файлов с text
                Regex regexSearchPasport = null;
                if (state.Text.Contains("anketa"))
                {
                    regexSearchPasport = new Regex($@"(?!.*zatavl\w*)({Regex.Escape(state.Text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])",
                        RegexOptions.IgnoreCase | RegexOptions.Compiled);
                }
                else
                {
                    regexSearchPasport = new Regex($@"(^|[_\s])({Regex.Escape(state.Text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])",
                        RegexOptions.IgnoreCase | RegexOptions.Compiled);
                }

                bool hasTextFile = state.DtReestrFilesFiltered.AsEnumerable()
                    .Any(r =>
                        r["Номер заявки"]?.ToString() == state.RequestNumber &&
                        !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                        regexSearchPasport.IsMatch(Path.GetFileNameWithoutExtension(r["Путь к файлу"].ToString()))
                    );

                // Применение правил подбора file_id в зависимости от document_set и subject_type
                ProcessFileIdAssignment(state, updRow, documentSet, normalizedSubjectType, hasTextFile);
            }

            state.LogBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Step09: Обработано строк={state.MatchingUpdateRows.Count}");
        }

        /// <summary>
        /// Вспомогательный метод для присвоения file_id согласно правилам document_set/subject_type
        /// </summary>
        private static void ProcessFileIdAssignment(FillReestrRKNewState state, DataRow updRow, 
            string documentSet, string normalizedSubjectType, bool hasTextFile)
        {
            string searchText = null;
            List<string> fileIds = null;

            // Правило: Passport наборы
            if (state.PassportSets.Contains(documentSet) && hasTextFile)
            {
                var passportParentRules = new (string ParentSlot, string DocumentSet, string SubjectType)[]
                {
                    ("anketa_zatavl", "BN_DKBO0132", "BANK"),
                    ("AnketaBank", "BN_DKBO0048", "BANK"),
                    ("zayavlenie", "EDO0019", "EDO"),
                    ("zayavlenieakcept", "EDO0019", "EDO"),
                    ("AnketaBroker", "BK1444", "BROK"),
                    ("AnketaDU", "DU0080", "DU"),
                    ("anketa", "PD0075", "BANK"),
                };

                var normDocSet = documentSet?.Trim();
                var rule = passportParentRules.FirstOrDefault(r =>
                    r.DocumentSet.Equals(normDocSet, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrEmpty(r.SubjectType) || r.SubjectType.Equals(normalizedSubjectType, StringComparison.OrdinalIgnoreCase)) &&
                    state.FoundParentSlots.Contains(r.ParentSlot));

                if (rule.DocumentSet == null)
                {
                    state.LogBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Пропуск {documentSet} ({normalizedSubjectType}): не найден родительский слот");
                }
                else
                {
                    searchText = "pasport";
                    fileIds = new List<string>();
                    foreach (DataRow r in state.DtReestrFilesFiltered.Rows)
                    {
                        if (r["Номер заявки"]?.ToString() == state.RequestNumber &&
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
            // Правило: PD0084 + BANK
            else if (documentSet?.Trim() == "PD0084" && normalizedSubjectType == "BANK")
            {
                fileIds = CollectFileIds(state, new[] { "uvedomlenie1", "uvedomlenie2" });
                if (fileIds.Count > 0)
                    updRow["file_id"] = string.Join("|", fileIds);
            }
            // Правило: PD0084 + BROK
            else if (documentSet?.Trim() == "PD0084" && normalizedSubjectType == "BROK")
            {
                fileIds = CollectFileIds(state, new[] { "uvedomlenie3", "uvedomlenie4" });
                if (fileIds.Count > 0)
                    updRow["file_id"] = string.Join("|", fileIds);
            }
            // Правило: PD0084 + EDO
            else if (documentSet?.Trim() == "PD0084" && normalizedSubjectType == "EDO")
            {
                fileIds = CollectFileIds(state, new[] { "uvedomlenie3", "uvedomlenie4" });
                if (fileIds.Count > 0)
                    updRow["file_id"] = string.Join("|", fileIds);
            }
            // Правило: PD0085 + BANK
            else if (documentSet?.Trim() == "PD0085" && normalizedSubjectType == "BANK")
            {
                fileIds = CollectFileIdsBySearchText(state, "ZayavleniyeBanka");
                if (fileIds.Count > 0)
                    updRow["file_id"] = string.Join("|", fileIds);
            }
            // Правило: PD0085 + EDO
            else if (documentSet?.Trim() == "PD0085" && normalizedSubjectType == "EDO")
            {
                searchText = state.Text.Equals("registration", StringComparison.OrdinalIgnoreCase) 
                    ? "registration" : "ZayavleniyeKompaniya";
                fileIds = CollectFileIdsBySearchText(state, searchText);
                if (fileIds.Count > 0)
                    updRow["file_id"] = string.Join("|", fileIds);
            }
            // Правило: PD0085 + BROK
            else if (documentSet?.Trim() == "PD0085" && normalizedSubjectType == "BROK")
            {
                fileIds = CollectFileIdsBySearchText(state, "registration");
                if (fileIds.Count > 0)
                    updRow["file_id"] = string.Join("|", fileIds);
            }
            // Правило: EDO0078 + EDO
            else if (documentSet?.Trim() == "EDO0078" && normalizedSubjectType == "EDO")
            {
                fileIds = CollectFileIdsBySearchText(state, "ZayavleniyeKompaniya");
                if (fileIds.Count > 0)
                    updRow["file_id"] = string.Join("|", fileIds);
            }
            // Правило: BK1186 + BROK
            else if (documentSet?.Trim() == "BK1186" && normalizedSubjectType == "BROK")
            {
                fileIds = CollectFileIdsBySearchText(state, "ZayavleniyeKompaniya");
                if (fileIds.Count > 0)
                    updRow["file_id"] = string.Join("|", fileIds);
            }
            // Правило: BN_DKBO0064
            else if (documentSet?.Trim() == "BN_DKBO0064")
            {
                searchText = state.Text.Equals("registration", StringComparison.OrdinalIgnoreCase) 
                    ? "registration" : "ZayavleniyeBanka";
                fileIds = CollectFileIdsBySearchText(state, searchText);
                if (fileIds.Count > 0)
                    updRow["file_id"] = string.Join("|", fileIds);
            }
            // Правило: BN_DKBO0134 (специальная обработка — множественные строки)
            else if (documentSet?.Trim() == "BN_DKBO0134")
            {
                state.LogBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - ОБРАБОТКА documentSet == BN_DKBO0134");
                var raspiskaFileIds = new List<string>();
                foreach (DataRow r in state.DtReestrFilesFiltered.Rows)
                {
                    if (r["Номер заявки"]?.ToString() == state.RequestNumber &&
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
                    state.ReestrRKUpdate.ImportRow(updRow);

                    for (int i = 1; i < raspiskaFileIds.Count; i++)
                    {
                        var newRow = state.ReestrRKUpdate.NewRow();
                        foreach (DataColumn col in updRow.Table.Columns)
                        {
                            if (state.ReestrRKUpdate.Columns.Contains(col.ColumnName))
                                newRow[col.ColumnName] = updRow[col.ColumnName];
                        }
                        newRow["file_id"] = raspiskaFileIds[i];
                        state.ReestrRKUpdate.Rows.Add(newRow);
                    }
                }
                return; // Специальная обработка — выход из метода
            }
            // Правило по умолчанию: поиск по regex с text
            else
            {
                Regex regexSearch = null;
                if (state.Text.Contains("anketa"))
                {
                    regexSearch = new Regex($@"(?!.*zatavl\w*)({Regex.Escape(state.Text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])",
                        RegexOptions.IgnoreCase | RegexOptions.Compiled);
                }
                else
                {
                    regexSearch = new Regex($@"(^|[_\s])({Regex.Escape(state.Text)})(\d{{1,3}})?(?![a-zA-Zа-яА-Я])",
                        RegexOptions.IgnoreCase | RegexOptions.Compiled);
                }

                fileIds = new List<string>();
                foreach (DataRow r in state.DtReestrFilesFiltered.Rows)
                {
                    if (r["Номер заявки"]?.ToString() != state.RequestNumber ||
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

            // Импорт строки с дедупликацией (кроме BN_DKBO0134, уже обработан выше)
            if (documentSet != "BN_DKBO0134" && !string.IsNullOrEmpty(updRow["file_id"]?.ToString()))
            {
                var key = $"{state.RequestNumber}|{documentSet?.Trim()}|{normalizedSubjectType}";
                if (state.ImportedKeys.Add(key))
                {
                    state.ReestrRKUpdate.ImportRow(updRow);
                }
                else
                {
                    state.LogBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - INFO - Пропуск дубля для ключа {key}");
                }
            }
        }

        /// <summary>
        /// Вспомогательный метод: собрать file_id по нескольким поисковым токенам
        /// </summary>
        private static List<string> CollectFileIds(FillReestrRKNewState state, string[] searchTokens)
        {
            var fileIds = new List<string>();
            foreach (var token in searchTokens)
            {
                foreach (DataRow r in state.DtReestrFilesFiltered.Rows)
                {
                    if (r["Номер заявки"]?.ToString() == state.RequestNumber &&
                        !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                        r["ID файла в СХФ"]?.ToString() != "error")
                    {
                        var path = r["Путь к файлу"].ToString();
                        var id = r["ID файла в СХФ"]?.ToString();
                        if (!string.IsNullOrEmpty(id) && 
                            path.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0 && 
                            !fileIds.Contains(id))
                        {
                            fileIds.Add(id);
                        }
                    }
                }
            }
            return fileIds;
        }

        /// <summary>
        /// Вспомогательный метод: собрать file_id по одному поисковому тексту
        /// </summary>
        private static List<string> CollectFileIdsBySearchText(FillReestrRKNewState state, string searchText)
        {
            var fileIds = new List<string>();
            foreach (DataRow r in state.DtReestrFilesFiltered.Rows)
            {
                if (r["Номер заявки"]?.ToString() == state.RequestNumber &&
                    !string.IsNullOrEmpty(r["Путь к файлу"]?.ToString()) &&
                    r["Путь к файлу"].ToString().IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 &&
                    r["ID файла в СХФ"]?.ToString() != "error")
                {
                    var id = r["ID файла в СХФ"]?.ToString();
                    if (!string.IsNullOrEmpty(id) && !fileIds.Contains(id))
                        fileIds.Add(id);
                }
            }
            return fileIds;
        }

        /// <summary>
        /// Шаг 10: Финализация — добавление лога в ref log
        /// </summary>
        public static void Step10_FlushLog(FillReestrRKNewState state, ref string log)
        {
            log = log + Environment.NewLine + state.LogBuilder.ToString();
        }

        /// <summary>
        /// ORCHESTRATOR-метод: FillReestrRK_NEW_Pipeline
        /// Последовательно вызывает шаги обработки и формирует результат, эквивалентный FillReestrRK_NEW
        /// </summary>
        public static void FillReestrRK_NEW_Pipeline(
            DataTable dtReestrFilesFiltered,
            DataTable dtBookOfReferenceReestrRK,
            DataRow rowUniqNumber,
            Dictionary<int, string> dictionaryGUIDservices,
            ref string log,
            string text,
            DataTable ReestrRKUpdate)
        {
            var state = new FillReestrRKNewState
            {
                DtReestrFilesFiltered = dtReestrFilesFiltered,
                DtBookOfReferenceReestrRK = dtBookOfReferenceReestrRK,
                RowUniqNumber = rowUniqNumber,
                DictionaryGUIDservices = dictionaryGUIDservices,
                Text = text,
                ReestrRKUpdate = ReestrRKUpdate
            };

            try
            {
                // Шаг 1: Инициализация контекста
                Step01_InitContext(state);

                // Шаг 2: Построение regex
                Step02_BuildRegex(state);

                // Шаг 3: Проверка hasMatch
                Step03_CheckHasMatch(state);
                if (state.ShouldAbort)
                {
                    Step10_FlushLog(state, ref log);
                    return;
                }

                // Шаг 4: Обнаружение родительских слотов
                Step04_DetectParents(state);

                // Шаг 5: Выбор подходящих строк справочника
                Step05_SelectMatchingUpdateRows(state);
                if (state.ShouldAbort)
                {
                    Step10_FlushLog(state, ref log);
                    return;
                }

                // Шаг 6: Поиск файлов с text в путях
                Step06_FindRowsWithTextInPaths(state);
                if (state.ShouldAbort)
                {
                    Step10_FlushLog(state, ref log);
                    return;
                }

                // Шаг 7: Фильтрация по parent subject (опционально)
                Step07_FilterByParentSubjectForChildSlots(state);

                // Шаг 8: Построение кэшей
                Step08_BuildCaches(state);

                // Шаг 9: Обработка строк и присвоение file_id
                Step09_ProcessUpdateRows(state);

                // Шаг 10: Финализация лога
                Step10_FlushLog(state, ref log);
            }
            catch (Exception ex)
            {
                state.LogBuilder.AppendLine($"{DateTime.Now:dd-MM-yyyy HH:mm:ss} - ERR - {ex.Message}  {ex.StackTrace}");
                Step10_FlushLog(state, ref log);
            }
        }
    }
}
