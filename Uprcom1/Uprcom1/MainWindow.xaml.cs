using System;
using System.Windows;
using System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;
using System.Windows.Input;


namespace Uprcom1
{

    public partial class MainWindow : Window
    {
        // Класс-переменная для хранения настроек одинаковых методов проверки файлов ignorelist.txt и provider_names.txt
        private class CheckFileSettings
        {
            public System.Windows.Controls.Label ErrorLabel { get; set; }
            public Func<string> LoadMethod { get; set; }
            public Func<int> CountMethod { get; set; }
            public Func<FileStatus> StatusMethod { get; set; }
            public Func<string> SaveStatusMethod { get; set; }
            public Func<string> RepairMethod { get; set; }
            public string FileDescription { get; set; }
        }





        // Переменные
        private readonly FileDataManager _fileManager;

        public MainWindow()
        {
            InitializeComponent();
            _fileManager = new FileDataManager();
            InitializeDataFromFiles();
        }

        // Процедура инициализации данных: считываение всех возможных данных из файлов
        private void InitializeDataFromFiles()
        {
            // 1. Вызываем процедуру проверки наличия uprcom.dbf и выводим ошибки только на форму
            SetFolderDBF(_fileManager.SelectedFolder);

            // 2. Считываем данные из ignorelist.txt и если есть ошибки, выводим в виде сообщения и на форму
            var settings = new CheckFileSettings
            {
                ErrorLabel = LblFileIgnoreError,
                LoadMethod = _fileManager.LoadIgnoreList,
                CountMethod = () => _fileManager.CountIgnoreID,
                StatusMethod = () => _fileManager.IgnoreFileStatus,
                SaveStatusMethod = _fileManager.StatusSaveIgnoreList,
                RepairMethod = _fileManager.RepairIgnoreList,
                FileDescription = "ignorelist.txt"
            };

            CheckFileIgnoreMapping(settings);

            // 3. Считываем данные из provider_names.txt и если есть ошибки, выводим в виде сообщения и на форму
            settings = new CheckFileSettings
            {
                ErrorLabel = LblFileNamesError,
                LoadMethod = _fileManager.LoadProviderMappings,
                CountMethod = () => _fileManager.CountProviderMappings,
                StatusMethod = () => _fileManager.MappingFileStatus,
                SaveStatusMethod = _fileManager.StatusSaveMappingeList,
                RepairMethod = _fileManager.RepairMappingListFile,
                FileDescription = "provider_names.txt"
            };

            CheckFileIgnoreMapping(settings);
        }




        // Кнопка закрытия окна
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();




        // Процедура проверяющая наличие файла uprcom.dbf
        private void SetFolderDBF(string path)
        {
            // 1. В текст бокс выводим путь к файлу
            txtboxPath.Text = path;

            // 2. Вызываем метод проверки наличия и доступности файла, если есть ошибки - выводим их на форму
            string error = _fileManager.SetSelectedFolder(path);

            if (error != null)
                ShowError(error, showPopup: false, errorLabel: lblWarning);  // Показываем метку с ошибкой, но сообщение не выводим
            else
                HideError(lblWarning);  // Скрываем метку с ошибкой
        }





        // Процедура проверяющая наличие файла ignorelist.txt/provider_names.txt
        private void CheckFileIgnoreMapping(CheckFileSettings settings)
        {
            // 1. Скрываем ошибку на UI
            HideError(settings.ErrorLabel);

            // 2. Загружаем данные и проверяем статус
            string error = settings.LoadMethod();

            // 3. Если нет ошибок и есть записи - выход
            if (error == null && settings.CountMethod() > 0) return;

            // 4. Корректировка сообщения для пустого файла
            error = error ?? "В файле ignorelist.txt нет валидных записей.";

            // 4. Обработка ошибок файла (но не неверных данных)
            if (settings.StatusMethod() != FileStatus.Invalid)
            {
                ShowError(error, errorLabel: settings.ErrorLabel);
                return;
            }

            // 6. Обработка ошибок данных
            if (settings.SaveStatusMethod() != null)
            {
                // Если есть ошибки данных и перезапись невозможно, то выводим сообщение об этом и записываем ошибку данных в UI
                ShowWarning($"В файле {settings.FileDescription} есть ошибки, но файл недоступен для исправления.");
                ShowError(error, false, settings.ErrorLabel);
                return;
            }

            // 7. Диалог с пользователем
            var repairDialogResult = MessageBox.Show(
                $"{error}\nИсправить записи в файле?",
                "Неверные записи в файле",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (repairDialogResult != MessageBoxResult.Yes)
            {
                ShowError(error, false, settings.ErrorLabel);
                return;
            }

            // 8. Попытка исправления
            error = settings.RepairMethod();
            if (error != null)
            {
                ShowWarning($"Перезапись файла {settings.FileDescription} не удалась:\n{error}");
                ShowError(error, false, settings.ErrorLabel);
                return;
            }

            // 9. Успешное исправление
            string resultMessage = $"Все некорректные записи в файле {settings.FileDescription} удалены.";
            if (settings.CountMethod() == 0)
            {
                ShowError($"В файле {settings.FileDescription} нет валидных записей.", false, settings.ErrorLabel);
                resultMessage += " Но валидных записей не осталось.";
            }

            MessageBox.Show(resultMessage);
        }




        // Вспомогательный метод для вывода ошибок в UI и в сообщении (зависит от showPopup)
        private void ShowError(string message, bool showPopup = true, System.Windows.Controls.Label errorLabel = null)
        {
            var targetLabel = errorLabel ?? LblFileIgnoreError;
            targetLabel.Content = message;
            targetLabel.Visibility = Visibility.Visible;

            if (showPopup) ShowWarning(message);
        }




        // Вспомогательный метод - окно с предупреждением
        private void ShowWarning(string message)
        {
            MessageBox.Show(message, "Внимание!", MessageBoxButton.OK, MessageBoxImage.Warning);
        }




        // Вспомогательный метод, который скрывает метки с ошибками
        private void HideError(System.Windows.Controls.Label errorLabel)
        {
            errorLabel.Visibility = Visibility.Collapsed;
        }



        // Метод при нажатии кнопки - выбор папки с файлом uprcom.dbf
        private void BtnSelectFile_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Выберите папку";
                dialog.SelectedPath = _fileManager.SelectedFolder; // Берём текущий путь из FileDataManager
                dialog.ShowNewFolderButton = true;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    SetFolderDBF(dialog.SelectedPath); // Обновляем путь в классе
                }
            }
        }



        // Основная обработка файла uprcom.dbf по нажатию кнопки Обработка
        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            // 1. Если в основном dbf файле какие-то ошибки - выводим предупреждение и останавливаем процедуру
            if (_fileManager.DbfFileNotReady)
            {
                ShowWarning(_fileManager.TextErrorDbfFile);
                return;
            }

            // 2. Проверяем наличие и доступность для записи папки Out, если ошибки - выводим их и останавливаем процедуру
            string error = _fileManager.CheckFolderOutEnvironment();
            if (error != null)
            {
                ShowWarning(error);
                return;
            }

            // 3. Создаем и показываем окно ожидания
            var progressWindow = new WindowProgressBar { Owner = this };    // Устанавливаем владельца (текущее окно)
            progressWindow.Show();

            try
            {
                // 4. Вызываем метод формирования dbf-файлов (входные данные: нужно ли проверять дубликаты, нужно ли очищать Monthdbt) и при ошмбке в файле останавливаем процедуру
                var (messageProcedure, passProcedure) = _fileManager.ProcessDbfFile((bool)ChkDuplicate.IsChecked, (bool)chkClearMonthdbt.IsChecked);

                // 5. Если не выполнилась запись файлов. то показываем сообщение и на ui выводим ошибку
                if (!passProcedure)
                {
                    ShowError(messageProcedure, showPopup: true, errorLabel: lblWarning);  // Показываем метку с ошибкой, выводим сообщение
                    return;
                }

                // 6. Закрываем окно прогресс-бара
                progressWindow.Close();

                // 7. Выводим итоговое сообщение
                MessageBox.Show(messageProcedure, "Готово", MessageBoxButton.OK, MessageBoxImage.Information);

            }
            // 8. Если есть ошибки - закрываем окно прогресс-бара и выводим ошибку
            catch (Exception ex)
            {
                progressWindow.Close();
                MessageBox.Show($"Произошла ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }


        }




        // Вызов окна справки
        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            var windowHelp = new WindowHelp( _fileManager)
            {
                Owner = this, // Главное окно - владелец
                ShowInTaskbar = false, // Скрыть с панели задач
                WindowStartupLocation = WindowStartupLocation.CenterOwner // Позиционирование
            };

            windowHelp.ShowDialog(); // Модальное окно
        }




        // Метод закрывающий открытые файлы при выходе из программы
        protected override void OnClosed(EventArgs e)
        {
            _fileManager?.Dispose(); // Явное освобождение
            base.OnClosed(e);
        }


        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();           // встроенный метод Window для перетаскивания
        }

        private void OnMinimizeClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }
    }
}
