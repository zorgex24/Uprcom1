using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ProgressBar;




namespace Uprcom1
{
    public class FileDataManager
    {
        private readonly DbfFileHandler _dbfHandler;    // Добавляем поле для субкласса обработчика uprcom.dbf
        private readonly ProviderNamesHandler _mappingHandler;    // Добавляем поле для субкласса обработчика provider_names.txt
        private readonly IgnoreListHandler _ignoreHandler;    // Добавляем поле для субкласса обработчика ignorelist.txt


        // Данные файлов uprcom.dbf (ид-название провайдера), ignorelist.txt (список ид) и provider_names.txt (сопоставления ид-название файла)
        private Dictionary<string, string> _actualProviders = new Dictionary<string, string>(capacity: 350, StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _ignoredProviders = new HashSet<string>(capacity: 50, StringComparer.Ordinal);
        private Dictionary<string, string> _providerMappings = new Dictionary<string, string>(capacity: 350, StringComparer.Ordinal);

        public FileDataManager()
        {
            // Инициализация путей к папке и файлам (вычисляется один раз при создании)
            _dbfHandler = new DbfFileHandler();             // Инициализируем обработчик uprcom.dbf и остальных файлов
            _mappingHandler = new ProviderNamesHandler();
            _ignoreHandler = new IgnoreListHandler();
        }

        // Переменная пути к файлу uprcom.dbf, к которой могут обращаться из основных процедур (только чтение)
        public string SelectedFolder => (Path.GetDirectoryName(_dbfHandler.FilePath));
        // Переменная для внешних процедур
        public HashSet<string> ListignoredProviders => _ignoredProviders;
        // Переменная для внешних процедур
        public Dictionary<string, string> ActualProvidersList => _actualProviders;



        // Методы для процедур UI
        // Статусы файлов
        public bool DbfFileNotReady => _dbfHandler.Status != FileStatus.ReadyForRW; 
        public FileStatus IgnoreFileStatus => _ignoreHandler.Status; 
        public FileStatus MappingFileStatus => _mappingHandler.Status;
        // Текст ошибок статусов файлов
        public string TextErrorDbfFile => _dbfHandler.ErrorMessage;
        // Количество записей в файлах
        public int CountProviderMappings => _providerMappings.Count;
        public int CountIgnoreID => _ignoredProviders.Count;


        // Класс-переменная для записей оо дубликатах в файлах
        public class DuplicateRecord
        {
            public string FileName { get; set; }
            public string FileFullInfo { get; set; }
            public string FullAddress { get; set; }

            public DuplicateRecord(string fileName, string fileFullInfo, string fullAddress)
            {
                FileName = fileName;
                FileFullInfo = fileFullInfo;
                FullAddress = fullAddress;
            }
        }


        // Класс методов проверки дубликатов
        public class AddressComparer : IEqualityComparer<object[]>
        {
            private readonly int[] _keyFieldIndexes;

            public AddressComparer(int[] keyFieldIndexes)
                => _keyFieldIndexes = keyFieldIndexes;

            public bool Equals(object[] x, object[] y)
            {
                foreach (var idx in _keyFieldIndexes)
                    if (!Equals(x[idx], y[idx]))
                        return false;
                return true;
            }

            public int GetHashCode(object[] obj)
            {
                unchecked
                {
                    int hash = 17;
                    foreach (var idx in _keyFieldIndexes)
                        hash = hash * 23 + (obj[idx]?.GetHashCode() ?? 0);
                    return hash;
                }
            }
        }








        ///////////////////////////////////////////////////////////////
        // Метод для изменения выбранной папки с файлом uprcom.dbf  //
        // Входные данные: путь к файлу uprcom.dbf                 //
        // выходные данные: ошибка или null если все нормально    //
        ///////////////////////////////////////////////////////////
        public string SetSelectedFolder(string path)
        {
            // 1. Инициализация и очистка предыдущего состояния
            _actualProviders.Clear();

            // 2. Создаем или переиспользуем обработчик DBF
            _dbfHandler.SetFolder(path);

            // 3. Возввращаем содержимое текста ошибки: null, если ошибок нет или текст ошибки в противном случае
            return _dbfHandler.ErrorMessage;
        }




        ///////////////////////////////////////////////////////////////////////////
        // Метод считывания списка ID  из файла в переменную _ignoredProviders  //
        // Вход: ничего Выход: текст ошибки файла/данных или null              //
        ////////////////////////////////////////////////////////////////////////
        public string LoadIgnoreList()
        {
            // 1. Первичная проверка (для раннего выхода)
            if (_ignoreHandler.NotReadyFile) return _ignoreHandler.ErrorMessage;

            // 2. Сброс состояния
            _ignoredProviders.Clear();
            bool HasInvalidEntries = false;
            bool hasDuplicates = false;

            // 3. Основной цикл обработки
            try
            {
                foreach (var line in _ignoreHandler.GetFilteredLines())
                {
                    string id = line.Trim();

                    if (!IsValidId(id, 1))
                    {
                        HasInvalidEntries = true;
                        continue;
                    }

                    if (!_ignoredProviders.Add(id))
                        hasDuplicates = true;
                }
            }
            // Ловим ошибки, возникшие ВО ВРЕМЯ итерации, но если ошибка вдруг еще не обработана
            catch (Exception ex) when (!_ignoreHandler.NotReadyFile)
            {
                _ignoreHandler.HandleFileError(ex);
            }

            // Если произошла ошибка во время чтения - очищаем данные из переменной. Важно: очистка ДО установки статуса данных
            if (_ignoreHandler.NotReadyFile)
                _ignoredProviders.Clear();

            _ignoreHandler.ErrorToText(HasInvalidEntries, hasDuplicates);

            return _ignoreHandler.ErrorMessage;
        }




        /////////////////////////////////////////////////////////////////////////////////
        // Метод проверки файла provider_names.txt на наличие, ошибки чтения/записи   //
        // и считывания пары ID-имя файла  в переменную _providerMappings            //
        // Вход: ничего Выход: текст ошибки                                         //
        /////////////////////////////////////////////////////////////////////////////
        public string LoadProviderMappings()
        {
            // 1. Если были ошибки при создании субкласса и проверке файла на чтение, то прерываемся. передавая ошибку выше
            if (_mappingHandler.NotReadyFile) return _mappingHandler.ErrorMessage;

            // 2. Считываем данные из файла
            _providerMappings.Clear();
            var uniqueIds = new HashSet<string>();
            var uniqueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool HasInvalidEntries = false;
            bool hasDuplicates = false;

            try
            {
                foreach (var line in _mappingHandler.GetFilteredLines())
                {
                    // 3. Парсинг строки (разделитель - пробел/табуляция)
                    string[] parts = line.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries)
                                       .Select(p => p.Trim()).ToArray();

                    // 4. Проверка наличия обеих частей
                    if (parts.Length < 2)
                    {
                        HasInvalidEntries = true;
                        continue;
                    }

                    string id = parts[0].Trim();
                    string name = parts[1].Trim();

                    // Если в имени файла на конце .dbf - то отсекаем его
                    if (name.Length > 4 && name.EndsWith(".dbf", StringComparison.OrdinalIgnoreCase))
                    {
                        name = name.Substring(0, name.Length - 4).TrimEnd();
                    }

                    // 5. Проверка ид и имени файла и длины файла не более 30 символов
                    if (!IsValidId(id) || !IsValidFileName(name) || name.Length > 30)
                    {
                        HasInvalidEntries = true;
                        continue;
                    }

                    // 6. Проверка дубликатов
                    bool isIdDuplicate = !uniqueIds.Add(id);
                    bool isNameDuplicate = !uniqueNames.Add(name);

                    if (isIdDuplicate || isNameDuplicate)
                        hasDuplicates = true;
                    else
                        _providerMappings[id] = name;
                }
            }
            // Ловим ошибки, возникшие ВО ВРЕМЯ итерации, но если ошибка вдруг еще не обработана
            catch (Exception ex) when (!_mappingHandler.NotReadyFile)
            {
                _mappingHandler.HandleFileError(ex);
            }

            // Если произошла ошибка во время чтения - очищаем данные из переменной. Важно: очистка ДО установки статуса данных
            if (_mappingHandler.NotReadyFile)
                _ignoredProviders.Clear();

            _mappingHandler.ErrorToText(HasInvalidEntries, hasDuplicates);

            return _mappingHandler.ErrorMessage;
        }




        //////////////////////////////////////////////////////////////////
        // Метод удаления невалидных ID из файла ignorelist.txt        //
        // Вход: ничего                                               //
        // Выход: ошибка записи, ошибка перечтения файла, либо null  //
        //////////////////////////////////////////////////////////////
        public string RepairIgnoreList()
        {
            try
            {
                // 1. Читаем все строки сразу (однократное чтение файла)
                var rawLines = _ignoreHandler.ReadRawLines().ToList();

                // 2. Если файл прочитать не удалось -выходим
                if (_ignoreHandler.NotReadyFile) return $"{_ignoreHandler.ErrorMessage}";

                var linesToKeep = new List<string>();
                bool hasInvalidEntries = false;
                var tempIgnoredProviders = new HashSet<string>(capacity: 50, StringComparer.Ordinal);

                // 3. Обрабатываем каждую строку
                foreach (string line in rawLines)
                {
                    if (IsCommentOrEmptyLine(line))
                    {
                        linesToKeep.Add(line);
                        continue;
                    }

                    string idPart = line.Split('#')[0].Trim();
                    if (IsValidId(idPart, 1))
                    {
                        if (tempIgnoredProviders.Add(idPart))
                        {
                            linesToKeep.Add(line);
                        }
                        else
                        {
                            hasInvalidEntries = true; // Дубликат
                        }
                    }
                    else
                    {
                        hasInvalidEntries = true; // Невалидный ID
                    }
                }

                // 4. Если нет изменений — выходим, возвращая null
                if (!hasInvalidEntries) return null;

                // 5. Перезаписываем файл через SafeFileRewrite
                _ignoreHandler.SafeFileRewrite(linesToKeep);

                // 6. Если запись успешна, обновляем данные в переменной и выходим, возвращая null
                if (_ignoreHandler.Status == FileStatus.ReadyForRW)
                {
                    _ignoredProviders.Clear();
                    _ignoredProviders.UnionWith(tempIgnoredProviders);
                    return null;
                }

                // 7. Если ошибка — возвращаем её текст
                return $"{_ignoreHandler.ErrorMessage}";
            }
            catch (Exception ex)
            {
                return $"Неизвестная ошибка при обработке ignorelist.txt: {ex.Message}";
            }
        }




        /////////////////////////////////////////////////////////////////////////////////
        // Метод удаления некорректных строк и дубликатов из файла provider_names.txt //
        // Вход: ничего Выход: ошибка записи, ошибка перечтения файла, либо null     //
        //////////////////////////////////////////////////////////////////////////////
        public string RepairMappingListFile()
        {
            try
            {
                // 1. Читаем строки через handler
                var rawLines = _mappingHandler.ReadRawLines().ToList();

                // 2. Если файл прочитать не удалось - выходим
                if (_mappingHandler.NotReadyFile) return _mappingHandler.ErrorMessage;

                // 3. Вызываем метод исправления строк
                var (validLines, newMappings) = ProcessProviderNamesLines(rawLines, out bool hasInvalidEntries);

                // 4. Если нет изменений - выход
                if (!hasInvalidEntries) return null;

                // 5. Перезапись через handler
                _mappingHandler.SafeFileRewrite(validLines);

                // 6. Если ошибка при записи -выходим, передавай ее текст
                if (_mappingHandler.Status != FileStatus.ReadyForRW) return _mappingHandler.ErrorMessage;

                // 7. Если все записалось нормально. то в переменную _providerMappings заносим исправленные значения и возвращаем null
                _providerMappings = newMappings;
                return null;
            }

            // 15. Если были внезапные ошибки не с файловой системой - то возвращаем их
            catch (Exception ex)
            {
                return $"Неизвестная ошибка при обработке provider_names.txt: {ex.Message}";
            }
        }




        ///////////////////////////////////////////////////////////////////////////////////
        // Метод исправления неверных записей в строках из файла provider_names.txt     //
        // Вход: сырые строки  Выход: исправленные строки и готовые пары id-имя файла  //
        ////////////////////////////////////////////////////////////////////////////////
        private (List<string> validLines, Dictionary<string, string> newMappings) ProcessProviderNamesLines(List<string> rawLines, out bool hasInvalidEntries)
        {
            var validLines = new List<string>(rawLines.Count);  // Переменная, в которой будут хранится исправленные сырые строки
            // Создаем копию переменной _providerMappings для временного хранения исправленных данных
            var newMappings = new Dictionary<string, string>( capacity: Math.Max(_providerMappings.Count, 1), StringComparer.Ordinal);
            var uniqueIds = new HashSet<string>();
            var fileNameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            const int MaxFileNameLength = 28;  // Максимальная длина миени файла, до которого оно обрежется
            hasInvalidEntries = false;        // Флаг наличия изменений

            // 1. Сначала собираем ВСЕ имена файлов из сырых строк (нормализованные)
            var allFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int i = 0;

            while (i < rawLines.Count)
            {
                string line = rawLines[i];

                if (IsCommentOrEmptyLine(line))
                {
                    i++; // Пропускаем комментарии/пустые строки
                    continue;
                }

                string[] parts = GetFileNameParts(line);
                if (parts.Length < 2 || !IsValidId(parts[0]) || !IsValidFileName(parts[1]))
                {
                    hasInvalidEntries = true;
                    rawLines.RemoveAt(i); // Удаляем и НЕ увеличиваем индекс
                    continue;
                }

                string fileName = NormalizeFileName(parts[1]);
                fileName = fileName.Length > 28 ? fileName.Substring(0, 28) : fileName;
                if (!string.IsNullOrEmpty(fileName))
                    allFileNames.Add(fileName); // HashSet автоматически уберет дубли

                i++; // Переход к следующей строке
            }

            // 3. Обработка строк
            foreach (string line in rawLines)
            {
                if (IsCommentOrEmptyLine(line))
                {
                    validLines.Add(line);
                    continue;
                }

                // 5. Разбиваем на ID и имя файла
                string[] parts = GetFileNameParts(line);
                string id = parts[0];
                string fileName = NormalizeFileName(parts[1]);  
                int commentPos = line.IndexOf('#');
                string commentPart = commentPos >= 0 ? line.Substring(commentPos) : string.Empty;

                // 8. Обработка дубликатов ID (оставляем только первое вхождение)
                if (!uniqueIds.Add(id))  // Проверка + добавление в одной операции
                {
                    hasInvalidEntries = true;
                    continue;
                }

                string newLine = line;

                // 9. Проверка длины имени файла, если больше MaxFileNameLength - обрезаем
                if (fileName.Length > MaxFileNameLength)
                {
                    fileName = fileName.Substring(0, MaxFileNameLength);
                    newLine = $"{id} {fileName}";
                    if (!string.IsNullOrEmpty(commentPart)) newLine = newLine.PadRight(50) + commentPart.Trim();
                    hasInvalidEntries = true;
                }

                // 10. Обработка дубликатов имени файла (добавляем суффикс)
                if (fileNameCounts.TryGetValue(fileName, out int count))
                {
                    hasInvalidEntries = true;
                    int attempts = 1;

                    while (attempts < 99)
                    {
                        // Увеличиваем счетчик, суффикс и формируем новое имя
                        fileNameCounts[fileName] = ++count;
                        string newFileName = $"{fileName}{count}";
                        attempts++;

                        // Проверяем, есть ли такое имя в словаре и в существующих именах 
                        if (!allFileNames.Contains(newFileName))
                        {
                            allFileNames.Add(newFileName); //если нету - добавляем новое имя в список существующих имен и выходим из цикла
                            break;
                        }
                        // Если имя занято - цикл продолжится с новым count+1
                    }
                    // присваем имя с суффиксом
                    newLine = $"{id} {fileName}{count}";
                    if (!string.IsNullOrEmpty(commentPart)) newLine = newLine.PadRight(50) + commentPart.Trim();
                }
                else
                {
                    fileNameCounts[fileName] = 0;
                }
                // 11. Добавляем строку в результат
                validLines.Add(newLine);
                newMappings[id] = fileName; // Данные для переменной _providerMappings
            }
            return (validLines, newMappings);
        }

        // Вспомогательный метод -разделение строки на части, выделание ид, имени файла и комментария
        private string[] GetFileNameParts(string line)
        {
            int commentPos = line.IndexOf('#');
            string content = commentPos >= 0 ? line.Substring(0, commentPos) : line;
            return content.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }

        // Вспомогательный метод - нормализация имени удаление .dbf иобрезание до 28 символов
        private string NormalizeFileName(string fileName)
        {
            if (fileName.Length > 4 && fileName.EndsWith(".dbf", StringComparison.OrdinalIgnoreCase))
                fileName = fileName.Substring(0, fileName.Length - 4);

            return fileName;
        }




        /////////////////////////////////////////////////////////////////
        // Метод проверки файла ignorelist.txt на возможность записи  //
        // Вход: ничего Выход: статус файла                          //
        //////////////////////////////////////////////////////////////
        public string StatusSaveIgnoreList() => _ignoreHandler.GetWriteAccessStatus();




        /////////////////////////////////////////////////////////////////////
        // Метод проверки файла provider_names.txt на возможность записи  //
        // Вход: ничего Выход: статус файла                              //
        //////////////////////////////////////////////////////////////////
        public string StatusSaveMappingeList() => _mappingHandler.GetWriteAccessStatus();




        ////////////////////////////////////////////////////////////////////////////////
        // Метод, который проверяет строка пустая или там только комментарий         //
        // Вход: строка для проверки                                                //
        // выход: true если строка пустая или там только комментарий, иначе false  //
        ////////////////////////////////////////////////////////////////////////////
        private bool IsCommentOrEmptyLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return true;
            return line.TrimStart()[0] == '#';
        }




        //////////////////////////////////////////////////////////////////////
        /// <summary>
        /// Проверяет, что ID состоит только из цифр и имеет допустимую длину.
        /// </summary>
        /// <param name="id">Строка для проверки.</param>
        /// <param name="minLength">Минимальная длина (по умолчанию 18).</param>
        /// <param name="maxLength">Максимальная длина (по умолчанию 18).</param>
        protected bool IsValidId(string id, int minLength = 18)
        {
            if (string.IsNullOrEmpty(id) || id.Length < minLength || id.Length > 18)
                return false;

            return id.All(char.IsDigit);
        }




        //////////////////////////////////////////////////////////////////////
        /// <summary>
        /// Проверяет, что имя файла содержит только:
        /// - Строчные латинские буквы (a-z)
        /// - Цифры (0-9)
        /// - Подчёркивание (_)
        /// - Не начинается с цифры
        /// - Длина ≥ 2 символов
        /// </summary>
        protected bool IsValidFileName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length < 2 || char.IsDigit(name[0]))
                return false;

            foreach (char c in name)
            {
                // Явная проверка на латинские буквы (a-z), цифры и подчёркивание
                if (!((c >= 'a' && c <= 'z') || char.IsDigit(c) || c == '_'))
                    return false;
            }
            return true;
        }




        ////////////////////////////////////////////////////////////////////////////////
        // Метод проверки наличия папки Out и возможности записи в нее файлов        //
        // Если папки нет, то пытаемся ее создать в той же папке, где и uprcom.dbf  //
        // Вход: ничего Выход: ошибка доступности, либо null                       //
        ////////////////////////////////////////////////////////////////////////////
        public string CheckFolderOutEnvironment() => _dbfHandler.VerifyOrCreateFolder();




        ////////////////////////////////////////////////////////////////////////////////
        // Метод разбиения файла uprcom.dbf на малые файл, в которых провайдеры сгруппированы по ID  //
        // Если папки нет, то пытаемся ее создать в той же папке, где и uprcom.dbf  //
        // Вход: ничего Выход: ошибка доступности, либо null                       //
        ////////////////////////////////////////////////////////////////////////////
        public (string messageProcedure, bool passProcedure) ProcessDbfFile(bool removeDuplicates, bool clearMonthDbt)
        {
            // 1. Разблокируем файл (снимаем с него поток, чтобы не было параллельного)
            _dbfHandler.Unlock();
            
            try
            {
                // 2. Вызываем метод чтения данных из файла
                var (records, fields) = _dbfHandler.ReadDbfRecordsLazy();

                // 3. Если возникли какие-либо ошибки - выходим, отметив что создание файлов не удалось
                if (_dbfHandler.NotReadyFile) return (_dbfHandler.ErrorMessage, false);

                // 4. Переменные для формирования отчетов 
                int filesCreated = 0;                           // Количество созданных файлов
                var skippedProviders = new HashSet<string>();  // список пропущенных провайдеров
                List<DuplicateRecord> Duplicates = new List<DuplicateRecord>(); // Запись информации о дубликатах
                var errors = new List<string>();             // Запись об ошибках создания dbf файлов

                // 5. Создаем лог-файл
                var logContent = new StringBuilder(1024)        // Переменная для строк лог-файла
                    .AppendLine($"Обработка начата: {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
                    .AppendLine($"Выбранный файл: {_dbfHandler.FilePath}")
                    .AppendLine(new string('-', 40))
                    .AppendLine();

                // 6. Выводим в лог ошибки вспомогательных файлов
                if (_mappingHandler.Status == FileStatus.NotFound)
                    logContent.AppendLine("ВНИМАНИЕ! Файл provider_names.txt не найден.\n" + new string('-', 40));
                else if (CountProviderMappings == 0)
                    logContent.AppendLine("ВНИМАНИЕ! Файл provider_names.txt не содержит валидных сопоставлений.\n" + new string('-', 40));
                if (_ignoreHandler.Status == FileStatus.NotFound)
                    logContent.AppendLine("ВНИМАНИЕ! Файл ignorelist.txt не найден.\n" + new string('-', 40));
                else if (CountIgnoreID == 0)
                    logContent.AppendLine("ВНИМАНИЕ! Файл ignorelist.txt не содержит валидных ID.\n" + new string('-', 40));

                // 7. Получаем индексы нужных полей
                int idxProvider = Array.FindIndex(fields, f => f.Name.Equals("PROVIDER", StringComparison.OrdinalIgnoreCase));
                int idxProvName = Array.FindIndex(fields, f => f.Name.Equals("PROVNAME", StringComparison.OrdinalIgnoreCase));
                int idxAddresId = Array.FindIndex(fields, f => f.Name.Equals("ADDRESID", StringComparison.OrdinalIgnoreCase));
                int idxKylic = Array.FindIndex(fields, f => f.Name.Equals("KYLIC", StringComparison.OrdinalIgnoreCase));
                int idxNdom = Array.FindIndex(fields, f => f.Name.Equals("NDOM", StringComparison.OrdinalIgnoreCase));
                int idxNkorp = Array.FindIndex(fields, f => f.Name.Equals("NKORP", StringComparison.OrdinalIgnoreCase));
                int idxNkw = Array.FindIndex(fields, f => f.Name.Equals("NKW", StringComparison.OrdinalIgnoreCase));
                int idxNkomn = Array.FindIndex(fields, f => f.Name.Equals("NKOMN", StringComparison.OrdinalIgnoreCase));
                int idxKnasp = Array.FindIndex(fields, f => f.Name.Equals("KNASP", StringComparison.OrdinalIgnoreCase));
                int idxNote = Array.FindIndex(fields, f => f.Name.Equals("NOTE", StringComparison.OrdinalIgnoreCase));
                int idxMonthdbt = Array.FindIndex(fields, f => f.Name.Equals("MONTHDBT", StringComparison.OrdinalIgnoreCase));

                // 8. Группировка записей по провайдерам
                var providerGroups = new Dictionary<string, HashSet<object[]>>();
                var keyIndexes = new[] { idxProvider, idxAddresId, idxKylic, idxNdom, idxNkorp, idxNkw, idxNkomn };
                var comparer = new AddressComparer(keyIndexes);

                foreach (var record in records)
                {
                    string providerId = record[idxProvider].ToString();

                    // 8.1. Пропускаем игнорируемых провайдеров
                    if (_ignoredProviders.Contains(providerId))
                    {
                        string fileName = _providerMappings.TryGetValue(providerId, out var name) ? $"{name}.dbf" : "N/A";
                        skippedProviders.Add($"{providerId}     {record[idxProvName]} ({fileName})");  // HashSet автоматически проверяет уникальность
                        continue;
                    }

                    // 8.2. Модификация полей
                    record[idxNote] = record[idxKnasp];
                    record[idxKnasp] = "г.Ростов-на-Дону";
                    if (clearMonthDbt) record[idxMonthdbt] = null;

                    // 8.3. Добавление в группу с проверкой дубликатов
                    if (!providerGroups.TryGetValue(providerId, out var recordsSet))
                    {
                        recordsSet = new HashSet<object[]>(removeDuplicates ? comparer : null);
                        providerGroups[providerId] = recordsSet;
                    }

                    // 8.4 Если не удалось добавить (дубликат) и включена проверка дубликатов
                    if (!recordsSet.Add(record) && removeDuplicates)
                    {
                        // Формируем информацию о дубликате
                        var address = new StringBuilder()
                            .Append($"  ID: {record[idxAddresId]}, Адрес: ул. {record[idxKylic]}, д. {record[idxNdom]}");

                        if (!string.IsNullOrWhiteSpace(record[idxNkorp]?.ToString()))
                            address.Append($", корп. {record[idxNkorp]}");
                        if (!string.IsNullOrWhiteSpace(record[idxNkw]?.ToString()))
                            address.Append($", кв. {record[idxNkw]}");
                        if (!string.IsNullOrWhiteSpace(record[idxNkomn]?.ToString()))
                            address.Append($", ком. {record[idxNkomn]}");

                        string fileName = GetProviderFileName(providerId);
                        string fileFullInfo = $"Файл: {fileName}   ({record[idxProvName]}   {providerId})";
                        Duplicates.Add(new DuplicateRecord(
                            fileName,
                            fileFullInfo,
                            address.ToString()
                        ));
                        continue; // Пропускаем дубликат
                    }
                }

                // 9. Запись файлов через метод субкласса DbfFileHandler : FileHandler
                foreach (var group in providerGroups)
                {
                    if (!_dbfHandler.TryWriteDbfRecords(GetProviderFileName(group.Key), group.Value, out var error))
                    {
                        errors.Add(error);
                    }
                    else
                    {
                        filesCreated++;
                    }
                }

                // 10. Записываем в лог-файл провайдеров, которе были пропущены
                if (skippedProviders.Count > 0)
                {
                    logContent.AppendLine("ПРОПУЩЕННЫЕ ПРОВАЙДЕРЫ:");
                    logContent.AppendLine(new string('-', 40));
                    foreach (var provider in skippedProviders)
                    {
                        logContent.AppendLine(provider);
                    }
                    logContent.AppendLine();
                }

                // 11. Ошибки при записи файлов
                if (errors.Count > 0)
                {
                    logContent.AppendLine("ОШИБКИ:");
                    logContent.AppendLine(new string('-', 40));
                    foreach (var error in errors)
                    {
                        logContent.AppendLine(error);
                    }
                    logContent.AppendLine();
                }

                // 12. Дубликаты (группируем по имени файла)
                if (!removeDuplicates)
                {
                    logContent.AppendLine("Проверка на дубликаты отключена.");
                    logContent.AppendLine(new string('-', 40));
                    logContent.AppendLine();
                }
                else if (Duplicates.Count > 0)
                {
                    var duplicatesByFile = Duplicates
                        .GroupBy(d => d.FileName)
                        .OrderBy(g => g.Key);

                    logContent.AppendLine("НАЙДЕННЫЕ ДУБЛИКАТЫ:");
                    logContent.AppendLine(new string('-', 40));

                    foreach (var fileGroup in duplicatesByFile)
                    {
                        logContent.AppendLine(fileGroup.First().FileFullInfo);
                        foreach (var dup in fileGroup)
                        {
                            logContent.AppendLine(dup.FullAddress);
                        }
                        logContent.AppendLine();
                    }
                }

                // 13. Итоговая статистика
                logContent.AppendLine("ИТОГОВАЯ СТАТИСТИКА:");
                logContent.AppendLine(new string('-', 40));
                logContent.AppendLine($"Всего создано файлов: {filesCreated}");
                if (errors.Count > 0)
                {
                    logContent.AppendLine($"Не удалось создать файлов: {errors.Count}");
                }
                logContent.AppendLine($"Пропущено провайдеров: {skippedProviders.Count}");

                if (Duplicates.Count > 0)
                {
                    int filesWithDuplicates = Duplicates
                        .Select(d => d.FileName)
                        .Distinct()
                        .Count();

                    logContent.AppendLine($"Файлов с дубликатами: {filesWithDuplicates} (всего дубликатов: {Duplicates.Count})");
                }

                string errorSave = _dbfHandler.TryWriteLogFile(logContent.ToString());
                if (errorSave != null)
                {
                    MessageBox.Show(errorSave, "Ошибка", MessageBoxButton.OK,  MessageBoxImage.Error);
                }

                // 14. Возарвщаем различное итоговое сообщение в зависимости от наличия неудавшихся файлов и флаг того, что запись файлов прошла успешно
                string message = $"Всего создано файлов: {filesCreated}\nПропущено провайдеров: {skippedProviders.Count}";
                if (errors.Count > 0)
                {
                    message += $"\nНе удалось создать файлов: {errors.Count}";
                }

                return (message, true);

            }
            catch (Exception ex)
            {
                return ($"Критическая ошибка: {ex.Message}", false);
            }
            finally
            {
                // ВОССТАНАВЛИВАЕМ БЛОКИРОВКУ В ЛЮБОМ СЛУЧАЕ
                try
                {
                    _dbfHandler.LockForRead();
                }
                catch { }  // Даже если блокировка не удалась - не прерываем работу
            }
        }




            //////////////////////////////////////////////////////////
           // Метод, который формирует имена маленьких дбф-файлов  //
          // если нету сопоставления для этого провайдера         //
         // Вход: ID провайдера Выход: имя файла                 //
        //////////////////////////////////////////////////////////
        private string GetProviderFileName(string providerId)
        {
            if (_providerMappings.TryGetValue(providerId, out var name))
            {
                return $"{name}.dbf"; // Всегда добавляем .dbf
            }
            return $"k0{(providerId.Length >= 4 ? providerId.Substring(providerId.Length - 4) : providerId)}.dbf";
        }




           //////////////////////////////////////////////////////////////////////////
          // Метод загрузки актуальных провайдеров в переменную _actualProviders  //
         // Вход: ничего Выход: ошибка чтения из uprcom.dbf, либо null           //
        //////////////////////////////////////////////////////////////////////////
        public string LoadActualProviders()
        {
            // 1. Если данные уже были загружены - возвращаемся 
            if (_actualProviders.Count > 0) return null;

            // 2. Вызываем метод чтения данных из файла
            var providers = _dbfHandler.LoadUniqueProvidersFast();

            // 3. Если возникли какие-либо ошибки - выходим, возвращая текст ошибки
            if (_dbfHandler.NotReadyFile) return (_dbfHandler.ErrorMessage);

            // 4. Записываем в переменную уникальные пары id-имя провайдера и выходим
            _actualProviders = providers;
            return null;
        }




           ///////////////////////////////////////////////////////////////////////
          // Метод записи данных в файл ignorelist.txt                         //
         // Вход: список id для записи Выход: ошибка записи файла, либо null  //
        ///////////////////////////////////////////////////////////////////////
        public string SaveToFileIgnoreTxt(List<string> ignoreProviderIds)
        {
            try
            {
                // 3. Формируем шапку файла
                var linesToSave = new List<string>
                {
                "### Файл-список провайдеров, которые будут пропускаться при обработке файла uprcom.dbf",
                "### IgnoreList Config ###",
                "### Формат: ID     #Комментарий (не обязательно), например Имя провайдера (имя файла)",
                "### Файл создан: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ""
                };

                var tempVar = new HashSet<string>(capacity: 50, StringComparer.Ordinal);

                // 4. Добавляем игнорируемых провайдеров или сообщение об их отсутствии
                if (ignoreProviderIds.Count == 0)
                {
                    linesToSave.Add("### Нет провайдеров для пропуска");
                }
                else
                {
                    foreach (var id in ignoreProviderIds)
                    {
                        var line = new StringBuilder()
                            .AppendFormat("{0,-18}   # {1,-30}", id, _actualProviders[id]);

                        if (_providerMappings.TryGetValue(id, out var mappedFile))
                        {
                            line.AppendFormat("({0}.dbf)", mappedFile);
                        }

                        linesToSave.Add(line.ToString());
                        tempVar.Add(id);
                    }
                }

                // 5. Сохраняем в ignorelist.txt
                _ignoreHandler.SafeFileRewrite(linesToSave);

                // 6. если запись не удалась - возвращаем ошибку
                if (_ignoreHandler.NotReadyFile) return _ignoreHandler.ErrorMessage;

                // 7. Если запись прошла нормально, то в переменную записываем новый список игнорируемых провайдеров
                _ignoredProviders = tempVar;
                return null;

            }
            catch (Exception ex)
            {
                // 8 Сообщение об ошибки при записи
                return $"При формировании файла ignorelist.txt произошла ошибка: {ex}";
            }
        }




            //////////////////////////////////////////////////////////////////////////
           // Метод обновления данных в файле provider_names.txt                   //
          // Вход: флаг надо ли пересоздать файл (true) или дописать строчки (false)
         // Выход: сообщение об ошибке или результате обновления                 //
        //////////////////////////////////////////////////////////////////////////
        public string UpdateProviderNamesFile(bool regenerateFile)
        {
            // 1. Проверка доступности файла для записи
            try
            {
                var linesToSave = new List<string>();

                // Если в файле 0 строк или в переменной 0 сопоставлений - ставим флаг пересоздания файла
                if (CountProviderMappings == 0 ) regenerateFile = true;

                // Если файл пересоздается, то очищаем переменную _providerMappings и добавляем стартовый заголовок
                if (regenerateFile)
                {
                    _providerMappings.Clear();  // Очищаем все старые сопоставления
                    linesToSave.Add("# Сопоставления для переименования провайдеров");
                    linesToSave.Add("# Формат строк: ID имя_файла # Комментарий");
                    linesToSave.Add($"# Файл сгенерирован {DateTime.Now:dd.MM.yyyy HH:mm}");
                    linesToSave.Add("");
                    linesToSave.Add(new string('-', 40));
                }
                else
                {
                    linesToSave.AddRange(_mappingHandler.ReadRawLines());
                }

                // 3. Находим ID провайдеров, которые есть в _actualProviders, но отсутствуют в _providerMappings
                var missingIds = _actualProviders.Keys.Except(_providerMappings.Keys).ToList();

                // Если новых сопоставлений нет - то выходим, так как добавлять нечего
                if (missingIds.Count == 0)
                {
                    return "Новых сопоставлений для добавления не найдено";
                }

                // В цикле добавляем в конец файла все недостающие сопоставления
                foreach (var id in missingIds)
                {
                    string fileName = GenerateProviderFileName(_actualProviders[id]);
                    linesToSave.Add($"{id} {fileName}".PadRight(50) + $"# {_actualProviders[id]}");
                }

                // исправляем в строках возможные дубликаты ид и имен
                var (validLines, newMappings) = ProcessProviderNamesLines(linesToSave, out _);

                // 5. Записываем файл
                _mappingHandler.SafeFileRewrite(validLines);

                // 6. если запись не удалась - возвращаем ошибку
                if (_mappingHandler.NotReadyFile) return _mappingHandler.ErrorMessage;

                // 7. Если запись прошла нормально, то в переменную записываем новый список игнорируемых провайдеров
                _providerMappings = newMappings;

                // Возвращаем сообщение об успешном добавлении/создании сопоставлений
                return $"Файл provider_names.txt {(regenerateFile ? "создан" : "обновлён")}. Добавлено {missingIds.Count} записей";
            }
            catch (Exception ex)
            {
                return $"При формировании файла provider_names.txt произошла ошибка: {ex}";
            }
        }




          ///////////////////////////////////////////////////////////
         // освобождение потока (перед закрытием окна, например)  //
        ///////////////////////////////////////////////////////////
        public void Dispose()
        { 
            _dbfHandler.Unlock();
        }   





        ///////////////////////////////////////////////////////////////////////////
        // Генерация имен файлов по следующим правилам:
        // 1) Префиксы отправляются в конец названия файла, они и их трансляция заранее предопределены
        // 2) Пробелы заменяются на символ подчеркивания
        // 3) Все переводится в нижний регистр
        // 4) Все буквы кирилицы заменются на аналогичные латиницы
        // 5) Удаляются все символны кроме a-z 0-9 _
        // 6) Если имяфайла начинается с цифры -то предписываем "f_"
        ///////////////////////////////////////////////////////////////////////////
        private string GenerateProviderFileName(string input)
        {
            // Сопоставление префиксов и их транслитераций
            var prefixTranslit = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                {"АО", "ao"}, {"ООО", "ooo"}, {"ДНТ", "dnt"}, {"ЖК", "jk"}, {"ЖСК", "jsk"},
                {"ИП", "ip"}, {"РКЦ", "rkc"}, {"СНО", "sno"}, {"СНТ", "snt"}, {"ТСЖ", "tsj"},
                {"ТСН", "tsn"}, {"УК", "uk"}, {"ЗАО", "zao"}, {"МУП", "mup"}
                };

            // Встроенная транслитерация для остального текста
            var translitMap = new Dictionary<char, string>
                {
                {'а', "a"}, {'б', "b"}, {'в', "v"}, {'г', "g"}, {'д', "d"},
                {'е', "e"}, {'ё', "e"}, {'ж', "j"}, {'з', "z"}, {'и', "i"},
                {'й', "y"}, {'к', "k"}, {'л', "l"}, {'м', "m"}, {'н', "n"},
                {'о', "o"}, {'п', "p"}, {'р', "r"}, {'с', "s"}, {'т', "t"},
                {'у', "u"}, {'ф', "f"}, {'х', "h"}, {'ц', "c"}, {'ч', "ch"},
                {'ш', "sh"}, {'щ', "sch"}, {'ъ', ""}, {'ы', "y"}, {'ь', ""},
                {'э', "e"}, {'ю', "yu"}, {'я', "ya"},
                {' ', "_"}
                };

            var random = new Random();

            // 1. Защита от пустого ввода (единственная проверка)
            if (string.IsNullOrWhiteSpace(input))
                return $"no_name_{random.Next(1, 100)}";

            // 2. Удаление ВСЕХ цифр в начале строки
            input = Regex.Replace(input, @"^\d+", "").TrimStart();

            // 3. Перенос префиксов в конец
            string prefix = "";
            foreach (var p in prefixTranslit.Keys.OrderByDescending(p => p.Length))
            {
                if (input.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                {
                    prefix = "_" + prefixTranslit[p];
                    input = input.Substring(p.Length).TrimStart();
                    break;
                }
            }

            // 4. Транслитерация основной части
            var sb = new StringBuilder();
            foreach (char c in input.ToLower())
            {
                if (translitMap.TryGetValue(c, out string translit))
                    sb.Append(translit);
                else if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                    sb.Append(c);
            }

            // Формирование результата
            string result = Regex.Replace(sb.ToString(), @"_+", "_").Trim('_');

            // Ограничиваем длину основной части (без префикса) 25 символами
            if (result.Length > 25)
                result = result.Substring(0, 25).Trim('_');

            // Добавляем префикс только если есть основное имя
            return string.IsNullOrEmpty(result)
                ? $"no_name_{random.Next(1, 10001)}"
                : $"{result}{prefix}";
        }




        



















        }
}