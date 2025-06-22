using DotNetDBF;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;



namespace Uprcom1
{
    // Переменная состояния файла и данных в файле
    public enum FileStatus
    {
        ReadyForRW,     // Доступен для чтения-записи
        NotFound,       // Файл не существует
        Locked,         // Файл занят другой программой
        Invalid,        // Структура/содержимое невалидны
        IOError         // Другие ошибки ввода-вывода
    }





      ///////////////////////////////////////////////////////
     // Базовый класс с общими методами работы с файлами  //
    ///////////////////////////////////////////////////////
    public abstract class FileHandler
    {
         //////////////////////////////////////////////////
        // Классовые переменные, общие для всех файлов  //
        public string ErrorMessage { get; protected set; }
        // Путь к файлу
        public string FilePath { get; protected set; }
        // Имя файла
        public string FileName { get; }
        // Поток для блокировки
        protected FileStream _fileLock;
        // Основное свойство статуса
        public FileStatus Status { get; protected set; }
        // Выносим кодировку в константу
        protected static readonly Encoding DbfEncoding = Encoding.GetEncoding(866);






        ///////////////////////////////////////////////////
        // Переменные для внешних методов:
        // Не готовность файла (операция прошла с ошибками)
        public bool NotReadyFile => Status != FileStatus.ReadyForRW;




        protected FileHandler(string filePath, string fileName)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Путь к файлу не может быть пустым");

            FilePath = filePath;
            FileName = fileName;
            ErrorMessage = string.Empty;
            Status = FileStatus.NotFound; // Изначальный статус
        }

         /////////////////////////////////////////////////////////////////////////////
        // Метод, устанавливающий статус и записывающий в переменную текст ошибки  //
        public void SetError(string message, FileStatus status = FileStatus.Locked)
        {
            Status = status;
            ErrorMessage = message;
        }




         /////////////////////////////////////////////////////////////////////
        // Метод проверки файла на запись или чтение (входная переменная)  //
        private void CheckAccess(FileAccess fileAccess)
        {
            // 1. Проверяем, доступен ли файл для записи
            try
            {
                using (var stream = File.Open(FilePath, FileMode.Open, fileAccess, FileShare.Read))
                    // 2. Статус файла - готов для чтения-записи, текст ошибки null (ошибки нету)
                    SetError(message: null, status: FileStatus.ReadyForRW);   
            }
            // 3. При возникновении ошибок меняем статус и заносим текст ошибки в переменную ErrorMessage
            catch (Exception ex)
            {
                HandleFileError(ex);
            }
        }



         ///////////////////////////////////////////////////////////////////////////////
        // Совмещённая проверка наличия и чтения файла (для DBF и старта программы)  //
        public void CheckExistenceAndRead() => CheckAccess(FileAccess.Read);



         ///////////////////////////////////////////////////////////
        // Отдельная проверка записи (для редактируемых файлов)  //
        public void CheckWriteAccess() => CheckAccess(FileAccess.Write);


         ///////////////////////////////////////////////////////////////////
        // Метод вызова проверки файла на запись и возврата кода ошибки  //
        public string GetWriteAccessStatus()
        {
            CheckAccess(FileAccess.Write);
            return ErrorMessage;
        }



        ///////////////////////////////////////////////////////////////////////
        //  Блокирует файл только для чтения (для субкласса DbfFileHandler)  //
        public void LockForRead()
        {
            // 1. Если не готов к чтению или уже был заблокирован - выходим без действий
            if (_fileLock != null || NotReadyFile) return;

            // 2. Блокируем файл, если ошибка то обнуляем блокировку и меняем статус
            try
            {
                _fileLock = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                // 3. При успешной блокировке устанавливаем статус на Готов и обнуляем текст ошибки (ее нету)
                SetError(message: null, status: FileStatus.ReadyForRW);   
            }
            // 4. При любой ошибки (в т.ч. файл отсуствует) - освобождаем файл, меняем статус на соотвествующий
            // и записываем в переменную ErrorMessage текст ошибки
            catch (Exception ex)
            {
                _fileLock = null;
                HandleFileError(ex);
            }
        }



         //////////////////////////////////////////////////////////////////////
        //  Снимает блокировку файла (только для субкласса DbfFileHandler)  //
        public void Unlock()
        {
            _fileLock?.Dispose();
            _fileLock = null;
        }




         /////////////////////////////////////////////////////////////////////
        // Основной метод чтения строк (ленивый)
        public IEnumerable<string> ReadRawLines()
        {
            try
            {
                return File.ReadLines(FilePath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                HandleFileError(ex);
                return Enumerable.Empty<string>();
            }
        }



        
         /////////////////////////////////////////////////////////////////////
        // Фильтрация комментариев и пустых строк
        public IEnumerable<string> GetFilteredLines()
        {
            return ReadRawLines()
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Where(line => !line.TrimStart().StartsWith("#"))
                .Select(line => line.Split('#')[0].Trim()); // ← Выбираем часть ДО символа #
        }




         //////////////////////////////////////////////////////////////////////
        // Метод перезаписи файла через временный файл
        public void SafeFileRewrite(IEnumerable<string> content)
        {
            string tempPath = null;
            try
            {
                // Создем путь для временного файла в той же папке, где и перезаписываемый
                tempPath = Path.Combine(Path.GetDirectoryName(FilePath), $"tmp_{Guid.NewGuid()}");
                // Записываем временный файл и сразу перезаписываем нужный
                File.WriteAllLines(tempPath, content, Encoding.UTF8);
                // Если целевого файла нет - просто перемещаем временный файл
                if (!File.Exists(FilePath))
                {
                    File.Move(tempPath, FilePath);
                }
                else
                {
                    // Если файл существует - заменяем его
                    File.Replace(tempPath, FilePath, null);
                }
                // Если все нормально -выставляем статус файла Ок и обнуляем переменную ошибки
                SetError(message: null, status: FileStatus.ReadyForRW);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||  ex is SecurityException)
            {
                // Если при записи возникли ошибки - выставляем статус файла и заносим в переменную ошибок
                HandleFileError(ex);
            }
            finally
            {
                if (tempPath != null && File.Exists(tempPath))
                    try { File.Delete(tempPath); } catch { /* Игнорируем ошибки удаления */ }
            }
        }




        //////////////////////////////////////////////////////////////////////
        // Метод отлова ошибок при работе с файлами
        public void HandleFileError(Exception ex)
        {
            if (ex is UnauthorizedAccessException)
                SetError($"Нет прав доступа к файлу {FileName}.");

            else if (ex is IOException ioEx && ioEx.HResult == -2147024864)
                SetError($"Файл {FileName} занят другой программой (заблокирован).");

            else if (ex is FileNotFoundException)
                SetError($"Файл {FileName} не найден.", FileStatus.NotFound);

            else if (ex is SecurityException)
                SetError($"Доступ к файлу {FileName} запрещён из-за ограничений безопасности.");

            else if (ex is IOException)
                SetError($"Ошибка ввода-вывода при работе с файлом {FileName}.", FileStatus.IOError);

            else SetError($"Неизвестная ошибка файла {FileName}: {ex.Message}");

        }




        /////////////////////////////////////////////////////////////////////////////
        // Метод расшифровки ошибок невалидных записей и дубликатов
        // Вход: есть ли  невалдиные записи и есть ли ошибки Выход: текст ошибки
        public void ErrorToText(bool HasInvalidEntries, bool HasDuplicate)
        {
            // Если в статусе файла уже есть ошибка -  возвращаемся
            if (NotReadyFile) return;

            // Иначе определаем ошибку данных в файле и возвращаем ее
            if (HasInvalidEntries && HasDuplicate)
                SetError(message: $"В файле {FileName} имеются неверные записи и дубликаты.", status: FileStatus.Invalid);
            else if (HasInvalidEntries)
                SetError(message: $"В файле {FileName} имеются неверные записи.", status: FileStatus.Invalid);
            else if (HasDuplicate)
                SetError(message: $"В файле {FileName} имеются дубликаты.", status: FileStatus.Invalid);
            // Если все нормально -выставляем статус файла Ок и обнуляем переменную ошибки
            else SetError(message: null, status: FileStatus.ReadyForRW);
        }
    }









      /////////////////////////////////////////////////////////////////////////
     // Первый субкласс DbfFileHandler для работы с файлом uignorelist.txt  //
    /////////////////////////////////////////////////////////////////////////
    public class IgnoreListHandler : FileHandler
    {
        private static readonly string FixedPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "ignorelist.txt");

        public IgnoreListHandler() : base(FixedPath, "ignorelist.txt")
        {
            CheckExistenceAndRead();
        }
    }













      //////////////////////////////////////////////////////////////////////////////////
     // Второй субкласс ProviderNamesHandler для работы с файлом provider_names.txt  //
    //////////////////////////////////////////////////////////////////////////////////
    public class ProviderNamesHandler : FileHandler
    {
        private static readonly string FixedPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "provider_names.txt");

        public ProviderNamesHandler() : base(FixedPath, "provider_names.txt")
        {
            CheckExistenceAndRead();
        }
    }











      ////////////////////////////////////////////////////////////////////
     // Третий субкласс DbfFileHandler для работы с файлом uprcom.dbf  //
    ////////////////////////////////////////////////////////////////////
    public class DbfFileHandler : FileHandler
    {

        private string _outFolderPath; // Путь к папке Out (или другой если задать другое имя в VerifyOrCreateFolder)

        private DBFField[] _cachedFields; // Добавляем поле, в котором записываем структуру полей uprcom.dbf для записи маленьких дбф-файлов

        private static readonly string FixedPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "uprcom.dbf");
        public DbfFileHandler() : base(FixedPath, "uprcom.dbf") { } // Изначально путь с папкой программы




         /////////////////////////////////////////////////////////////////////////
        // Метод для изменения пути (например, при смене папки пользователем)  //
        public void SetFolder(string folderPath)
        {
            // Сначала снимаем блокировку предыдущего файла
            Unlock();

            // проверяем чтобы путь был не пустой
            if (string.IsNullOrEmpty(folderPath))
                throw new ArgumentException("Путь к папке не может быть пустой");

            // Формируем полный путь к uprcom.dbf
            FilePath = Path.Combine(folderPath, "uprcom.dbf");

            // 1. Автоматическая проверка после изменения пути
            CheckExistenceAndRead();
            if (NotReadyFile) return; // Прекращаем если файл недоступен

            // 2. Проверяем его структуру
            ValidateStructure();
            if (NotReadyFile) return;  // Прекращаем если структура файла неверная

            // 3. Блокируем файл
            LockForRead();
        }




         //////////////////////////////////////////////
        // Метод проверки полей в файле uprcom.dbf  //
        public void ValidateStructure()
        {
            // Если файл не готов к чтению - выходим без проверки
            if (NotReadyFile) return;

            // Пытаемся проверить структуру файла
            try
            {
                using (var fsCheck = new FileStream(FilePath, FileMode.Open, FileAccess.Read))
                using (var reader = new DBFReader(fsCheck) { CharEncoding = DbfEncoding })
                {
                    // Проверка количества полей
                    if (reader.Fields.Length != 54)
                    {
                        SetError(message: "Ошибка: В файле uprcom.dbf должно быть 54 поля", status: FileStatus.Invalid);
                        return;
                    }

                    // Проверка обязательных полей
                    var requiredFields = new[] { "PROVIDER", "PROVNAME", "ADDRESID", "KNASP", "KYLIC",
                                   "NDOM", "NKORP", "NKW", "NKOMN", "NOTE", "MONTHDBT" };
                    var missingFields = requiredFields
                        .Where(f => !reader.Fields.Any(rf => rf.Name.Equals(f, StringComparison.OrdinalIgnoreCase)))
                        .ToList();

                    if (missingFields.Any())
                    {
                        // Если не хватает каких-то полей, то статус = ошибка структуры и в тексте ошибке перечень нехватающих полей
                        SetError(message: $"Ошибка: Отсутствуют обязательные поля: {string.Join(", ", missingFields)}", status: FileStatus.Invalid);
                    }

                    if (reader.RecordCount == 0)
                    {
                        SetError(message: "В файле uprcom.dbf нет ни одной записи.", status: FileStatus.Invalid);
                    }
                    // Если все нормально то статус остается прежним Status = ReadyForRW
                    // выходим из метода
                }
            }
            catch (Exception ex)
            {
                // Если ошибки при доступе к файлу - то статус = заблокирован
                HandleFileError(ex);
            }
        }




        //////////////////////////////////////////////////////////
        // Метод проверки/создания папки и проверки прав записи //
        // Вход: folderName - имя проверяемой папки             //
        // Выход: сообщение об ошибке (если есть)               //
        public string VerifyOrCreateFolder(string folderName = "Out")
        {
            try
            {
                // 1. Создаем полный путь к папке
                _outFolderPath = Path.Combine(Path.GetDirectoryName(FilePath), folderName);

                // 2. Создаем папку, если не существует
                Directory.CreateDirectory(_outFolderPath);

                // 3. Тест записи/удаления временного файла
                string testFile = Path.Combine(_outFolderPath, $"test_{Guid.NewGuid()}.tmp");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);

                // 4. Успех - возвращаем пустое значение текста ошибки
                return null;
            }
            // 5. Если есть ошибки - отлавливаем их и возвращаем их текстовку
            catch (UnauthorizedAccessException)
            {
                return $"Нет прав доступа к папке {folderName}";
            }
            catch (IOException ex) when (ex is DirectoryNotFoundException || ex is PathTooLongException)
            {
                return $"Ошибка пути: {ex.Message}";
            }
            catch (SecurityException ex)
            {
                return $"Ошибка безопасности: {ex.Message}";
            }
            catch (IOException ex)
            {
                return $"Ошибка ввода-вывода: {ex.Message}";
            }
            catch (Exception ex)
            {
                return $"Неизвестная ошибка: {ex.Message}";
            }
        }




          /////////////////////////////////////////////////////////////////////////////
         // Метод записи дбф-файла с фиксированными параметрами кодировки
        // Вход: строки для записи, выход - булево значение ошибка/неошибка
        public bool TryWriteDbfRecords(string fileName, IEnumerable<object[]> records, out string errorMessage)
        {
            errorMessage = null;
            string filePath = Path.Combine(_outFolderPath, fileName);

            try
            {
                using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536))
                using (var writer = new DBFWriter(fs))
                {
                    writer.CharEncoding = DbfEncoding;
                    writer.LanguageDriver = 0x65;
                    writer.Fields = _cachedFields; // Используем кэшированную структуру полей

                    foreach (var record in records)
                        writer.WriteRecord(record);
                }
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Ошибка при создании файла {fileName}: {ex.Message}";
                return false;
            }
        }




         /////////////////////////////////////////////////////////////////////////////
        // Метод чтения записей из DBF-файла с обновлением _cachedFields.
        public (IEnumerable<object[]> Records, DBFField[] Fields) ReadDbfRecordsLazy()
        {
            _cachedFields = null;  // Обнуляем переменную структуры полей - ее прочитаем заново
            // если в dbf есть ошибки чтения - то возвращаем пустые значения
            if (NotReadyFile) return (new List<object[]>(), null);

            try
            {
                // Открытие файла и создание reader'а
                var fileStream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var reader = new DBFReader(fileStream)
                {
                    CharEncoding = DbfEncoding
                };

                // Сохранение структуры полей
                _cachedFields = (DBFField[])reader.Fields.Clone();

                // Ленивое чтение записей
                IEnumerable<object[]> ReadRecordsLazy()
                {
                    try
                    {
                        object[] record;
                        while ((record = reader.NextRecord()) != null)
                        {
                            yield return record;
                        }
                    }
                    finally
                    {
                        // Гарантированное освобождение ресурсов
                        reader.Dispose();
                        fileStream.Dispose();
                    }
                }

                return (ReadRecordsLazy(), _cachedFields);
            }
            catch (Exception ex)
            {
                HandleFileError(ex);
                return (Enumerable.Empty<object[]>(), null);
            }
        }




          ////////////////////////////////////////////
         // Запись лог-файла
        // Вход: содержание Выход: успех/поломка
        public string TryWriteLogFile(string content)
        {
            // Если нет содержимого - то и записывать нечего
            if (string.IsNullOrWhiteSpace(content)) return null;

            try
            {
                string logFilePath = Path.Combine(Path.GetDirectoryName(FilePath), $"_result_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.WriteAllText(logFilePath, content, Encoding.UTF8);
                return null;
            }
            catch (Exception ex)
            {
                return $"Ошибка при записи лог-файла: {ex.Message}";
            }
        }



         ////////////////////////////////////////////////////////////////////////////////
        // Метод быстрой загрузки пар id-имя провайдера из файла uprcom.dbf
        public Dictionary<string, string> LoadUniqueProvidersFast()
        {
            var providers = new Dictionary<string, string>(capacity: 320);

            // Если файл уже проверен и заблокирован, пропускаем повторную валидацию
            if (NotReadyFile)  return null;

            try
            {
                using (var fileStream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var reader = new DBFReader(fileStream) { CharEncoding = DbfEncoding })
                {

                    int idxProvider = Array.FindIndex(reader.Fields, f =>
                    f.Name.Equals("PROVIDER", StringComparison.OrdinalIgnoreCase));
                    int idxProvName = Array.FindIndex(reader.Fields, f =>
                        f.Name.Equals("PROVNAME", StringComparison.OrdinalIgnoreCase));

                    object[] record;
                    while ((record = reader.NextRecord()) != null)
                    {
                        string id = (record[idxProvider] as string)?.Trim() ?? "";
                        string name = (record[idxProvName] as string)?.Trim() ?? "";

                        if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name) && !providers.ContainsKey(id))
                            providers.Add(id, name);
                    }

                    if (providers.Count== 0) SetError(message: "В файле uprcom.dbf нет ни одной записи.", status: FileStatus.Invalid);
                }

                return providers;
            }
            catch (Exception ex)
            {
                HandleFileError(ex);
                return null;
            }
        }





    }



















}
