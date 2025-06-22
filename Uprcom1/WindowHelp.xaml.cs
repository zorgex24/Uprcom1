using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;


namespace Uprcom1
{

    // Переменные для таблицы изменения пропуска поставщиков
    public class ProviderItem : INotifyPropertyChanged
    {
        private bool _isIgnored;
        private string _nameN;
        private string _id;
        private bool _isFixed;

        public bool IsIgnored
        {
            get => _isIgnored;
            set { _isIgnored = value; OnPropertyChanged(); }
        }

        public string NameN
        {
            get => _nameN;
            set { _nameN = value; OnPropertyChanged(); }
        }

        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public bool IsFixed
        {
            get => _isFixed;
            set { _isFixed = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }


    /// <summary>
    /// Логика взаимодействия для WindowHelp.xaml
    /// </summary>
    public partial class WindowHelp : Window
    {
        private readonly FileDataManager _fileManager;

        public WindowHelp(FileDataManager fileManager)
        {
            InitializeComponent();
            _fileManager = fileManager;
            if (_fileManager.DbfFileNotReady)
            {
                errorMessage.Text = _fileManager.TextErrorDbfFile;
                mainContent.Visibility = Visibility.Collapsed;
                errorMessage.Visibility = Visibility.Visible;
            }
        }




        // Кнопка закрытия окна справки
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();




        // Событие переключения вкладок
        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Работаем только при переходе на вкладку с кнопками (TxtFilesTab)
            if (e.Source is TabControl tabControl && tabControl.SelectedItem == TxtFilesTab)
            {
                // Если файл доступен И основная панель ещё не скрыта (нет предыдущих ошибок)
                if (!_fileManager.DbfFileNotReady && mainContent.Visibility == Visibility.Visible)
                {
                    // Запускаем метод считывания уникальных id-имя провайдера из uprcom.dbf
                    string error = _fileManager.LoadActualProviders();
                    if (error != null)
                    {
                        // Если ошибка - скрываем панель с кнопками
                        errorMessage.Text = error;
                        mainContent.Visibility = Visibility.Collapsed;
                        errorMessage.Visibility = Visibility.Visible;
                    }
                }
            }
        }





        // Процедура обновления файла provider_names.txt Либо создание его заново, либо дописывание недостающих сопоставлений в конец файла
        private void BtnUpdate_Click(object sender, RoutedEventArgs e)
        {
            bool regenerate = ChkRegen.IsChecked == true;

            // Если галочка пересоздать не стоит и файла нету - то запрашиваем создать ли его?
            if (!regenerate && _fileManager.MappingFileStatus == FileStatus.NotFound)
            {
                MessageBoxResult resultQ = MessageBox.Show(
                    "Файла provider_names.txt не существует. Создать его?",
                    "Отсутствует файл",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                // Если пользователь отказался создавать файл, то выходим из процедуры
                if (resultQ == MessageBoxResult.No) return;
                regenerate = true; // Иначе включаем режим пересоздания
            }
            // Если файл существует, то проверяем его на возможность перезаписи
            else
            {
                if (_fileManager.StatusSaveMappingeList() != null)
                {
                    // Если файл недоступен для записи -сообщаем об этом и выходим
                    MessageBox.Show("Файл provider_names.txt недоступен для перезаписи!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                else
                {
                    // Если файл доступен для записи, то перечитываем данные из файла и если есть ошибка чтения - то запрашиваем пересоздание файла
                    string error = _fileManager.LoadProviderMappings();
                    if (error != null && !regenerate)
                    {
                        MessageBoxResult resultQ = MessageBox.Show(
                            "Файл provider_names.txt невозможно прочитать. Пересоздать его?",
                            "Ошибка чтения",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                        // Если пользователь отказался создавать файл, то выходим из процедуры
                        if (resultQ == MessageBoxResult.No) return;
                        regenerate = true; // Иначе включаем режим пересоздания
                    }
                }
            }

            // Вызываем метод обновления сопоставлений
            string result = _fileManager.UpdateProviderNamesFile(regenerate);

            // Если файл не удалось записать - сообщаем об этом, иначе собщаем результат обновления данных в файле
            if (_fileManager.MappingFileStatus != FileStatus.ReadyForRW)
            {
                MessageBox.Show(result, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(result,
                          regenerate ? "Файл пересоздан" : "Файл обновлён",
                          MessageBoxButton.OK,
                          MessageBoxImage.Information);
            }
                
        }




        // Процедура показа окна редактирования игнорируемых провайдеров в файле ignorelist.txt
        private void BtnEditIgnore_Click(object sender, RoutedEventArgs e)
        {
            // 1. Перезагружаем данные из файла
            _fileManager.LoadIgnoreList();

            // 2. Если файл ignorelist.txt отсуствует - предлагаем создать новый
            if (_fileManager.IgnoreFileStatus == FileStatus.NotFound)
            {
                MessageBoxResult resultQ = MessageBox.Show(
                    "Файла ignorelist.txt не существует. Создать его?",
                    "Отсутствует файл",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                // 3. Если пользователь отказался создавать файл, то выходим из процедуры
                if (resultQ == MessageBoxResult.No) return;
            }
            else  // 4. Иначе проверяем на доступность для записи
            {
                if (_fileManager.StatusSaveIgnoreList() != null)
                {
                    MessageBox.Show("Файл ignorelist.txt недоступен для перезаписи!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            // 5. В таблицу заносим значения из двух файлов
            ProvidersDataGrid.ItemsSource = PrepareProviderItems();

            // 6. Показываем панель редактирования ignorelist.txt
            EditIgnorePanel.Visibility = Visibility.Visible;
            ContentHelp.Visibility = Visibility.Collapsed;
        }




        // Заполнение таблицы данными из файлов для редактирования файла ignorelist.txt
        private ObservableCollection<ProviderItem> PrepareProviderItems()
        {
            var items = new ObservableCollection<ProviderItem>();

            // Фиксированные ID (пример)
            var fixedIds = new HashSet<string> { "8", "9", "610005001000000465", "610005001000000434", "610005001000000437", "610005001000000002",
                "610005001000000004", "610005001000000264", "610005001000000011", "610005001000000244", "610005001000000207", "610005001000001134",
                "610005001000000806", "610005001000000783", "610005001000000726", "610005001000000721", "610005001000000003", "610005001000000565",
                "610005001000000562", "610005001000000548", "610005001000000547", "610005001000000488", "610005001000000487", "610005001000000782",
                "610005001000000762", "610005001000001185", "610005001000000713", "610005001000000538", "610005001000001180", "610005001000001110",
                "610005001000000771", "610005001000000684", "610005001000001189", "610005001000001220", "610005001000001190"};

            // 1. Провайдеры из ignorelist.txt (сверху)
            var ListProvignore = _fileManager.ListignoredProviders;
            var ActualProviders = _fileManager.ActualProvidersList;
            foreach (var id in ListProvignore)
            {
                if (ActualProviders.TryGetValue(id, out var name))
                {
                    items.Add(new ProviderItem
                    {
                        IsIgnored = true,
                        NameN = name,  // Используем NameN вместо Name
                        Id = id,
                        IsFixed = false
                    });
                }
            }

            // 2. Фиксированные провайдеры
            foreach (var fixedId in fixedIds)
            {
                if (!ListProvignore.Contains(fixedId) &&
                    ActualProviders.TryGetValue(fixedId, out var name))
                {
                    items.Add(new ProviderItem
                    {
                        IsIgnored = false,
                        NameN = name,  // Используем NameN
                        Id = fixedId,
                        IsFixed = true
                    });
                }
            }

            // 3. Остальные провайдеры (по алфавиту)
            foreach (var provider in ActualProviders
                .Where(p => !ListProvignore.Contains(p.Key) && !fixedIds.Contains(p.Key))
                .OrderBy(p => p.Value))
            {
                items.Add(new ProviderItem
                {
                    IsIgnored = false,
                    NameN = provider.Value,
                    Id = provider.Key,
                    IsFixed = false
                });
            }

            return items;
        }



        // Запись данных в файл ignorelist.txt
        private void BtnApplyIgnoreList_Click(object sender, RoutedEventArgs e)
        {
            // 1. Получаем и фильтруем провайдеры за один запрос
            var ignoredProviderIds = (ProvidersDataGrid.ItemsSource as IEnumerable<ProviderItem>)?
                     .Where(i => i.IsIgnored)
                     .Select(i => i.Id)                //  Берём только Id
                     .ToList() ?? new List<string>(); //  List<string>

            string error = _fileManager.SaveToFileIgnoreTxt(ignoredProviderIds);

            // Если возникли ошибки при записи - выводим в сообщении иначе выводим сообщение об успешной записи
            if (error != null)
            {
                MessageBox.Show(error, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(
                        _fileManager.CountIgnoreID == 0
                            ? "Список очищен (нет провайдеров для пропуска)"
                            : $"Список содержит {_fileManager.CountIgnoreID} пропускаемых провайдеров",
                        "Готово",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information );
            }

            // 6. Показываем панель справки
            EditIgnorePanel.Visibility = Visibility.Collapsed;
            ContentHelp.Visibility = Visibility.Visible;
        }




        // Выход из окна редактирования файла ignorelist.txt
        private void BtnCancelEdit_Click(object sender, RoutedEventArgs e)
        {
            // Показываем панель справки
            EditIgnorePanel.Visibility = Visibility.Collapsed;
            ContentHelp.Visibility = Visibility.Visible;
        }





        // Метод позволяющий сразу манипулировать флажком в таблице, без него приходится 2 раза кликать на ячейку
        private void DataGrid_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var dataGrid = sender as DataGrid;
            var hit = VisualTreeHelper.HitTest(dataGrid, e.GetPosition(dataGrid));

            if (hit.VisualHit is FrameworkElement element &&
                element.Parent is CheckBox checkBox)
            {
                checkBox.IsChecked = !checkBox.IsChecked;
                e.Handled = true;
            }
        }














        










        
    }
}
